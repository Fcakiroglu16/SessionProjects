using System.ComponentModel;
using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.AI;

namespace KubernetesAgent;

// Agent'ın kendi araçları: salt okunur kaynak kullanımı raporu ve cluster'ı değiştiren TEK aksiyon (restart).
// Kubernetes MCP salt okunur kalır; yazma işlemleri
// burada tek tek tanımlanır ve korumalı namespace'lerde çalışmaz.
public sealed class KubernetesActions(IKubernetes client)
{
    // Agent'ın hiçbir koşulda dokunmadığı sistem namespace'leri
    private static readonly HashSet<string> ProtectedNamespaces =
        ["kube-system", "kube-public", "kube-node-lease", "local-path-storage"];

    public static KubernetesActions Create()
    {
        // Cluster içinde pod'un ServiceAccount'u (yetkiler RBAC ile sınırlı), dışarıda ~/.kube/config kullanılır
        var config = KubernetesClientConfiguration.IsInCluster()
            ? KubernetesClientConfiguration.InClusterConfig()
            : KubernetesClientConfiguration.BuildConfigFromConfigFile(
                currentContext: Environment.GetEnvironmentVariable("KUBERNETES_CONTEXT") ?? "docker-desktop");
        return new KubernetesActions(new Kubernetes(config));
    }

    public IEnumerable<AITool> AsTools()
    {
        yield return AIFunctionFactory.Create(GetPodResourceUsageAsync,
            new AIFunctionFactoryOptions { Name = "kubernetes_pod_resource_usage" });
        yield return AIFunctionFactory.Create(RestartDeploymentAsync,
            new AIFunctionFactoryOptions { Name = "kubernetes_restart_deployment" });
    }

    [Description("SALT OKUNUR. Bir namespace'teki pod'ların anlık CPU/RAM kullanımını (metrics-server), tanımlı " +
                 "requests/limits değerlerini ve kullanımın request'e ve limit'e oranını (%) hesaplanmış olarak döner. " +
                 "CPU/RAM sorularında yüzdeleri kendin hesaplamak yerine bu aracı kullan.")]
    private async Task<string> GetPodResourceUsageAsync(
        [Description("Pod'ların namespace'i")] string @namespace,
        CancellationToken cancellationToken)
    {
        PodMetricsList metrics;
        try
        {
            metrics = await client.GetKubernetesPodsMetricsByNamespaceAsync(@namespace);
        }
        catch (Exception ex)
        {
            return $"HATA: metrics API okunamadı (metrics-server kurulu mu?): {ex.Message}";
        }

        var pods = await client.CoreV1.ListNamespacedPodAsync(@namespace, cancellationToken: cancellationToken);
        var lines = new List<string>
        {
            "pod | container | CPU kullanım | CPU request | CPU limit | CPU %request | CPU %limit | " +
            "RAM kullanım | RAM request | RAM limit | RAM %request | RAM %limit"
        };

        foreach (var pod in pods.Items.Where(p => p.Status?.Phase == "Running"))
        {
            var podMetrics = metrics.Items.FirstOrDefault(m => m.Metadata.Name == pod.Metadata.Name);
            foreach (var container in pod.Spec.Containers)
            {
                var usage = podMetrics?.Containers.FirstOrDefault(c => c.Name == container.Name)?.Usage;
                var cpu = Quantity(usage, "cpu");
                var memory = Quantity(usage, "memory");
                var cpuRequest = Quantity(container.Resources?.Requests, "cpu");
                var cpuLimit = Quantity(container.Resources?.Limits, "cpu");
                var memoryRequest = Quantity(container.Resources?.Requests, "memory");
                var memoryLimit = Quantity(container.Resources?.Limits, "memory");

                lines.Add(string.Join(" | ",
                    pod.Metadata.Name, container.Name,
                    Millicores(cpu), Millicores(cpuRequest), Millicores(cpuLimit),
                    Percent(cpu, cpuRequest), Percent(cpu, cpuLimit),
                    Mebibytes(memory), Mebibytes(memoryRequest), Mebibytes(memoryLimit),
                    Percent(memory, memoryRequest), Percent(memory, memoryLimit)));
            }
        }

        return lines.Count == 1 ? $"'{@namespace}' namespace'inde Running pod yok." : string.Join('\n', lines);
    }

    private static decimal? Quantity(IDictionary<string, ResourceQuantity>? values, string name) =>
        values is not null && values.TryGetValue(name, out var quantity) ? quantity.ToDecimal() : null;

    private static string Millicores(decimal? cores) =>
        cores is null ? "-" : $"{cores.Value * 1000:0.#}m";

    private static string Mebibytes(decimal? bytes) =>
        bytes is null ? "-" : $"{bytes.Value / 1024 / 1024:0.#}Mi";

    private static string Percent(decimal? used, decimal? total) =>
        used is null || total is null or 0 ? "-" : $"%{used.Value / total.Value * 100:0.#}";

    [Description("Bir Deployment'ın pod'larını sırayla yeniden başlatır (kubectl rollout restart ile aynı). " +
                 "Cluster'ı DEĞİŞTİRİR. Sistem namespace'lerinde çalışmaz.")]
    private async Task<string> RestartDeploymentAsync(
        [Description("Deployment'ın namespace'i")] string @namespace,
        [Description("Deployment adı")] string name,
        [Description("Neden yeniden başlatıldığı (bulgulara dayanan kısa gerekçe)")] string reason,
        CancellationToken cancellationToken)
    {
        if (ProtectedNamespaces.Contains(@namespace))
            return $"REDDEDİLDİ: '{@namespace}' korumalı bir sistem namespace'i; bu agent burada değişiklik yapamaz.";

        try
        {
            await client.AppsV1.ReadNamespacedDeploymentAsync(name, @namespace, cancellationToken: cancellationToken);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return $"BULUNAMADI: {@namespace}/{name} adında bir Deployment yok.";
        }

        // kubectl rollout restart'ın yaptığı gibi pod template'ine restartedAt annotation'ı eklenir
        var restartedAt = DateTime.UtcNow.ToString("o");
        var patch = new
        {
            spec = new
            {
                template = new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string>
                        {
                            ["kubectl.kubernetes.io/restartedAt"] = restartedAt,
                            ["sessionprojects.io/restart-reason"] = reason
                        }
                    }
                }
            }
        };

        await client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(JsonSerializer.Serialize(patch), V1Patch.PatchType.StrategicMergePatch),
            name, @namespace, cancellationToken: cancellationToken);

        return $"TAMAM: {@namespace}/{name} yeniden başlatıldı (restartedAt={restartedAt}). " +
               "Yeni pod'ların Running/Ready olduğunu pods_list ile doğrula.";
    }
}
