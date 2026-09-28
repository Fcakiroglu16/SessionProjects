using A2A;
using A2A.AspNetCore;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SessionProjects.Agents.Common;

// Uzman agent'ı A2A (Agent-to-Agent) protokolüyle dışarı açar:
//   POST {publicUrl}/a2a                         -> JSON-RPC A2A endpoint
//   GET  {publicUrl}/.well-known/agent-card.json -> agent card (keşif)
// Orkestratör agent card'ı A2ACardResolver ile okuyup bu agent'ı kendi aracı olarak kullanır.
public static class A2AHostingExtensions
{
    public const string A2APath = "/a2a";

    // Aspire, agent'ın dışarıdan erişilen kendi adresini bu değişkenle verir (agent card'daki URL)
    public const string PublicUrlEnvironmentVariable = "A2A_PUBLIC_URL";

    public static AgentCard CreateAgentCard(AIAgent agent, params A2A.AgentSkill[] skills)
    {
        var publicUrl = Environment.GetEnvironmentVariable(PublicUrlEnvironmentVariable)
                        ?? $"http://localhost:{Environment.GetEnvironmentVariable("PORT") ?? "8088"}";

        return new AgentCard
        {
            Name = agent.Name ?? agent.Id,
            Description = agent.Description ?? string.Empty,
            Version = "1.0.0",
            SupportedInterfaces =
            [
                new AgentInterface { Url = publicUrl.TrimEnd('/') + A2APath, ProtocolBinding = "JSONRPC" }
            ],
            Capabilities = new AgentCapabilities { Streaming = false },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [.. skills]
        };
    }

    public static IServiceCollection AddA2AAgent(this IServiceCollection services, AIAgent agent)
    {
        return services.AddA2AServer(agent);
    }

    public static void MapA2AAgent(this IEndpointRouteBuilder endpoints, AIAgent agent, AgentCard card)
    {
        endpoints.MapA2AJsonRpc(agent, A2APath);
        endpoints.MapWellKnownAgentCard(card);
    }
}
