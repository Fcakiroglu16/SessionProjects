using System.Collections.Concurrent;
using SessionProjects.Contracts.Dtos;

namespace Microservice1.API.Orders;

// Demo amaçlı in-memory sipariş ve müşteri deposu.
// Tarihler "bugünden X gün önce" olarak üretilir; böylece iade süresi gibi politika pencereleri her gün geçerli kalır.
public class OrderRepository
{
    private readonly ConcurrentDictionary<string, OrderDto> _orders = new();
    private readonly ConcurrentDictionary<string, CustomerDto> _customers = new();

    public OrderRepository()
    {
        var now = DateTime.UtcNow;

        AddCustomer(new("C-1001", "Ayşe Yılmaz", now.AddYears(-2), 0, "Standart"));
        AddCustomer(new("C-1002", "Mehmet Kaya", now.AddYears(-1), 0, "Standart"));
        AddCustomer(new("C-1003", "Zeynep Demir", now.AddMonths(-8), 1, "Standart"));
        AddCustomer(new("C-1004", "Can Öztürk", now.AddYears(-3), 0, "Premium"));
        AddCustomer(new("C-1005", "Elif Şahin", now.AddYears(-4), 2, "Premium"));

        // 1) Kargoda, henüz teslim edilmemiş
        AddOrder(new("ORD-1001", "C-1001", now.AddDays(-2),
            [new(1, "Kalem", 3, 25.50m)], 76.50m,
            new("InTransit", "Yurtiçi Kargo", "YK-558210", now.AddDays(-1), null, now.AddDays(1))));

        // 2) 3 gün önce teslim edilmiş, ürün stokta var
        AddOrder(new("ORD-1002", "C-1002", now.AddDays(-6),
            [new(1, "Kalem", 10, 25.50m)], 255m,
            new("Delivered", "Aras Kargo", "AR-771034", now.AddDays(-5), now.AddDays(-3), null)));

        // 3) 2 gün önce teslim edilmiş, ürünün stoğu yok
        AddOrder(new("ORD-1003", "C-1003", now.AddDays(-5),
            [new(4, "Deri Ajanda", 1, 450m)], 450m,
            new("Delivered", "MNG Kargo", "MN-330981", now.AddDays(-4), now.AddDays(-2), null)));

        // 4) 20 gün önce teslim edilmiş, yüksek tutarlı
        AddOrder(new("ORD-1004", "C-1004", now.AddDays(-24),
            [new(5, "Dolma Kalem", 1, 2000m)], 2000m,
            new("Delivered", "Yurtiçi Kargo", "YK-220457", now.AddDays(-22), now.AddDays(-20), null)));

        // 5) Tahmini teslim tarihi 6 gün geçmiş, hâlâ kargoda
        AddOrder(new("ORD-1005", "C-1005", now.AddDays(-12),
            [new(2, "Defter", 5, 60m)], 300m,
            new("InTransit", "Aras Kargo", "AR-119876", now.AddDays(-10), null, now.AddDays(-6))));
    }

    public OrderDto? GetOrder(string orderId) => _orders.GetValueOrDefault(orderId);

    public IReadOnlyList<OrderDto> GetCustomerOrders(string customerId) =>
        _orders.Values.Where(o => o.CustomerId == customerId).OrderByDescending(o => o.OrderedAt).ToList();

    public CustomerDto? GetCustomer(string customerId) => _customers.GetValueOrDefault(customerId);

    private void AddOrder(OrderDto order) => _orders[order.OrderId] = order;

    private void AddCustomer(CustomerDto customer) => _customers[customer.CustomerId] = customer;
}
