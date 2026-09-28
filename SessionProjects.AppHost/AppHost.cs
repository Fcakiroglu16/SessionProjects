using SessionProjects.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var observability = builder.AddObservability();
var otelCollector = observability.OtelCollector;

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var microservice2 = builder.AddProject<Projects.Microservice2_API>("microservice2-api")
    .WithReference(rabbitmq)
    .WithOtelCollector(otelCollector)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.Microservice1_API>("microservice1-api")
    .WithReference(microservice2)
    .WithReference(rabbitmq)
    .WithOtelCollector(otelCollector)
    .WaitFor(rabbitmq)
    .WaitFor(microservice2);

// Değer user secrets'tan okunur (Parameters:openai-api-key); tanımlı değilse Aspire dashboard başlangıçta sorar.
var openAiApiKey = builder.AddParameter("openai-api-key", secret: true);

var observabilityAgent = builder.AddProject<Projects.ObservabilityAgent>("observability-agent")
    .WithHttpEndpoint(port: 8088, env: "PORT")
    .WithA2APublicUrl()
    .WithEnvironment("OPEN_AI_KEY", openAiApiKey)
    .WithMcpServer("SIGNOZ_MCP_URL", observability.SigNozMcp)
    .WithMcpServer("GRAFANA_MCP_URL", observability.GrafanaMcp)
    .WithMcpServer("PROMETHEUS_MCP_URL", observability.PrometheusMcp)
    .WithOtelCollector(otelCollector);

// SigNoz API key opsiyonel (Parameters:signoz-api-key, SigNoz UI > Settings > API Keys).
// Yoksa agent yine başlar; SigNoz MCP'yi "ulaşılamadı" olarak raporlar, Prometheus ve Grafana ile çalışır.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:signoz-api-key"]))
    observabilityAgent.WithEnvironment("SIGNOZ_API_KEY", builder.AddParameter("signoz-api-key", secret: true));

var kubernetesMcp = builder.AddKubernetesMcp();

var kubernetesAgent = builder.AddProject<Projects.KubernetesAgent>("kubernetes-agent")
    .WithHttpEndpoint(port: 8089, env: "PORT")
    .WithA2APublicUrl()
    .WithEnvironment("OPEN_AI_KEY", openAiApiKey)
    .WithMcpServer("KUBERNETES_MCP_URL", kubernetesMcp)
    .WithOtelCollector(otelCollector);

// Orkestratör: uzman agent'ları A2A ile araç olarak kullanır. Adreslerini service discovery'den alır.
// WaitFor yok: bir uzman ayakta değilse (ör. signoz-api-key girilmedi) orkestratör yine açılır,
// o uzmanı çağırdığında "ulaşılamadı" bilgisini cevabına yansıtır.
builder.AddProject<Projects.SreAdvisorAgent>("sre-advisor-agent")
    .WithHttpEndpoint(port: 8090, env: "PORT")
    .WithEnvironment("OPEN_AI_KEY", openAiApiKey)
    .WithReference(observabilityAgent)
    .WithReference(kubernetesAgent)
    .WithOtelCollector(otelCollector);

builder.Build().Run();
