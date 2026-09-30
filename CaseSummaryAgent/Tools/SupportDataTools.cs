using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using SessionProjects.Contracts.Dtos;

namespace CaseSummaryAgent.Tools;

// Agent'ın mikroservislerden bilgi topladığı araçlar. Hepsi SALT OKUNUR: hiçbir araç sipariş, stok veya
// müşteri verisini değiştirmez. Tarih farkı gibi hesaplamalar burada, kodda yapılır; model sadece yorumlar.
public sealed class SupportDataTools(HttpClient orderService, HttpClient productService)
{
    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(GetOrderAsync, new AIFunctionFactoryOptions { Name = "get_order" });
        yield return AIFunctionFactory.Create(GetCustomerAsync, new AIFunctionFactoryOptions { Name = "get_customer" });
        yield return AIFunctionFactory.Create(GetProductStockAsync, new AIFunctionFactoryOptions { Name = "get_product_stock" });
    }

    // İade/değişim süresi (politika madde 3.1 / 3.2). Karşılaştırma modelde değil burada yapılır.
    private const int ReturnWindowDays = 14;

    [Description("Siparişi, kalemlerini, tutarını ve kargo durumunu getirir. Teslimattan bu yana geçen gün " +
                 "(daysSinceDelivery), 14 günlük iade süresi içinde olup olmadığı (withinReturnWindow) ve tahmini " +
                 "teslimden bu yana geçen gün (daysPastEstimatedDelivery) hesaplanmış gelir.")]
    private async Task<object> GetOrderAsync(
        [Description("Sipariş numarası, ör. ORD-1002")] string orderId,
        CancellationToken cancellationToken)
    {
        var order = await GetOrNullAsync<OrderDto>(orderService, $"/api/orders/{Uri.EscapeDataString(orderId)}",
            cancellationToken);
        if (order is null) return new { found = false, orderId, message = "Sipariş bulunamadı." };

        var now = DateTime.UtcNow;
        var shipment = order.Shipment;
        int? daysSinceDelivery = shipment.DeliveredAt is { } deliveredAt ? (int)(now - deliveredAt).TotalDays : null;
        return new
        {
            found = true,
            order.OrderId,
            order.CustomerId,
            order.OrderedAt,
            order.Items,
            order.TotalAmount,
            shipment = new
            {
                shipment.Status,
                shipment.Carrier,
                shipment.TrackingNumber,
                shipment.ShippedAt,
                shipment.DeliveredAt,
                shipment.EstimatedDeliveryAt,
                // null alanlar serileştirmede atlandığı için teslim durumu ayrıca açıkça verilir
                delivered = shipment.DeliveredAt is not null,
                daysSinceDelivery,
                // teslim edilmemiş siparişte süre henüz başlamamıştır
                withinReturnWindow = daysSinceDelivery is null or <= ReturnWindowDays,
                daysPastEstimatedDelivery = shipment.DeliveredAt is null && shipment.EstimatedDeliveryAt is { } eta && eta < now
                    ? (int)(now - eta).TotalDays
                    : 0
            }
        };
    }

    [Description("Müşteri profilini (üyelik süresi, segment, geçmiş şikâyet sayısı) ve toplam sipariş sayısını getirir.")]
    private async Task<object> GetCustomerAsync(
        [Description("Müşteri numarası, ör. C-1002")] string customerId,
        CancellationToken cancellationToken)
    {
        var customer = await GetOrNullAsync<CustomerDto>(orderService,
            $"/api/customers/{Uri.EscapeDataString(customerId)}", cancellationToken);
        if (customer is null) return new { found = false, customerId, message = "Müşteri bulunamadı." };

        var orders = await orderService.GetFromJsonAsync<List<OrderDto>>(
            $"/api/customers/{Uri.EscapeDataString(customerId)}/orders", cancellationToken) ?? [];
        return new
        {
            found = true,
            customer.CustomerId,
            customer.FullName,
            customer.Segment,
            customer.MemberSince,
            membershipYears = (int)((DateTime.UtcNow - customer.MemberSince).TotalDays / 365),
            customer.PreviousComplaintCount,
            totalOrderCount = orders.Count
        };
    }

    [Description("Ürünün güncel stok miktarını getirir. Değişim önerilmeden önce stok kontrolü için kullanılır.")]
    private async Task<object> GetProductStockAsync(
        [Description("Ürün numarası (siparişteki productId)")] int productId,
        CancellationToken cancellationToken)
    {
        var product = await GetOrNullAsync<ProductDto>(productService, $"/api/products/{productId}", cancellationToken);
        return product is null
            ? new { found = false, productId, message = "Ürün bulunamadı." }
            : new { found = true, product.Id, product.Name, product.Stock, inStock = product.Stock > 0 };
    }

    private static async Task<T?> GetOrNullAsync<T>(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
}
