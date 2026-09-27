using Aspire.Hosting.ApplicationModel;

namespace SessionProjects.AppHost;

// Telemetry akışı:
//   Servisler --OTLP--> otel-collector --traces/logs--> signoz-otel-collector --> ClickHouse --> SigNoz UI
//                                      --metrics------> :8889 <--scrape-- Prometheus <-- Grafana
public record ObservabilityResources(
    IResourceBuilder<ContainerResource> OtelCollector,
    IResourceBuilder<ContainerResource> SigNozMcp,
    IResourceBuilder<ContainerResource> GrafanaMcp,
    IResourceBuilder<ContainerResource> PrometheusMcp);

public static class ObservabilityExtensions
{
    private const string ObservabilityPath = "./observability";

    private const string SigNozVersion = "v0.129.0";
    private const string SigNozOtelCollectorVersion = "v0.144.5";
    private const string ClickHouseVersion = "25.5.6";
    private const string ZooKeeperVersion = "3.7.1";
    private const string OtelCollectorVersion = "0.161.0";
    private const string PrometheusVersion = "v3.15.0";
    private const string GrafanaVersion = "13.0.9";
    private const string SigNozMcpVersion = "v0.15.0";
    private const string GrafanaMcpVersion = "1.6.0";
    private const string PrometheusMcpVersion = "1.6.2";

    // MCP server host portları sabit: MCP istemci ayarları (~/.claude.json, .mcp.json) bu adresleri kullanır.
    private const int SigNozMcpPort = 54612;
    private const int GrafanaMcpPort = 54613;
    private const int PrometheusMcpPort = 54614;

    public static ObservabilityResources AddObservability(this IDistributedApplicationBuilder builder)
    {
        var (signozOtelCollector, signozMcp) = builder.AddSigNoz();

        var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib",
                OtelCollectorVersion)
            .WithBindMount($"{ObservabilityPath}/otel-collector/config.yaml", "/etc/otelcol-contrib/config.yaml",
                isReadOnly: true)
            .WithEndpoint(targetPort: 4317, name: "grpc", scheme: "http")
            .WithEndpoint(targetPort: 4318, name: "http", scheme: "http")
            .WithUrlForEndpoint("grpc", url => url.DisplayText = "OTLP gRPC")
            .WithUrlForEndpoint("http", url => url.DisplayText = "OTLP HTTP")
            .WithHttpEndpoint(targetPort: 8889, name: "prometheus")
            .WithUrlForEndpoint("prometheus", url => { url.DisplayText = "Prometheus metrics"; url.Url = "/metrics"; })
            .WithHttpEndpoint(targetPort: 13133, name: "health")
            .WithUrlForEndpoint("health", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
            .WithHttpHealthCheck("/", endpointName: "health")
            .WaitFor(signozOtelCollector);

        var prometheus = builder.AddContainer("prometheus", "prom/prometheus", PrometheusVersion)
            .WithBindMount($"{ObservabilityPath}/prometheus/prometheus.yml", "/etc/prometheus/prometheus.yml",
                isReadOnly: true)
            .WithVolume("sessionprojects-prometheus-data", "/prometheus")
            .WithArgs("--config.file=/etc/prometheus/prometheus.yml", "--storage.tsdb.path=/prometheus")
            .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http")
            .WithUrlForEndpoint("http", url => url.DisplayText = "Prometheus UI")
            .WithHttpHealthCheck("/-/healthy")
            .WaitFor(otelCollector);

        var grafana = builder.AddContainer("grafana", "grafana/grafana", GrafanaVersion)
            .WithBindMount($"{ObservabilityPath}/grafana/provisioning", "/etc/grafana/provisioning", isReadOnly: true)
            .WithBindMount($"{ObservabilityPath}/grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
            .WithVolume("sessionprojects-grafana-data", "/var/lib/grafana")
            // Sadece local geliştirme için: login olmadan admin erişimi
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Admin")
            .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "true")
            .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http")
            .WithUrlForEndpoint("http", url => url.DisplayText = "Grafana")
            .WithHttpHealthCheck("/api/health")
            .WaitFor(prometheus);

        // Grafana anonim admin erişimine açık olduğu için MCP server'a token verilmiyor.
        // Agent sadece okuma yapar: yazma araçları ve bu kurulumda olmayan kategoriler kapalı
        var grafanaMcp = builder.AddContainer("grafana-mcp", "grafana/mcp-grafana", GrafanaMcpVersion)
            .WithEnvironment("GRAFANA_URL", "http://grafana:3000")
            .WithArgs("-t", "streamable-http", "--address", "0.0.0.0:8000",
                "--allowed-hosts", $"localhost:{GrafanaMcpPort},127.0.0.1:{GrafanaMcpPort}",
                "--disable-write", "--disable-incident", "--disable-oncall", "--disable-sift", "--disable-admin",
                "--disable-pyroscope", "--disable-loki", "--disable-elasticsearch", "--disable-asserts")
            .WithHttpEndpoint(port: GrafanaMcpPort, targetPort: 8000, name: "http")
            .WithUrlForEndpoint("http", url => { url.DisplayText = "Grafana MCP"; url.Url = "/mcp"; })
            .WaitFor(grafana);

        var prometheusMcp = builder.AddContainer("prometheus-mcp", "ghcr.io/pab1it0/prometheus-mcp-server", PrometheusMcpVersion)
            .WithEnvironment("PROMETHEUS_URL", "http://prometheus:9090")
            .WithEnvironment("PROMETHEUS_MCP_SERVER_TRANSPORT", "http")
            .WithEnvironment("PROMETHEUS_MCP_BIND_HOST", "0.0.0.0")
            .WithEnvironment("PROMETHEUS_MCP_BIND_PORT", "8080")
            .WithHttpEndpoint(port: PrometheusMcpPort, targetPort: 8080, name: "http")
            .WithUrlForEndpoint("http", url => { url.DisplayText = "Prometheus MCP"; url.Url = "/mcp"; })
            .WaitFor(prometheus);

        return new ObservabilityResources(otelCollector, signozMcp, grafanaMcp, prometheusMcp);
    }

    // Aspire'ın varsayılan OTLP ayarını (dashboard) ezer; servisler telemetry'yi otel-collector'a gönderir.
    public static IResourceBuilder<ProjectResource> WithOtelCollector(this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<ContainerResource> otelCollector)
    {
        return project
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("grpc"))
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WaitFor(otelCollector);
    }

    // MCP server'ın streamable-http adresini (".../mcp") verilen ortam değişkeniyle projeye geçirir.
    public static IResourceBuilder<ProjectResource> WithMcpServer<T>(this IResourceBuilder<ProjectResource> project,
        string environmentVariable, IResourceBuilder<T> mcpServer) where T : class, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        var endpoint = mcpServer.GetEndpoint("http");
        return project
            .WithEnvironment(environmentVariable, ReferenceExpression.Create($"{endpoint}/mcp"))
            .WaitFor(mcpServer);
    }

    // SigNoz: ZooKeeper + ClickHouse + şema migrator + SigNoz (UI/API) + SigNoz OTel Collector
    private static (IResourceBuilder<ContainerResource> OtelCollector, IResourceBuilder<ContainerResource> Mcp) AddSigNoz(
        this IDistributedApplicationBuilder builder)
    {
        const string clickHouseDsn = "tcp://signoz-clickhouse:9000";
        const string userScriptsVolume = "sessionprojects-signoz-clickhouse-user-scripts";
        const string userScriptsPath = "/var/lib/clickhouse/user_scripts";

        var jwtSecret = builder.AddParameter("signoz-jwt-secret",
            new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

        var zookeeper = builder.AddContainer("signoz-zookeeper", "signoz/zookeeper", ZooKeeperVersion)
            .WithContainerRuntimeArgs("--user", "root")
            .WithVolume("sessionprojects-signoz-zookeeper-data", "/bitnami/zookeeper")
            .WithEnvironment("ZOO_SERVER_ID", "1")
            .WithEnvironment("ALLOW_ANONYMOUS_LOGIN", "yes")
            .WithEnvironment("ZOO_AUTOPURGE_INTERVAL", "1")
            .WithHttpEndpoint(targetPort: 8080, name: "admin")
            .WithUrlForEndpoint("admin", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
            .WithHttpHealthCheck("/commands/ruok", endpointName: "admin");

        // ClickHouse'un histogramQuantile fonksiyonu için gerekli binary'i indirir
        var initClickHouse = builder.AddContainer("signoz-init-clickhouse", "clickhouse/clickhouse-server",
                ClickHouseVersion)
            .WithVolume(userScriptsVolume, userScriptsPath)
            .WithEntrypoint("bash")
            .WithArgs("-c", $$"""
                set -e
                version="v0.0.1"
                node_os=$(uname -s | tr '[:upper:]' '[:lower:]')
                node_arch=$(uname -m | sed s/aarch64/arm64/ | sed s/x86_64/amd64/)
                if [ -x {{userScriptsPath}}/histogramQuantile ]; then echo "histogramQuantile already exists"; exit 0; fi
                echo "Fetching histogram-binary for ${node_os}/${node_arch}"
                cd /tmp
                wget -O histogram-quantile.tar.gz "https://github.com/SigNoz/signoz/releases/download/histogram-quantile%2F${version}/histogram-quantile_${node_os}_${node_arch}.tar.gz"
                tar -xvzf histogram-quantile.tar.gz
                mv histogram-quantile {{userScriptsPath}}/histogramQuantile
                """);

        var clickHouse = builder.AddContainer("signoz-clickhouse", "clickhouse/clickhouse-server", ClickHouseVersion)
            .WithBindMount($"{ObservabilityPath}/signoz/clickhouse/config.xml", "/etc/clickhouse-server/config.xml",
                isReadOnly: true)
            .WithBindMount($"{ObservabilityPath}/signoz/clickhouse/users.xml", "/etc/clickhouse-server/users.xml",
                isReadOnly: true)
            .WithBindMount($"{ObservabilityPath}/signoz/clickhouse/custom-function.xml",
                "/etc/clickhouse-server/custom-function.xml", isReadOnly: true)
            .WithBindMount($"{ObservabilityPath}/signoz/clickhouse/cluster.xml",
                "/etc/clickhouse-server/config.d/cluster.xml", isReadOnly: true)
            .WithVolume(userScriptsVolume, userScriptsPath)
            .WithVolume("sessionprojects-signoz-clickhouse-data", "/var/lib/clickhouse")
            .WithEnvironment("CLICKHOUSE_SKIP_USER_SETUP", "1")
            .WithContainerRuntimeArgs("--ulimit", "nproc=65535", "--ulimit", "nofile=262144:262144")
            .WithHttpEndpoint(targetPort: 8123, name: "http")
            .WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
            .WithHttpHealthCheck("/ping", endpointName: "http")
            .WaitForCompletion(initClickHouse)
            .WaitFor(zookeeper);

        var migrator = builder.AddContainer("signoz-telemetrystore-migrator", "signoz/signoz-otel-collector",
                SigNozOtelCollectorVersion)
            .WithSigNozClickHouseEnvironment(clickHouseDsn)
            .WithEntrypoint("/bin/sh")
            .WithArgs("-c", """
                /signoz-otel-collector migrate bootstrap &&
                /signoz-otel-collector migrate sync up &&
                /signoz-otel-collector migrate async up
                """)
            .WaitFor(clickHouse);

        var signoz = builder.AddContainer("signoz", "signoz/signoz", SigNozVersion)
            .WithVolume("sessionprojects-signoz-sqlite", "/var/lib/signoz")
            .WithEnvironment("SIGNOZ_ALERTMANAGER_PROVIDER", "signoz")
            .WithEnvironment("SIGNOZ_TELEMETRYSTORE_CLICKHOUSE_DSN", clickHouseDsn)
            .WithEnvironment("SIGNOZ_SQLSTORE_SQLITE_PATH", "/var/lib/signoz/signoz.db")
            .WithEnvironment("SIGNOZ_TOKENIZER_JWT_SECRET", jwtSecret)
            .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
            .WithUrlForEndpoint("http", url => url.DisplayText = "SigNoz UI")
            .WithHttpHealthCheck("/api/v1/health")
            .WaitForCompletion(migrator);

        // API key server'da tutulmuyor; MCP istemcisi her istekte "SIGNOZ-API-KEY" header'ı ile gönderir
        // (SigNoz UI > Settings > API Keys).
        var signozMcp = builder.AddContainer("signoz-mcp", "signoz/signoz-mcp-server", SigNozMcpVersion)
            .WithEnvironment("SIGNOZ_URL", "http://signoz:8080")
            .WithEnvironment("TRANSPORT_MODE", "http")
            .WithEnvironment("MCP_SERVER_PORT", "8000")
            // Başlangıçta SigNoz dokümantasyon index'ini yenilemesin, image ile gelen snapshot yeterli
            .WithEnvironment("SIGNOZ_DOCS_REFRESH_INTERVAL", "0")
            .WithEnvironment("SIGNOZ_DOCS_FULL_REFRESH_INTERVAL", "0")
            .WithHttpEndpoint(port: SigNozMcpPort, targetPort: 8000, name: "http")
            .WithUrlForEndpoint("http", url => { url.DisplayText = "SigNoz MCP"; url.Url = "/mcp"; })
            .WithHttpHealthCheck("/livez")
            .WaitFor(signoz);

        // OpAMP (--manager-config) kullanılmıyor: OpAMP modunda collector pipeline config'ini SigNoz'dan bekler ve
        // UI'da ilk kullanıcı oluşturulana kadar OTLP alıcısını açmaz. Statik config ile ingestion hemen başlar.
        var signozOtelCollector = builder.AddContainer("signoz-otel-collector", "signoz/signoz-otel-collector",
                SigNozOtelCollectorVersion)
            .WithBindMount($"{ObservabilityPath}/signoz/otel-collector-config.yaml",
                "/etc/otel-collector-config.yaml", isReadOnly: true)
            .WithSigNozClickHouseEnvironment(clickHouseDsn)
            .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "host.name=signoz-host,os.type=linux")
            .WithEnvironment("LOW_CARDINAL_EXCEPTION_GROUPING", "false")
            .WithEntrypoint("/bin/sh")
            .WithArgs("-c", """
                /signoz-otel-collector migrate sync check &&
                /signoz-otel-collector --config=/etc/otel-collector-config.yaml
                """)
            .WithEndpoint(targetPort: 4317, name: "grpc", scheme: "http")
            .WithHttpEndpoint(targetPort: 13133, name: "health")
            .WithUrlForEndpoint("grpc", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
            .WithUrlForEndpoint("health", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
            .WithHttpHealthCheck("/", endpointName: "health")
            .WaitForCompletion(migrator)
            .WaitFor(signoz);

        return (signozOtelCollector, signozMcp);
    }

    private static IResourceBuilder<ContainerResource> WithSigNozClickHouseEnvironment(
        this IResourceBuilder<ContainerResource> container, string clickHouseDsn)
    {
        return container
            .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_CLICKHOUSE_DSN", clickHouseDsn)
            .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_CLICKHOUSE_CLUSTER", "cluster")
            .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_CLICKHOUSE_REPLICATION", "true")
            .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_TIMEOUT", "10m");
    }
}
