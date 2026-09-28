var builder = DistributedApplication.CreateBuilder(args);

// Local geliştirme: sadece mikroservisler ve bağımlılıkları.
// Gözlemlenebilirlik (SigNoz, Prometheus, Grafana) ve agent'lar Kubernetes'te çalışır: k8s/deploy.sh
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var microservice2 = builder.AddProject<Projects.Microservice2_API>("microservice2-api")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.Microservice1_API>("microservice1-api")
    .WithReference(microservice2)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WaitFor(microservice2);

builder.Build().Run();
