using Microservice2.API.Messaging;
using Microservice2.API.Products;
using SessionProjects.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient(RabbitMqConstants.ConnectionName);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<ProductRepository>();
builder.Services.AddHostedService<OrderCreatedEventConsumer>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/api/products", (ProductRepository repository) => repository.GetAll())
    .WithName("GetProducts");

app.MapGet("/api/products/{id:int}", (int id, ProductRepository repository) =>
        repository.GetById(id) is { } product ? Results.Ok(product) : Results.NotFound())
    .WithName("GetProductById");

app.Run();
