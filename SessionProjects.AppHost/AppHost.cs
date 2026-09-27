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

// Değerler user secrets'tan okunur (Parameters:openai-api-key, Parameters:signoz-api-key);
// tanımlı değilse Aspire dashboard başlangıçta sorar.
var openAiApiKey = builder.AddParameter("openai-api-key", secret: true);
var signozApiKey = builder.AddParameter("signoz-api-key", secret: true);

builder.AddProject<Projects.ObservabilityAgent>("observability-agent")
    .WithHttpEndpoint(port: 8088, env: "PORT")
    .WithEnvironment("OPEN_AI_KEY", openAiApiKey)
    .WithEnvironment("SIGNOZ_API_KEY", signozApiKey)
    .WithMcpServer("SIGNOZ_MCP_URL", observability.SigNozMcp)
    .WithMcpServer("GRAFANA_MCP_URL", observability.GrafanaMcp)
    .WithMcpServer("PROMETHEUS_MCP_URL", observability.PrometheusMcp)
    .WithOtelCollector(otelCollector);

builder.Build().Run();
