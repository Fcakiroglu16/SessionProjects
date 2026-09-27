using System.Text.Json;
using Microservice2.API.Products;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SessionProjects.Contracts;
using SessionProjects.Contracts.Events;

namespace Microservice2.API.Messaging;

public class OrderCreatedEventConsumer(
    IConnection connection,
    ProductRepository productRepository,
    ILogger<OrderCreatedEventConsumer> logger) : BackgroundService
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(RabbitMqConstants.OrderExchange, ExchangeType.Direct, durable: true,
            cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(RabbitMqConstants.ProductOrderCreatedQueue, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(RabbitMqConstants.ProductOrderCreatedQueue, RabbitMqConstants.OrderExchange,
            RabbitMqConstants.OrderCreatedRoutingKey, cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var @event = JsonSerializer.Deserialize<OrderCreatedEvent>(ea.Body.Span)!;

                var stockDecreased = productRepository.DecreaseStock(@event.ProductId, @event.Quantity);
                logger.LogInformation(
                    "OrderCreatedEvent alındı. OrderId: {OrderId}, ProductId: {ProductId}, Quantity: {Quantity}, StockDecreased: {StockDecreased}",
                    @event.OrderId, @event.ProductId, @event.Quantity, stockDecreased);

                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OrderCreatedEvent işlenemedi");
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(RabbitMqConstants.ProductOrderCreatedQueue, autoAck: false, consumer,
            stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.CloseAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
