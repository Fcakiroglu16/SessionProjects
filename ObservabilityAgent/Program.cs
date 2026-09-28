using Azure.AI.AgentServer.Core;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using ObservabilityAgent;
using SessionProjects.Agents.Common;
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
    tools: ServiceSummaryTool.CreateFromEnvironment() is { } summaryTool
        ? [summaryTool.AsAIFunction(), .. mcpTools.Tools]
        : mcpTools.Tools);

// A2A: orkestratör (SreAdvisorAgent) bu agent'ı agent card üzerinden keşfedip araç olarak kullanır
var agentCard = A2AHostingExtensions.CreateAgentCard(agent, new A2A.AgentSkill
{
    Id = "observability-health",
    Name = "Uygulama sağlık analizi",
    Description = "SigNoz (trace/log), Prometheus (metric) ve Grafana verisiyle mikroservislerde hata oranı, gecikme ve hata log'larını analiz eder.",
    Tags = ["observability","signoz","prometheus","grafana"],
    Examples = ["microservice1-api'de hata var mı?","Son 15 dakikada 5xx artışı oldu mu?"]
});

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.Services.AddA2AAgent(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());
builder.RegisterProtocol("a2a", endpoints => endpoints.MapA2AAgent(agent, agentCard));

var app = builder.Build();
app.Run();
