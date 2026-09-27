namespace SessionProjects.Contracts;

public static class RabbitMqConstants
{
    public const string ConnectionName = "rabbitmq";
    public const string OrderExchange = "order.exchange";
    public const string OrderCreatedRoutingKey = "order.created";
    public const string ProductOrderCreatedQueue = "microservice2.order-created.queue";
}
