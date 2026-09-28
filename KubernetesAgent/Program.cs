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

var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = "kubernetes-agent",
    Description = "Docker Desktop üzerindeki local Kubernetes cluster'ını Kubernetes MCP ile inceleyip sorun olup olmadığını analiz eder; onaylı Deployment restart yapabilir.",
    ChatOptions = new ChatOptions
    {
        Instructions = AgentInstructions.Build(mcpTools.ConnectedServers, mcpTools.FailedServers),
        Tools = [.. mcpTools.Tools, .. KubernetesActions.CreateFromKubeConfig().AsApprovalRequiredTools()]
    },
    // Sohbet geçmişi agent oturumunda tutulur. Foundry hosting preview'ında, geçmiş platformdan yeniden
    // kurulduğunda onay isteği (mcp_approval_request) farklı bir id ile yükleniyor ve onay cevabı eşleşmiyor;
    // kendi geçmiş sağlayıcımız bu yolu devre dışı bırakır ve onay akışı çalışır.
    ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions())
});

// A2A: orkestratör (SreAdvisorAgent) bu agent'ı agent card üzerinden keşfedip araç olarak kullanır
var agentCard = A2AHostingExtensions.CreateAgentCard(agent, new A2A.AgentSkill
{
    Id = "kubernetes-health",
    Name = "Kubernetes cluster sağlık analizi",
    Description = "Local Kubernetes cluster'ında node, pod, event ve log'ları salt okunur inceleyerek CrashLoop, restart, Pending gibi sorunları bulur.",
    Tags = ["kubernetes","pods","events"],
    Examples = ["Clusterda sorunlu pod var mı?","Hangi pod'lar sık restart ediyor?"]
});

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.Services.AddA2AAgent(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());
builder.RegisterProtocol("a2a", endpoints => endpoints.MapA2AAgent(agent, agentCard));

var app = builder.Build();
app.Run();
