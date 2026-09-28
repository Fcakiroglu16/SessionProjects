using System.ComponentModel;
using System.Globalization;
using Microsoft.Agents.AI;

namespace CaseSummaryAgent.SkillDefinitions;

// Kodla tanımlanmış (inline) agent skill örneği: talimat + statik kaynak + deterministik script.
// Dosya tabanlı skill'den (skills/return-exchange-policy) farkı: kurallar derlenmiş kodla birlikte gelir ve
// script'ler C# delegate'i olarak çalışır (ayrı bir script runner gerekmez).
public static class EscalationRulesSkill
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Kaynak ("sensitive-phrases") ve script aynı listeyi kullanır; ikisi birbirinden sapamaz
    private static readonly Dictionary<string, string[]> SensitivePhrases = new()
    {
        ["legal_threat"] = ["avukat", "dava", "mahkeme", "hukuki", "tüketici hakem", "savcılık"],
        ["public_complaint_threat"] = ["sosyal medya", "şikayetvar", "twitter", "herkese duyuracağım"],
        ["repeat_complaint"] = ["üçüncü kez", "ikinci kez", "yine aynı", "tekrar oldu", "her seferinde", "kaçıncı kez"],
        ["strong_dissatisfaction"] = ["rezalet", "berbat", "bir daha asla", "dolandırıcı"]
    };

    public static AgentSkill Create()
    {
        var skill = new AgentInlineSkill(
            name: "escalation-rules",
            description: "Müşteri talebinin önceliğini (Normal/Yüksek) ve kıdemli temsilciye yönlendirilmesi gerekip " +
                         "gerekmediğini belirleyen kurallar. Her talepte kullanılmalıdır.",
            instructions: """
                # Öncelik ve eskalasyon kuralları

                1. Her talepte önce `detect_sensitive_content` script'ini müşterinin mesajıyla çalıştır.
                2. Öncelik **Yüksek** olur eğer:
                   - script `legal_threat` veya `public_complaint_threat` bulduysa,
                   - müşterinin geçmiş şikâyet sayısı (get_customer → previousComplaintCount) 2 veya fazlaysa,
                   - kargo tahmini teslimden 7 gün veya daha fazla gecikmişse,
                   - politika dışı bir istisna 1.000 ₺ ve üzeri bir siparişi kapsıyorsa.
                   Aksi halde öncelik **Normal**'dir.
                3. `legal_threat` varsa: talep **kıdemli temsilciye** yönlendirilmeli, müşteriye otomatik veya şablon
                   yanıt gönderilmemeli ve hukuki değerlendirme notu düşülmelidir.
                4. Premium segmentteki müşteriler için önerilen aksiyona "öncelikli ele alınmalı" notu ekle.
                5. Hangi sinyalin önceliği belirlediğini gerekçede açıkça yaz.
                """);

        skill.AddResource(
            "sensitive-phrases",
            string.Join('\n', SensitivePhrases.Select(p => $"{p.Key}: {string.Join(", ", p.Value)}")),
            "Eskalasyon gerektiren ifade kategorileri ve örnek ifadeler");

        skill.AddScript(
            "detect_sensitive_content",
            DetectSensitiveContent,
            "Müşteri mesajında hukuki tehdit, kamuya açık şikâyet tehdidi, tekrarlayan şikâyet ve güçlü " +
            "memnuniyetsizlik ifadelerini tespit eder.");

        return skill;
    }

    private static object DetectSensitiveContent(
        [Description("Müşterinin orijinal mesajı")] string message)
    {
        var text = message.ToLower(Turkish);
        var matches = SensitivePhrases
            .Select(category => new
            {
                category = category.Key,
                phrases = category.Value.Where(phrase => text.Contains(phrase, StringComparison.Ordinal)).ToArray()
            })
            .Where(match => match.phrases.Length > 0)
            .ToArray();

        return new
        {
            categories = matches.Select(m => m.category).ToArray(),
            matchedPhrases = matches.SelectMany(m => m.phrases).ToArray(),
            requiresSeniorAgent = matches.Any(m => m.category == "legal_threat")
        };
    }
}
