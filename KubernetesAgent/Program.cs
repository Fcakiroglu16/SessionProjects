using Azure.AI.AgentServer.Core;
using KubernetesAgent;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using OpenAI;
using SessionProjects.Agents.Common;

var apiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Lütfen OPEN_AI_KEY ortam değişkenini ayarlayın.");

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o";

// MCP adresi Aspire AppHost tarafından ortam değişkeni olarak verilir
McpServerConfig[] mcpServers =
[
    new("Kubernetes", Environment.GetEnvironmentVariable("KUBERNETES_MCP_URL"))
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
    name: "kubernetes-agent",
    description: "Docker Desktop üzerindeki local Kubernetes cluster'ını Kubernetes MCP ile inceleyip sorun olup olmadığını analiz eder.",
    tools: mcpTools.Tools);

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddLocalIsolationKeyFallback();
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();
