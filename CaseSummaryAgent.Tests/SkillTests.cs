using System.Text.Json;
using CaseSummaryAgent.SkillDefinitions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CaseSummaryAgent.Tests;

// Skill'lerin LLM olmadan doğrulanması: keşif, içerik, kaynak ve script davranışı.
public class SkillTests
{
    private static readonly string SkillsPath = Path.Combine(AppContext.BaseDirectory, "skills");

    [Fact]
    public async Task FileSkill_IsDiscovered_WithDecisionTableAndPolicyResource()
    {
        using var source = new AgentFileSkillsSource(SkillsPath,
            (_, _, _, _, _) => Task.FromResult<object?>(null));

        var skills = await source.GetSkillsAsync(await CreateContextAsync());

        var policy = Assert.Single(skills, s => s.Frontmatter.Name == "return-exchange-policy");
        var content = await policy.GetContentAsync();
        Assert.Contains("Karar tablosu", content);
        Assert.Contains("2.1", content);

        var resource = await policy.GetResourceAsync("references/policy.md");
        Assert.NotNull(resource);
        var text = (await resource.ReadAsync(EmptyServiceProvider.Instance))?.ToString();
        Assert.Contains("14 gün içinde", text);
    }

    [Fact]
    public async Task EscalationSkill_ExposesInstructionsResourceAndScript()
    {
        var skill = EscalationRulesSkill.Create();

        Assert.Equal("escalation-rules", skill.Frontmatter.Name);
        var content = await skill.GetContentAsync();
        Assert.Contains("detect_sensitive_content", content);
        Assert.Contains("sensitive-phrases", content);

        var resource = await skill.GetResourceAsync("sensitive-phrases");
        var phrases = (await resource!.ReadAsync(EmptyServiceProvider.Instance))?.ToString();
        Assert.Contains("legal_threat", phrases);
    }

    [Theory]
    [InlineData("Bu üçüncü kez oluyor! Siparişim hâlâ gelmedi, avukatıma gideceğim.", true, "legal_threat", "repeat_complaint")]
    [InlineData("AVUKAT ile görüşeceğim, DAVA açacağım", true, "legal_threat")]
    [InlineData("Aldığım kalemlerden bazıları kırık geldi, yenisini istiyorum.", false)]
    public async Task EscalationSkill_Script_DetectsSensitiveContent(string message, bool requiresSenior,
        params string[] expectedCategories)
    {
        var skill = EscalationRulesSkill.Create();
        var script = await skill.GetScriptAsync("detect_sensitive_content");
        Assert.NotNull(script);

        var arguments = JsonSerializer.SerializeToElement(new { message });
        var result = JsonSerializer.SerializeToElement(
            await script.RunAsync(skill, arguments, EmptyServiceProvider.Instance, CancellationToken.None));

        var categories = result.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ToArray();
        Assert.Equal(requiresSenior, result.GetProperty("requiresSeniorAgent").GetBoolean());
        Assert.Equal(expectedCategories.OrderBy(c => c), categories.OrderBy(c => c));
    }

    private static async Task<AgentSkillsSourceContext> CreateContextAsync()
    {
        var agent = new ChatClientAgent(new NoOpChatClient(), new ChatClientAgentOptions { Name = "test" });
        return new AgentSkillsSourceContext(agent, await agent.CreateSessionAsync());
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class NoOpChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
