using Azure.AI.AgentServer.Core;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using ObservabilityAgent;
using OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Lütfen OPEN_AI_KEY ortam değişkenini ayarlayın.");

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o";

// MCP adresleri Aspire AppHost tarafından ortam değişkeni olarak verilir
var signozApiKey = Environment.GetEnvironmentVariable("SIGNOZ_API_KEY");
McpServerConfig[] mcpServers =
[
    new("SigNoz", Environment.GetEnvironmentVariable("SIGNOZ_MCP_URL"),
        string.IsNullOrWhiteSpace(signozApiKey) ? null : new() { ["SIGNOZ-API-KEY"] = signozApiKey }),
    new("Grafana", Environment.GetEnvironmentVariable("GRAFANA_MCP_URL")),
    new("Prometheus", Environment.GetEnvironmentVariable("PROMETHEUS_MCP_URL"))
];

await using var mcpTools = await McpToolProvider.CreateAsync(mcpServers);

Console.WriteLine($"MCP bağlantıları: {string.Join(", ", mcpTools.ConnectedServers)} ({mcpTools.Tools.Count} araç)");
foreach (var (server, error) in mcpTools.FailedServers)
    Console.WriteLine($"MCP bağlanamadı: {server} -> {error}");

var chatClient = new OpenAIClient(apiKey)
    .GetChatClient(model)
    .AsIChatClient();

var agent = chatClient.AsAIAgent(
    instructions: AgentInstructions.Build(mcpTools.ConnectedServers, mcpTools.FailedServers),
    name: "observability-agent",
    description: "SigNoz, Grafana ve Prometheus MCP'lerini kullanarak sistemde sorun olup olmadığını analiz eder.",
    tools: mcpTools.Tools);

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();
