using System.Net;
using System.Text;
using System.Text.Json;
using CaseSummaryAgent.Tools;
using Microsoft.Extensions.AI;
using SessionProjects.Contracts.Dtos;

namespace CaseSummaryAgent.Tests;

// Araçların LLM olmadan doğrulanması: servis yanıtlarından hesaplanan alanlar ve "bulunamadı" davranışı.
public class SupportDataToolsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetOrder_ComputesDaysSinceDelivery()
    {
        var now = DateTime.UtcNow;
        var order = new OrderDto("ORD-1", "C-1", now.AddDays(-6), [new(1, "Kalem", 10, 25.5m)], 255m,
            new ShipmentDto("Delivered", "Aras Kargo", "AR-1", now.AddDays(-5), now.AddDays(-3), null));

        var result = await InvokeAsync(CreateTools(("/api/orders/ORD-1", order)), "get_order", new() { ["orderId"] = "ORD-1" });

        Assert.True(result.GetProperty("found").GetBoolean());
        var shipment = result.GetProperty("shipment");
        Assert.True(shipment.GetProperty("delivered").GetBoolean());
        Assert.Equal(3, shipment.GetProperty("daysSinceDelivery").GetInt32());
        Assert.True(shipment.GetProperty("withinReturnWindow").GetBoolean());
        Assert.Equal(0, shipment.GetProperty("daysPastEstimatedDelivery").GetInt32());
    }

    [Fact]
    public async Task GetOrder_ComputesDelayForUndeliveredOrderPastEstimate()
    {
        var now = DateTime.UtcNow;
        var order = new OrderDto("ORD-5", "C-5", now.AddDays(-12), [new(2, "Defter", 5, 60m)], 300m,
            new ShipmentDto("InTransit", "Aras Kargo", "AR-5", now.AddDays(-10), null, now.AddDays(-6)));

        var result = await InvokeAsync(CreateTools(("/api/orders/ORD-5", order)), "get_order", new() { ["orderId"] = "ORD-5" });

        var shipment = result.GetProperty("shipment");
        Assert.False(shipment.GetProperty("delivered").GetBoolean());
        Assert.False(shipment.TryGetProperty("daysSinceDelivery", out var days) && days.ValueKind != JsonValueKind.Null);
        Assert.Equal(6, shipment.GetProperty("daysPastEstimatedDelivery").GetInt32());
    }

    [Fact]
    public async Task GetOrder_FlagsDeliveryOlderThan14DaysAsOutsideReturnWindow()
    {
        var now = DateTime.UtcNow;
        var order = new OrderDto("ORD-4", "C-4", now.AddDays(-25), [new(5, "Dolma Kalem", 1, 2000m)], 2000m,
            new ShipmentDto("Delivered", "Aras Kargo", "AR-4", now.AddDays(-23), now.AddDays(-20), null));

        var result = await InvokeAsync(CreateTools(("/api/orders/ORD-4", order)), "get_order", new() { ["orderId"] = "ORD-4" });

        var shipment = result.GetProperty("shipment");
        Assert.Equal(20, shipment.GetProperty("daysSinceDelivery").GetInt32());
        Assert.False(shipment.GetProperty("withinReturnWindow").GetBoolean());
    }

    [Fact]
    public async Task GetOrder_ReturnsNotFound()
    {
        var result = await InvokeAsync(CreateTools(), "get_order", new() { ["orderId"] = "ORD-404" });

        Assert.False(result.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task GetProductStock_ReportsOutOfStock()
    {
        var result = await InvokeAsync(CreateTools(("/api/products/4", new ProductDto(4, "Deri Ajanda", 450m, 0))),
            "get_product_stock", new() { ["productId"] = 4 });

        Assert.False(result.GetProperty("inStock").GetBoolean());
    }

    [Fact]
    public async Task GetCustomer_IncludesComplaintHistoryAndOrderCount()
    {
        var customer = new CustomerDto("C-5", "Elif Şahin", DateTime.UtcNow.AddYears(-4), 2, "Premium");
        var tools = CreateTools(("/api/customers/C-5", customer), ("/api/customers/C-5/orders", Array.Empty<OrderDto>()));

        var result = await InvokeAsync(tools, "get_customer", new() { ["customerId"] = "C-5" });

        Assert.Equal(2, result.GetProperty("previousComplaintCount").GetInt32());
        Assert.Equal(4, result.GetProperty("membershipYears").GetInt32());
        Assert.Equal("Premium", result.GetProperty("segment").GetString());
    }

    [Fact]
    public void Tools_AreReadOnlyLookups()
    {
        var names = CreateTools().AsAITools().Select(t => t.Name).ToArray();

        Assert.Equal(["get_order", "get_customer", "get_product_stock"], names);
    }

    private static SupportDataTools CreateTools(params (string Path, object Body)[] responses)
    {
        var client = new HttpClient(new StubHandler(responses)) { BaseAddress = new Uri("http://services") };
        return new SupportDataTools(client, client);
    }

    private static async Task<JsonElement> InvokeAsync(SupportDataTools tools, string name, AIFunctionArguments arguments)
    {
        var function = tools.AsAITools().OfType<AIFunction>().Single(t => t.Name == name);
        var result = await function.InvokeAsync(arguments);
        return result is JsonElement element ? element : JsonSerializer.SerializeToElement(result, Web);
    }

    private sealed class StubHandler((string Path, object Body)[] responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var match = responses.FirstOrDefault(r => r.Path == request.RequestUri!.AbsolutePath);
            return Task.FromResult(match.Path is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(match.Body, Web), Encoding.UTF8, "application/json")
                });
        }
    }
}
