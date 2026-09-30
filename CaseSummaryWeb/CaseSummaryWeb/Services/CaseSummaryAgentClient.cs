using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CaseSummaryWeb.Services;

// CaseSummaryAgent ile AG-UI üzerinden konuşur. AGUIChatClient bir IChatClient'tır: agent'ın sunucuda çalıştırdığı
// tool çağrıları FunctionCallContent / FunctionResultContent, cevap metni TextContent olarak akış halinde gelir.
// Burada tool'lar ÇALIŞTIRILMAZ; UI yalnızca agent'ın ne yaptığını gösterir.
public sealed class CaseSummaryAgentClient(IChatClient agent)
{
    public async IAsyncEnumerable<AgentEvent> RunAsync(IReadOnlyList<ChatMessage> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in agent.GetStreamingResponseAsync(history, cancellationToken: cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        yield return new ToolCallStarted(call.CallId, call.Name, ToDictionary(call.Arguments));
                        break;
                    case FunctionResultContent result:
                        yield return new ToolCallCompleted(result.CallId, ToJson(result.Result));
                        break;
                    case TextContent { Text.Length: > 0 } text:
                        yield return new TextDelta(text.Text);
                        break;
                }
            }
        }
    }

    private static IReadOnlyDictionary<string, string> ToDictionary(IDictionary<string, object?>? arguments) =>
        arguments?.ToDictionary(a => a.Key, a => a.Value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } s => s.GetString() ?? "",
            null => "",
            var v => v.ToString() ?? ""
        }) ?? new Dictionary<string, string>();

    // Sonuç JsonElement, string ya da string içinde JSON olarak gelebilir; hepsi JsonElement'e çevrilir.
    private static JsonElement? ToJson(object? result)
    {
        try
        {
            var element = result switch
            {
                JsonElement e => e,
                string s => JsonSerializer.SerializeToElement(s),
                null => (JsonElement?)null,
                var other => JsonSerializer.SerializeToElement(other)
            };
            if (element is { ValueKind: JsonValueKind.String } str && str.GetString() is { } raw &&
                raw.TrimStart().StartsWith('{'))
                return JsonDocument.Parse(raw).RootElement.Clone();
            return element;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public abstract record AgentEvent;
public sealed record ToolCallStarted(string CallId, string Name, IReadOnlyDictionary<string, string> Arguments) : AgentEvent;
public sealed record ToolCallCompleted(string CallId, JsonElement? Result) : AgentEvent;
public sealed record TextDelta(string Text) : AgentEvent;
