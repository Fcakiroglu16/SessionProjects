using Azure.AI.AgentServer.Core;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using OpenAI;
using SessionProjects.Agents.Common;
using SreAdvisorAgent;

var apiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Lütfen OPEN_AI_KEY ortam değişkenini ayarlayın.");

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o";

// Uzman agent'ların adresleri Aspire service discovery ile gelir (AppHost'ta WithReference):
//   services__{kaynak-adı}__http__0 = http://localhost:port
var observabilityAgentUrl = Environment.GetEnvironmentVariable("services__observability-agent__http__0");
var kubernetesAgentUrl = Environment.GetEnvironmentVariable("services__kubernetes-agent__http__0");

// Uzman agent'lar MCP araçlarıyla çok adımlı analiz yaptığı için cevapları uzun sürebilir
var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

List<AITool> tools = [];
if (!string.IsNullOrWhiteSpace(observabilityAgentUrl))
    tools.Add(new RemoteAgentTool(
        "ask_observability_agent",
        "ObservabilityAgent'a (A2A) soru sorar. Uygulama katmanını inceler: SigNoz trace/log, Prometheus metric " +
        "(5xx oranı, p95 gecikme, target durumu) ve Grafana. Mikroservislerde hata, yavaşlık, exception sorularında kullan.",
        new Uri(observabilityAgentUrl), httpClient).AsAIFunction());

if (!string.IsNullOrWhiteSpace(kubernetesAgentUrl))
    tools.Add(new RemoteAgentTool(
        "ask_kubernetes_agent",
        "KubernetesAgent'a (A2A) soru sorar. Altyapı katmanını inceler: local Kubernetes cluster'ında node, pod, " +
        "restart, CrashLoopBackOff, Pending, event ve pod log'ları. Pod/node/cluster sorularında kullan.",
        new Uri(kubernetesAgentUrl), httpClient).AsAIFunction());

Console.WriteLine($"Uzman agent'lar: observability={observabilityAgentUrl ?? "-"}, kubernetes={kubernetesAgentUrl ?? "-"}");

var chatClient = new OpenAIClient(apiKey)
    .GetChatClient(model)
    .AsIChatClient();

var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = "sre-advisor-agent",
    Description = "Uzman agent'ları (Observability, Kubernetes) A2A ile koordine edip bulguları birleştiren SRE orkestratörü.",
    ChatOptions = new ChatOptions { Instructions = AgentInstructions.Text, Tools = tools },
    // Model iki uzmanı aynı turda çağırdığında araçlar paralel çalışsın: toplam süre en yavaş uzman kadar olur
    AllowConcurrentInvocation = true
});

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();
