namespace CaseSummaryWeb.Services;

// Demo destek masası: gelen talepler ve temsilcinin kararları. Kararlar bellekte tutulur.
// Agent hiçbir aksiyonu uygulamaz; önerisini kabul eden, düzenleyen ya da reddeden her zaman temsilcidir.
public sealed class SupportDesk
{
    private readonly List<RepresentativeDecision> _decisions = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<SupportTicket> Tickets { get; } =
    [
        new("T-501", "C-1001", "ORD-1001", "Ahmet Yılmaz", "Siparişim nerede?",
            "Merhaba, siparişim nerede? Ne zaman gelir?", "E-posta", DateTime.Now.AddMinutes(-42)),
        new("T-502", "C-1002", "ORD-1002", "Mehmet Kaya", "Kırık kalemler",
            "Aldığım kalemlerden bazıları kırık geldi, yenisini istiyorum.", "Canlı destek", DateTime.Now.AddMinutes(-35)),
        new("T-503", "C-1003", "ORD-1003", "Zeynep Demir", "Ajanda hasarlı geldi",
            "Deri ajandanın kapağı yırtık geldi, hasarlı ürün gönderilmiş.", "E-posta", DateTime.Now.AddMinutes(-21)),
        new("T-504", "C-1004", "ORD-1004", "Can Öztürk", "Dolma kalem iadesi",
            "Dolma kalemi beğenmedim, iade etmek istiyorum.", "Web formu", DateTime.Now.AddMinutes(-12)),
        new("T-505", "C-1005", "ORD-1005", "Elif Şahin", "Sipariş hâlâ gelmedi",
            "Bu üçüncü kez oluyor! Siparişim hâlâ gelmedi, avukatıma gideceğim.", "Canlı destek", DateTime.Now.AddMinutes(-3))
    ];

    public IReadOnlyList<RepresentativeDecision> Decisions
    {
        get { lock (_lock) return [.. _decisions]; }
    }

    public RepresentativeDecision? DecisionFor(string ticketId)
    {
        lock (_lock) return _decisions.LastOrDefault(d => d.TicketId == ticketId);
    }

    public void Record(RepresentativeDecision decision)
    {
        lock (_lock) _decisions.Add(decision);
    }
}

public sealed record SupportTicket(
    string Id,
    string CustomerId,
    string OrderId,
    string CustomerName,
    string Subject,
    string Message,
    string Channel,
    DateTime ReceivedAt)
{
    // Agent'a giden metin: Postman örnekleriyle aynı format.
    public string ToAgentInput() => $"Müşteri: {CustomerId}, Sipariş: {OrderId}. Mesaj: {Message}";
}

public enum DecisionType { Approved, Edited, Rejected }

public sealed record RepresentativeDecision(
    string TicketId,
    DecisionType Type,
    string AgentRecommendation,
    string FinalAction,
    string? Note,
    DateTime DecidedAt);
