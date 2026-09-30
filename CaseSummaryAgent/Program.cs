using Azure.AI.AgentServer.Core;
using CaseSummaryAgent;
using CaseSummaryAgent.SkillDefinitions;
using CaseSummaryAgent.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Lütfen OPEN_AI_KEY ortam değişkenini ayarlayın.");

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o";

// Sipariş/müşteri servisi (microservice1) ve ürün/stok servisi (microservice2)
var orderServiceUrl = Environment.GetEnvironmentVariable("ORDER_SERVICE_URL") ?? "http://localhost:5296";
var productServiceUrl = Environment.GetEnvironmentVariable("PRODUCT_SERVICE_URL") ?? "http://localhost:5082";

// 1) TOOLS: mikroservislerden bilgi toplayan salt okunur fonksiyonlar
var supportTools = new SupportDataTools(
    new HttpClient { BaseAddress = new Uri(orderServiceUrl), Timeout = TimeSpan.FromSeconds(10) },
    new HttpClient { BaseAddress = new Uri(productServiceUrl), Timeout = TimeSpan.FromSeconds(10) });

// 2) SKILLS: dosya tabanlı (skills/return-exchange-policy/SKILL.md) + kodla tanımlanmış (escalation-rules).
// Provider "progressive disclosure" uygular: skill adları system prompt'ta listelenir, içerik load_skill ile,
// ek dokümanlar read_skill_resource ile, script'ler run_skill_script ile istendiğinde yüklenir.
var skillsProvider = new AgentSkillsProviderBuilder()
    // Dosya tabanlı skill'ler yalnızca talimat ve doküman içerir. Framework bir script runner şart koştuğu için
    // dosyadaki script'leri (.py/.sh ...) çalıştırmayı REDDEDEN bir runner verilir; container'da kod çalıştırılmaz.
    .UseFileSkill(Path.Combine(AppContext.BaseDirectory, "skills"),
        scriptRunner: (_, script, _, _, _) =>
            Task.FromResult<object?>($"Dosya tabanlı script çalıştırma kapalı: {script.Name}"))
    .UseSkill(EscalationRulesSkill.Create())
    .UseOptions(options =>
    {
        // Skill araçları varsayılan olarak insan onayı ister. Bu agent'ın skill'leri salt okunur bilgi ve
        // yan etkisiz script içerdiği için onay kapatılır.
        options.DisableLoadSkillApproval = true;
        options.DisableReadSkillResourceApproval = true;
        options.DisableRunSkillScriptApproval = true;
    })
    .Build();

var chatClient = new OpenAIClient(apiKey)
    .GetChatClient(model)
    .AsIChatClient();

var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = "case-summary-agent",
    Description = "Müşteri talebini sipariş, kargo, stok ve politika bilgisiyle özetleyip temsilciye uygun aksiyonu öneren karar destek agent'ı.",
    ChatOptions = new ChatOptions
    {
        Instructions = AgentInstructions.Text,
        Tools = [.. supportTools.AsAITools()]
    },
    AIContextProviders = [skillsProvider]
});

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

// AG-UI: aynı agent'ı UI'lara (Blazor, CopilotKit) açar. Metin ve tool çağrıları olay akışı (SSE) olarak gider,
// böylece UI agent'ın hangi servise ne sorduğunu canlı gösterebilir.
builder.Services.AddAGUIServer();
builder.RegisterProtocol("agui", endpoints => endpoints.MapAGUIServer("/ag-ui", agent));

var app = builder.Build();
app.Run();
