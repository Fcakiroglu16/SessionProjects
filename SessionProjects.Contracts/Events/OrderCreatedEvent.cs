namespace SessionProjects.Contracts.Events;

public record OrderCreatedEvent(Guid OrderId, int ProductId, int Quantity, DateTime CreatedAt);
