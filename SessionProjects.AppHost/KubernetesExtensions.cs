namespace SessionProjects.AppHost;

public static class KubernetesExtensions
{
    private const string KubernetesMcpVersion = "0.0.67";

    // kubernetes-mcp.toml içindeki "port" ile aynı olmalı
    private const int KubernetesMcpPort = 54615;

    // Docker Desktop'taki local Kubernetes için MCP server. Container yerine host'ta (npx) çalışır:
    // kubeconfig cluster'ı 127.0.0.1 üzerinden gösterdiği için container içinden erişilemez.
    public static IResourceBuilder<ExecutableResource> AddKubernetesMcp(this IDistributedApplicationBuilder builder)
    {
        return builder.AddExecutable("kubernetes-mcp", "npx", "./kubernetes",
                "-y", $"kubernetes-mcp-server@{KubernetesMcpVersion}", "--config", "kubernetes-mcp.toml")
            .WithHttpEndpoint(port: KubernetesMcpPort, name: "http", isProxied: false)
            .WithUrlForEndpoint("http", url => { url.DisplayText = "Kubernetes MCP"; url.Url = "/mcp"; })
            .WithHttpHealthCheck("/healthz");
    }
}
