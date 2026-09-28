using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ObservabilityAgent;

// Mikroservislerin temel sağlık metriklerini SABİT, test edilmiş PromQL sorgularıyla toplar.
// Model her seferinde PromQL'i sıfırdan yazdığında etiket/metric adlarını karıştırabiliyor; bu araç
// en sık sorulan soruları tek çağrıda ve deterministik olarak cevaplar. Serbest PromQL için prometheus_* araçları durur.
public sealed class ServiceSummaryTool(HttpClient httpClient, Uri prometheusUrl)
{
    private const string AppNamespace = "sessionprojects";

    public static ServiceSummaryTool? CreateFromEnvironment()
    {
        var url = Environment.GetEnvironmentVariable("PROMETHEUS_URL");
        return string.IsNullOrWhiteSpace(url) ? null : new ServiceSummaryTool(new HttpClient(), new Uri(url));
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(GetSummaryAsync,
        new AIFunctionFactoryOptions { Name = "observability_service_summary" });

    [Description("SALT OKUNUR. Mikroservisler (microservice1-api, microservice2-api) için istek hızı, 5xx oranı, " +
                 "p95 gecikme, bellek ve CPU kullanımını (limit oranıyla) Prometheus'tan tek çağrıda getirir. " +
                 "Sağlık / hata oranı / gecikme / CPU / RAM sorularında önce bu aracı kullan.")]
    private async Task<string> GetSummaryAsync(
        [Description("Zaman penceresi, ör. 5m, 15m, 1h")] string window = "15m",
        CancellationToken cancellationToken = default)
    {
        var requests = await QueryAsync(
            $"sum by (job)(rate(http_server_request_duration_seconds_count{{job=~\"microservice.*\"}}[{window}]))",
            "job", cancellationToken);
        var errors = await QueryAsync(
            $"sum by (job)(rate(http_server_request_duration_seconds_count{{job=~\"microservice.*\",http_response_status_code=~\"5..\"}}[{window}]))",
            "job", cancellationToken);
        var p95 = await QueryAsync(
            $"histogram_quantile(0.95, sum by (le, job)(rate(http_server_request_duration_seconds_bucket{{job=~\"microservice.*\"}}[{window}])))",
            "job", cancellationToken);
        var memory = await QueryAsync(
            $"sum by (container)(container_memory_working_set_bytes{{namespace=\"{AppNamespace}\",container=~\"microservice.*\"}})",
            "container", cancellationToken);
        var cpu = await QueryAsync(
            $"sum by (container)(rate(container_cpu_usage_seconds_total{{namespace=\"{AppNamespace}\",container=~\"microservice.*\"}}[5m]))",
            "container", cancellationToken);
        var memoryLimit = await QueryAsync(
            $"sum by (container)(kube_pod_container_resource_limits{{namespace=\"{AppNamespace}\",container=~\"microservice.*\",resource=\"memory\"}})",
            "container", cancellationToken);
        var cpuLimit = await QueryAsync(
            $"sum by (container)(kube_pod_container_resource_limits{{namespace=\"{AppNamespace}\",container=~\"microservice.*\",resource=\"cpu\"}})",
            "container", cancellationToken);

        var services = requests.Keys.Union(memory.Keys).OrderBy(s => s).ToList();
        if (services.Count == 0)
            return $"Son {window} içinde mikroservislere ait metric bulunamadı (trafik yok ya da telemetry gelmiyor).";

        var lines = new List<string>
        {
            $"Pencere: son {window} (CPU için 5m). Kaynak: Prometheus.",
            "servis | istek/s | 5xx/s | 5xx oranı | p95 gecikme | RAM (limit %) | CPU (limit %)"
        };
        foreach (var service in services)
        {
            var requestRate = requests.GetValueOrDefault(service);
            // 5xx serisi hiç yoksa bu "ölçülemedi" değil, hiç 5xx olmadığı anlamına gelir
            var errorRate = errors.GetValueOrDefault(service) ?? 0;
            var errorRatio = requestRate is > 0 ? $"%{errorRate / requestRate.Value * 100:0.##}" : "-";
            var latency = p95.GetValueOrDefault(service) is { } seconds && !double.IsNaN(seconds)
                ? $"{seconds * 1000:0.#} ms"
                : "trafik yok";

            lines.Add(string.Join(" | ",
                service,
                requestRate is null or double.NaN ? "trafik yok" : $"{requestRate:0.##}",
                $"{errorRate:0.##}",
                errorRatio,
                latency,
                WithLimit(memory.GetValueOrDefault(service), memoryLimit.GetValueOrDefault(service),
                    bytes => $"{bytes / 1024 / 1024:0.#}Mi"),
                WithLimit(cpu.GetValueOrDefault(service), cpuLimit.GetValueOrDefault(service),
                    cores => $"{cores * 1000:0.#}m")));
        }

        return string.Join('\n', lines);
    }

    private static string WithLimit(double? used, double? limit, Func<double, string> format) =>
        used is null ? "-" : limit is > 0 ? $"{format(used.Value)} (%{used.Value / limit.Value * 100:0.#})" : format(used.Value);

    private async Task<Dictionary<string, double?>> QueryAsync(string promQl, string label,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(prometheusUrl, $"/api/v1/query?query={Uri.EscapeDataString(promQl)}");
        using var document = JsonDocument.Parse(await httpClient.GetStringAsync(uri, cancellationToken));

        var result = new Dictionary<string, double?>();
        foreach (var series in document.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
        {
            if (!series.GetProperty("metric").TryGetProperty(label, out var name)) continue;
            var raw = series.GetProperty("value")[1].GetString();
            result[name.GetString()!] = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : null;
        }

        return result;
    }
}
