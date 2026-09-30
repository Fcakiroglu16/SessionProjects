using AGUI.Client;
using CaseSummaryWeb.Components;
using CaseSummaryWeb.Services;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry (trace/log/metric) + HttpClient instrumentation: UI -> agent -> mikroservis tek trace'te görünür
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// CaseSummaryAgent'ın AG-UI endpoint'i. Kubernetes'te LoadBalancer: http://localhost:8091/ag-ui
var agentEndpoint = builder.Configuration["CaseSummaryAgent:Endpoint"] ?? "http://localhost:8091/ag-ui";

// Agent birden fazla tool ve skill çağırdığı için yanıt süresi uzun olabilir.
// ServiceDefaults'taki standart resilience (10 sn deneme timeout'u + retry) kaldırılır: uzun süren SSE akışını
// kesmemeli ve hata durumunda agent'ı ikinci kez çalıştırmamalı.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers deneysel API
builder.Services.AddHttpClient("case-summary-agent", client => client.Timeout = TimeSpan.FromMinutes(3))
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddScoped<IChatClient>(sp => new AGUIChatClient(new AGUIChatClientOptions(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("case-summary-agent"), agentEndpoint)));
builder.Services.AddScoped<CaseSummaryAgentClient>();
builder.Services.AddSingleton<SupportDesk>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Kubernetes'te TLS yok (LoadBalancer :8092, HTTP); yönlendirme sadece lokal geliştirmede
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(CaseSummaryWeb.Client._Imports).Assembly);

app.MapDefaultEndpoints();

app.Run();
