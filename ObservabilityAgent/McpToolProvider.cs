using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace ObservabilityAgent;

public record McpServerConfig(string Name, string? Url, Dictionary<string, string>? Headers = null);

// SigNoz / Grafana / Prometheus MCP server'larına bağlanır ve araçlarını agent'a verilecek AITool listesine çevirir.
public sealed class McpToolProvider : IAsyncDisposable
{
    // OpenAI tek istekte en fazla 128 araç kabul ediyor
    private const int MaxToolCount = 128;

    // Agent sadece okuma yapar; MCP server'lar zaten read-only başlatılıyor, bu ikinci bir güvenlik katmanı
    private static readonly string[] WriteToolPrefixes =
        ["create_", "update_", "delete_", "add_", "remove_", "patch_", "put_", "post_", "set_", "upsert_"];

    private readonly List<McpClient> _clients = [];

    public List<AITool> Tools { get; } = [];

    public List<string> ConnectedServers { get; } = [];

    public Dictionary<string, string> FailedServers { get; } = [];

    public static async Task<McpToolProvider> CreateAsync(IEnumerable<McpServerConfig> servers,
        CancellationToken cancellationToken = default)
    {
        var provider = new McpToolProvider();

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.Url))
            {
                provider.FailedServers[server.Name] = "URL tanımlı değil";
                continue;
            }

            try
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Name = server.Name,
                    Endpoint = new Uri(server.Url),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = server.Headers
                });

                var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

                provider._clients.Add(client);
                provider.ConnectedServers.Add(server.Name);
                provider.Tools.AddRange(tools
                    .Where(tool => !WriteToolPrefixes.Any(prefix => tool.Name.StartsWith(prefix)))
                    .Select(tool => WithServerPrefix(tool, server.Name)));
            }
            catch (Exception ex)
            {
                provider.FailedServers[server.Name] = ex.Message;
            }
        }

        if (provider.Tools.Count > MaxToolCount)
            provider.Tools.RemoveRange(MaxToolCount, provider.Tools.Count - MaxToolCount);

        return provider;
    }

    // Farklı MCP server'larda aynı isimli araçlar olabilir (ör. list_metrics); modelin hangi kaynağı
    // kullandığını bilmesi için araç adına server adı eklenir.
    private static McpClientTool WithServerPrefix(McpClientTool tool, string serverName)
    {
        var prefix = serverName.ToLowerInvariant() + "_";
        var name = tool.Name.StartsWith(prefix) ? tool.Name : prefix + tool.Name;
        return tool.WithName(name.Length > 64 ? name[..64] : name);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
    }
}
