using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace SessionProjects.Agents.Common;

public record McpServerConfig(string Name, string? Url, Dictionary<string, string>? Headers = null);

// Verilen MCP server'larına (HTTP) bağlanır ve araçlarını agent'a verilecek AITool listesine çevirir.
public sealed class McpToolProvider : IAsyncDisposable
{
    // OpenAI tek istekte en fazla 128 araç kabul ediyor
    private const int MaxToolCount = 128;

    // Agent sadece okuma yapar; MCP server'lar zaten read-only başlatılıyor, bu ikinci bir güvenlik katmanı
    private static readonly string[] WriteToolPrefixes =
        ["create_", "update_", "delete_", "add_", "remove_", "patch_", "put_", "post_", "set_", "upsert_"];

    private const int ConnectAttempts = 24;
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(5);

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
                var (client, tools) = await ConnectWithRetryAsync(server, cancellationToken);

                provider._clients.Add(client);
                provider.ConnectedServers.Add(server.Name);
                provider.Tools.AddRange(tools
                    .Where(tool => !IsWriteTool(tool.Name, server.Name) && IsSupportedSchema(tool))
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

    // Araç adı sunucu ön ekiyle gelebilir (ör. signoz_create_notification_channel); ön ek çıkarılıp kontrol edilir
    private static bool IsWriteTool(string toolName, string serverName)
    {
        var prefix = serverName.ToLowerInvariant() + "_";
        var name = toolName.StartsWith(prefix) ? toolName[prefix.Length..] : toolName;
        return WriteToolPrefixes.Any(name.StartsWith);
    }

    // OpenAI, parametre şemasının kökünde type=object ister ve oneOf/anyOf/allOf/enum/const/not kabul etmez.
    // Uyumsuz tek bir araç bütün isteği 400 ile düşürdüğü için bu araçlar yüklenmez.
    private static bool IsSupportedSchema(McpClientTool tool)
    {
        var schema = tool.JsonSchema;
        var supported = schema.ValueKind == System.Text.Json.JsonValueKind.Object
                        && schema.TryGetProperty("type", out var type) && type.ValueEquals("object")
                        && !new[] { "oneOf", "anyOf", "allOf", "enum", "const", "not" }
                            .Any(keyword => schema.TryGetProperty(keyword, out _));
        if (!supported)
            Console.WriteLine($"MCP aracı atlandı (OpenAI ile uyumsuz parametre şeması): {tool.Name}");
        return supported;
    }

    // Kubernetes'te pod'ların başlama sırası garanti değil: MCP server henüz ayakta değilse (bağlantı reddedildi,
    // DNS henüz çözülmüyor vb.) bir süre tekrar dener. Yetki (401) gibi HTTP hatalarında tekrar denemez.
    private static async Task<(McpClient Client, IList<McpClientTool> Tools)> ConnectWithRetryAsync(
        McpServerConfig server, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Name = server.Name,
                    Endpoint = new Uri(server.Url!),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = server.Headers
                });

                var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                return (client, tools);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null && attempt < ConnectAttempts)
            {
                Console.WriteLine($"MCP {server.Name} henüz hazır değil ({ex.Message}), {attempt}/{ConnectAttempts}. deneme");
                await Task.Delay(ConnectRetryDelay, cancellationToken);
            }
        }
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
