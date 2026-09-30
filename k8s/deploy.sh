#!/usr/bin/env bash
# SessionProjects'i Docker Desktop Kubernetes'e (context: docker-desktop) kurar.
#
#   ./k8s/deploy.sh             -> image build + gözlemlenebilirlik (Helm) + uygulamalar + agent'lar
#   ./k8s/deploy.sh delete      -> uygulamaları ve agent'ları kaldırır (Helm kurulumları kalır)
#   ./k8s/deploy.sh delete-all  -> her şeyi kaldırır (SigNoz, Prometheus/Grafana, metrics-server dahil)
#
# Namespace'ler:
#   observability          SigNoz, kube-prometheus-stack (Prometheus + Grafana), otel-collector
#   sessionprojects        microservice1-api, microservice2-api, rabbitmq
#   sessionprojects-agents MCP server'lar ve agent'lar (sre-advisor-agent dışarıya açık)
set -euo pipefail

CONTEXT="${KUBE_CONTEXT:-docker-desktop}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
kc() { kubectl --context "$CONTEXT" "$@"; }
hc() { helm --kube-context "$CONTEXT" "$@"; }

SIGNOZ_CHART_VERSION="0.143.0"
KPS_CHART_VERSION="91.8.1"

case "${1:-}" in
  delete)
    kc delete namespace sessionprojects-agents sessionprojects --ignore-not-found
    kc delete clusterrole,clusterrolebinding sessionprojects-cluster-reader --ignore-not-found
    exit 0 ;;
  delete-all)
    "$0" delete
    hc uninstall signoz kube-prometheus-stack -n observability --ignore-not-found || true
    kc delete -f "$ROOT/k8s/observability/otel-collector.yaml" --ignore-not-found
    kc delete namespace observability --ignore-not-found
    kc delete -f "$ROOT/k8s/metrics-server/components.yaml" --ignore-not-found
    exit 0 ;;
esac

# Secret değeri: önce ortam değişkeni, yoksa AppHost user secrets (Parameters:<name>)
read_secret() {
  local env_value="$1" user_secret_key="$2"
  if [[ -n "$env_value" ]]; then printf '%s' "$env_value"; return; fi
  dotnet user-secrets list --project "$ROOT/SessionProjects.AppHost" 2>/dev/null \
    | awk -F' = ' -v key="$user_secret_key" '$1 == key { print $2 }'
}

upsert_secret() {
  local namespace="$1" name="$2" value="$3"
  kc -n "$namespace" create secret generic "$name" --from-literal=api-key="$value" \
    --dry-run=client -o yaml | kc apply -f - >/dev/null
}

echo "==> 1/5 Image'lar build ediliyor (compose.yaml)"
docker compose -f "$ROOT/compose.yaml" build

echo "==> 2/5 Gözlemlenebilirlik (Helm)"
helm repo add signoz https://charts.signoz.io >/dev/null 2>&1 || true
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts >/dev/null 2>&1 || true
helm repo update signoz prometheus-community >/dev/null
kc create namespace observability --dry-run=client -o yaml | kc apply -f - >/dev/null
hc upgrade --install kube-prometheus-stack prometheus-community/kube-prometheus-stack \
  --version "$KPS_CHART_VERSION" -n observability \
  -f "$ROOT/k8s/observability/kube-prometheus-stack-values.yaml" --wait --timeout 10m
# --force-conflicts: aşağıdaki kubectl patch'in sahiplendiği alanı Helm'in geri alabilmesi için (server-side apply)
hc upgrade --install signoz signoz/signoz --version "$SIGNOZ_CHART_VERSION" -n observability \
  -f "$ROOT/k8s/observability/signoz-values.yaml" --force-conflicts --wait --timeout 15m
# SigNoz collector'ı chart OpAMP modunda (--manager-config) başlatır; bu modda SigNoz UI'da ilk kullanıcı
# oluşturulana kadar OTLP alıcısı açılmaz. Statik config ile çalıştırarak ingestion'ı hemen başlatıyoruz.
# Chart'ta bunun için bir ayar olmadığından her helm upgrade sonrası yeniden uygulanır.
kc -n observability patch deployment signoz-otel-collector --type=json \
  -p='[{"op":"replace","path":"/spec/template/spec/containers/0/args","value":["--config=/conf/otel-collector-config.yaml"]}]'
kc apply -f "$ROOT/k8s/observability/otel-collector.yaml" -f "$ROOT/k8s/observability/grafana-dashboard.yaml"
kc apply -f "$ROOT/k8s/metrics-server/components.yaml"

echo "==> 3/5 Uygulamalar"
kc apply -f "$ROOT/k8s/apps/00-namespace.yaml"
if ! kc -n sessionprojects get secret rabbitmq-credentials >/dev/null 2>&1; then
  # Şifre repoya yazılmaz; ilk kurulumda rastgele üretilir
  kc -n sessionprojects create secret generic rabbitmq-credentials \
    --from-literal=username=sessionprojects --from-literal=password="$(openssl rand -hex 16)"
fi
kc apply -f "$ROOT/k8s/apps/"

echo "==> 4/5 Agent'lar"
kc apply -f "$ROOT/k8s/agents/00-namespace.yaml"
OPENAI_KEY="$(read_secret "${OPENAI_API_KEY:-}" "Parameters:openai-api-key")"
if [[ -z "$OPENAI_KEY" ]]; then
  echo "HATA: OpenAI anahtarı bulunamadı. OPENAI_API_KEY ortam değişkenini ya da" >&2
  echo "      'dotnet user-secrets set Parameters:openai-api-key <key> --project SessionProjects.AppHost' ayarlayın." >&2
  exit 1
fi
upsert_secret sessionprojects-agents openai-api-key "$OPENAI_KEY"
SIGNOZ_KEY="$(read_secret "${SIGNOZ_API_KEY:-}" "Parameters:signoz-api-key")"
[[ -n "$SIGNOZ_KEY" ]] && upsert_secret sessionprojects-agents signoz-api-key "$SIGNOZ_KEY"
kc apply -f "$ROOT/k8s/agents/"

echo "==> 5/5 Rollout"
# Aynı :latest tag'iyle yeniden build edilen image'ların alınması için pod'ları yenile
kc -n sessionprojects rollout restart deployment/microservice1-api deployment/microservice2-api deployment/case-summary-web >/dev/null
kc -n sessionprojects-agents rollout restart deployment/observability-agent deployment/kubernetes-agent \
  deployment/sre-advisor-agent deployment/case-summary-agent >/dev/null
for d in rabbitmq microservice2-api microservice1-api case-summary-web; do
  kc -n sessionprojects rollout status "deployment/$d" --timeout=180s
done
for d in signoz-mcp grafana-mcp prometheus-mcp kubernetes-mcp observability-agent kubernetes-agent sre-advisor-agent \
         case-summary-agent; do
  kc -n sessionprojects-agents rollout status "deployment/$d" --timeout=180s
done

echo
kc -n sessionprojects-agents get svc sre-advisor-agent
echo
echo "Destek UI:   http://localhost:8092  (CaseSummaryAgent, AG-UI)"
echo "CaseSummary: curl -s http://localhost:8091/responses -H 'Content-Type: application/json' -d '{\"input\":\"Müşteri: C-1002, Sipariş: ORD-1002. Mesaj: Kalem kırık geldi.\"}'"
echo "SreAdvisor:  curl -s http://localhost:8090/responses -H 'Content-Type: application/json' -d '{\"input\":\"Sistemde sorun var mı?\"}'"
echo "Grafana:     kubectl --context $CONTEXT -n observability port-forward svc/kube-prometheus-stack-grafana 3000:80"
echo "SigNoz:      kubectl --context $CONTEXT -n observability port-forward svc/signoz 8080:8080"
