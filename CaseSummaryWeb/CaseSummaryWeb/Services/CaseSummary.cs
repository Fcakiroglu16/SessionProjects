using Markdig;

namespace CaseSummaryWeb.Services;

// Agent cevabı sabit başlıklarla gelir (### Özet, ### Önerilen aksiyon ...). UI bu bölümleri ayrı kartlara böler.
public sealed class CaseSummary
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private readonly Dictionary<string, string> _sections;

    private CaseSummary(Dictionary<string, string> sections) => _sections = sections;

    public string Summary => Get("Özet");
    public string CheckedFacts => Get("Kontrol edilen bilgiler");
    public string RecommendedAction => Get("Önerilen aksiyon");
    public string Rationale => Get("Gerekçe");
    public string Priority => Get("Öncelik");
    public string Uncertainties => Get("Belirsizlikler");

    // Öncelik bölümü "Normal; ..." ya da "Yüksek; ..." ile başlar. Açıklamada "yüksek" geçebileceği için
    // sadece ilk kelimeye bakılır.
    public bool IsHighPriority => Priority.TrimStart('*', ' ').StartsWith("Yüksek", StringComparison.OrdinalIgnoreCase);
    public bool HasRecommendation => RecommendedAction.Length > 0;

    public static CaseSummary Parse(string markdown)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var buffer = new List<string>();

        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith("### "))
            {
                Flush();
                current = line[4..].Trim();
                continue;
            }
            buffer.Add(line);
        }
        Flush();
        return new CaseSummary(sections);

        void Flush()
        {
            if (current is not null) sections[current] = string.Join('\n', buffer).Trim();
            buffer.Clear();
        }
    }

    public static string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);

    private string Get(string key) => _sections.TryGetValue(key, out var value) ? value : "";
}
