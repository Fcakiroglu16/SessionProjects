using System.Text.Json;

namespace CaseSummaryWeb.Services;

// Agent'ın bir tool/skill çağrısının UI'daki karşılığı: hangi servise gidildi, ne soruldu, ne öğrenildi.
public sealed class ToolActivity(string callId, string name, IReadOnlyDictionary<string, string> arguments)
{
    public string CallId { get; } = callId;
    public string Name { get; } = name;
    public IReadOnlyDictionary<string, string> Arguments { get; } = arguments;
    public bool Completed { get; private set; }
    public IReadOnlyList<string> Facts { get; private set; } = [];

    public string Kind => Name switch
    {
        "get_order" or "get_customer" or "get_product_stock" => "tool",
        _ => "skill"
    };

    public string Icon => Name switch
    {
        "get_order" => "📦",
        "get_customer" => "👤",
        "get_product_stock" => "🏷️",
        "load_skill" => "📘",
        "read_skill_resource" => "📄",
        "run_skill_script" => "🔎",
        _ => "⚙️"
    };

    public string Title => Name switch
    {
        "get_order" => $"Sipariş sorgulandı · {Arg("orderId")}",
        "get_customer" => $"Müşteri profili · {Arg("customerId")}",
        "get_product_stock" => $"Stok kontrolü · ürün {Arg("productId")}",
        "load_skill" => $"Skill yüklendi · {Arg("skillName")}",
        "read_skill_resource" => $"Politika dokümanı okundu · {Arg("resourceName")}",
        "run_skill_script" => $"Script çalıştı · {Arg("scriptName")}",
        _ => Name
    };

    public string Source => Name switch
    {
        "get_order" or "get_customer" => "microservice1 · sipariş/müşteri",
        "get_product_stock" => "microservice2 · ürün/stok",
        _ => "agent skill"
    };

    public void Complete(JsonElement? result)
    {
        Completed = true;
        if (result is { ValueKind: JsonValueKind.Object } json)
            Facts = ExtractFacts(json);
    }

    private string Arg(string key) =>
        Arguments.TryGetValue(key, out var value) && value.Length > 0 ? value
        : Arguments.Values.LastOrDefault(v => v.Length > 0 && !v.StartsWith('{')) ?? "…";

    private List<string> ExtractFacts(JsonElement r)
    {
        if (r.TryGetProperty("found", out var found) && found.ValueKind == JsonValueKind.False)
            return ["Kayıt bulunamadı"];

        return Name switch
        {
            "get_customer" =>
            [
                $"{Str(r, "fullName")} · {Str(r, "segment")}",
                $"{Str(r, "membershipYears")} yıllık üye · {Str(r, "totalOrderCount")} sipariş",
                $"Geçmiş şikâyet: {Str(r, "previousComplaintCount")}"
            ],
            "get_order" => OrderFacts(r),
            "get_product_stock" =>
            [
                $"{Str(r, "name")}",
                r.TryGetProperty("inStock", out var inStock) && inStock.GetBoolean()
                    ? $"Stokta: {Str(r, "stock")} adet"
                    : "Stok yok"
            ],
            "run_skill_script" =>
            [
                r.TryGetProperty("requiresSeniorAgent", out var senior) && senior.GetBoolean()
                    ? "⚠️ Kıdemli temsilci gerekli"
                    : "Hassas içerik yok",
                .. r.TryGetProperty("categories", out var categories)
                    ? categories.EnumerateArray().Select(c => $"Kategori: {c.GetString()}")
                    : []
            ],
            _ => []
        };
    }

    private static List<string> OrderFacts(JsonElement r)
    {
        var facts = new List<string> { $"Tutar: {Str(r, "totalAmount")} ₺" };
        if (r.TryGetProperty("items", out var items))
            facts.Add(string.Join(", ", items.EnumerateArray()
                .Select(i => $"{Str(i, "productName")} ×{Str(i, "quantity")}")));
        if (r.TryGetProperty("shipment", out var s))
        {
            var delivered = s.TryGetProperty("delivered", out var d) && d.GetBoolean();
            facts.Add(delivered
                ? $"Teslim edildi · {Str(s, "daysSinceDelivery")} gün önce"
                : $"Kargoda · tahmini teslimden {Str(s, "daysPastEstimatedDelivery")} gün geçti");
        }
        return facts;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()
            : "-";
}
