using Microservice1.API.Messaging;
using Microservice1.API.Orders;
using SessionProjects.Contracts;
using SessionProjects.Contracts.Dtos;
using SessionProjects.Contracts.Events;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient(RabbitMqConstants.ConnectionName);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<OrderRepository>();

// Aspire service discovery: "microservice2-api" AppHost'taki resource adı
builder.Services.AddHttpClient("microservice2",
    client => client.BaseAddress = new Uri("https+http://microservice2-api"));

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Senkron iletişim: Microservice2'den ürünleri HTTP ile çeker
app.MapGet("/api/products", async (IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
    {
        var client = httpClientFactory.CreateClient("microservice2");
        var products = await client.GetFromJsonAsync<List<ProductDto>>("/api/products", cancellationToken);
        return Results.Ok(products);
    })
    .WithName("GetProductsFromMicroservice2");

// Asenkron iletişim: RabbitMQ'ya OrderCreatedEvent publish eder, Microservice2 consume eder
app.MapPost("/api/orders", async (CreateOrderRequest request, RabbitMqPublisher publisher,
        CancellationToken cancellationToken) =>
    {
        var @event = new OrderCreatedEvent(Guid.NewGuid(), request.ProductId, request.Quantity, DateTime.UtcNow);

        await publisher.PublishAsync(@event, RabbitMqConstants.OrderCreatedRoutingKey, cancellationToken);

        return Results.Accepted(value: @event);
    })
    .WithName("CreateOrder");

// Sipariş geçmişi ve kargo durumu (müşteri talebi agent'ı bu endpoint'leri salt okunur kullanır)
app.MapGet("/api/orders/{orderId}", (string orderId, OrderRepository repository) =>
        repository.GetOrder(orderId) is { } order ? Results.Ok(order) : Results.NotFound())
    .WithName("GetOrder");

app.MapGet("/api/customers/{customerId}", (string customerId, OrderRepository repository) =>
        repository.GetCustomer(customerId) is { } customer ? Results.Ok(customer) : Results.NotFound())
    .WithName("GetCustomer");

app.MapGet("/api/customers/{customerId}/orders", (string customerId, OrderRepository repository) =>
        Results.Ok(repository.GetCustomerOrders(customerId)))
    .WithName("GetCustomerOrders");

app.Run();

record CreateOrderRequest(int ProductId, int Quantity);
