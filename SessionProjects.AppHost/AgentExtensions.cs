namespace SessionProjects.AppHost;

public static class AgentExtensions
{
    // A2A agent card'ında yayınlanacak, agent'ın dışarıdan erişilen kendi adresi
    public static IResourceBuilder<ProjectResource> WithA2APublicUrl(this IResourceBuilder<ProjectResource> agent)
    {
        return agent.WithEnvironment("A2A_PUBLIC_URL", agent.GetEndpoint("http"));
    }
}
