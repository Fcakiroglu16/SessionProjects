using System.Text.Json;
using RabbitMQ.Client;
using SessionProjects.Contracts;

namespace Microservice1.API.Messaging;

public class RabbitMqPublisher(IConnection connection)
{
    public async Task PublishAsync<T>(T message, string routingKey, CancellationToken cancellationToken = default)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(RabbitMqConstants.OrderExchange, ExchangeType.Direct, durable: true,
            cancellationToken: cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Type = typeof(T).Name
        };

        await channel.BasicPublishAsync(RabbitMqConstants.OrderExchange, routingKey, mandatory: false, properties,
            body, cancellationToken);
    }
}
