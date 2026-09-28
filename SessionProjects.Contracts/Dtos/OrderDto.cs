namespace SessionProjects.Contracts.Dtos;

public record OrderItemDto(int ProductId, string ProductName, int Quantity, decimal UnitPrice);

public record ShipmentDto(
    string Status,
    string Carrier,
    string TrackingNumber,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime? EstimatedDeliveryAt);

public record OrderDto(
    string OrderId,
    string CustomerId,
    DateTime OrderedAt,
    IReadOnlyList<OrderItemDto> Items,
    decimal TotalAmount,
    ShipmentDto Shipment);

public record CustomerDto(
    string CustomerId,
    string FullName,
    DateTime MemberSince,
    int PreviousComplaintCount,
    string Segment);
