using A2A;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SessionProjects.Agents.Common;

// Uzak bir A2A agent'ını orkestratöre araç (AIFunction) olarak verir.
// Agent card ilk kullanımda A2ACardResolver ile çözülür; uzman agent o an kapalıysa araç hata metni döner
// ve bir sonraki çağrıda tekrar dener. Böylece orkestratör, uzmanlardan biri ayakta olmasa da çalışır.
public sealed class RemoteAgentTool(string toolName, string description, Uri baseUrl, HttpClient httpClient)
{
    private static readonly TimeSpan CardResolveTimeout = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private AIAgent? _agent;

    public AIFunction AsAIFunction()
    {
        return AIFunctionFactory.Create(AskAsync, new AIFunctionFactoryOptions
        {
            Name = toolName,
            Description = description
        });
    }

    [System.ComponentModel.Description("Uzman agent'a doğal dilde bir soru/görev gönderir ve cevabını döner.")]
    private async Task<string> AskAsync(
        [System.ComponentModel.Description("Uzman agent'a sorulacak soru veya verilecek görev")] string question,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = await GetAgentAsync(cancellationToken);
            var response = await agent.RunAsync(question, cancellationToken: cancellationToken);
            return response.Text;
        }
        catch (Exception ex)
        {
            return $"HATA: '{toolName}' uzman agent'ına ulaşılamadı ({baseUrl}): {ex.Message}";
        }
    }

    private async Task<AIAgent> GetAgentAsync(CancellationToken cancellationToken)
    {
        if (_agent is not null) return _agent;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_agent is not null) return _agent;

            // Uzman agent henüz başlamadıysa Aspire proxy bağlantıyı kabul edip bekletir; kısa bir süre sınırı
            // koymazsak orkestratörün bütün isteği bu tek çağrıda asılı kalır.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CardResolveTimeout);

            var resolver = new A2ACardResolver(baseUrl, httpClient);
            _agent = await resolver.GetAIAgentAsync(httpClient, cancellationToken: timeout.Token);
            return _agent;
        }
        finally
        {
            _lock.Release();
        }
    }
}
