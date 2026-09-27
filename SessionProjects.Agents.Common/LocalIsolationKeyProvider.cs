using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable OPENAI001 // Foundry hosting session API'leri preview

namespace SessionProjects.Agents.Common;

// Foundry platformu her isteğe x-agent-user-isolation-key / x-agent-chat-isolation-key header'larını ekler.
// Local'de (Aspire, curl) bu header'lar olmadığı için varsayılan sağlayıcı null döner ve istek 500 ile düşer.
// Bu sağlayıcı header varsa onları kullanır, yoksa sabit local anahtarlara düşer.
public sealed class LocalIsolationKeyProvider : HostedSessionIsolationKeyProvider
{
    private const string LocalUserKey = "local-user";
    private const string LocalChatKey = "local-chat";

    public override ValueTask<HostedSessionContext?> GetKeysAsync(ResponseContext context, CreateResponse request,
        CancellationToken cancellationToken = default)
    {
        var userKey = context.Isolation.UserIsolationKey;
        var chatKey = context.Isolation.ChatIsolationKey;

        return ValueTask.FromResult<HostedSessionContext?>(new HostedSessionContext(
            string.IsNullOrEmpty(userKey) ? LocalUserKey : userKey,
            string.IsNullOrEmpty(chatKey) ? LocalChatKey : chatKey));
    }
}

public static class LocalIsolationKeyProviderExtensions
{
    public static IServiceCollection AddLocalIsolationKeyFallback(this IServiceCollection services)
    {
        return services.AddSingleton<HostedSessionIsolationKeyProvider, LocalIsolationKeyProvider>();
    }
}
