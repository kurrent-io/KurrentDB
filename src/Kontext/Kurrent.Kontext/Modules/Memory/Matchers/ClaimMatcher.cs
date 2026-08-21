using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntimeGenAI;
using OpenAI;

namespace Kurrent.Kontext.Memory.Matchers;

public class ClaimMatcher(IChatClient chatClient, Action<ChatOptions>? configureOptions = null, string? systemPrompt = null) : IClaimMatcher {
    readonly string _systemPrompt = systemPrompt ?? DefaultSystemPrompt;

    const string DefaultSystemPrompt = """
        You are a strict data deduction engine. Compare two claims.
        If they mean exactly the same truth, fact, or logical point, match is true.
        If they describe different metrics, colors, or subjects, match is false.
        """;

    public async Task<bool> IsPureDuplicateAsync(string claim1, string claim2) {
        var options = new ChatOptions {
            ResponseFormat = ChatResponseFormat.ForJsonSchema<ClaimMatchResult>(),
            Temperature = 0.0f,
        };

        configureOptions?.Invoke(options);

        options.ResponseFormat = ChatResponseFormat.ForJsonSchema<ClaimMatchResult>();

        var userPrompt = $"Claim A: \"{claim1}\"\nClaim B: \"{claim2}\"";

        var messages = new List<ChatMessage> {
            new ChatMessage(ChatRole.System, _systemPrompt), 
            new ChatMessage(ChatRole.User, userPrompt)
        };

        var response = await chatClient
            .GetResponseAsync(messages, options)
            .ConfigureAwait(false);

        return JsonSerializer.Deserialize<ClaimMatchResult>(response.Text) is { Match: true };
    }
    
    public class ClaimMatchResult {
        public bool Match { get; set; }
    }
}

public abstract class ClaimMatcherBase(IChatClient chatClient) : IClaimMatcher {
    IChatClient ChatClient { get; } = chatClient;

    const string DefaultSystemPrompt = """
        You are a strict data deduction engine. Compare two claims.
        If they mean exactly the same truth, fact, or logical point, match is true.
        If they describe different metrics, colors, or subjects, match is false.
        """;

    protected string SystemPrompt { get; } = DefaultSystemPrompt;

    protected virtual void ConfigureChatOptions(ChatOptions options) { }

    public async Task<bool> IsPureDuplicateAsync(string claim1, string claim2) {
        var options = new ChatOptions {
            ResponseFormat = ChatResponseFormat.ForJsonSchema<ClaimMatcher.ClaimMatchResult>(), Temperature = 0.0f,
        };

        ConfigureChatOptions(options);

        options.ResponseFormat = ChatResponseFormat.ForJsonSchema<ClaimMatcher.ClaimMatchResult>();

        var userPrompt = $"Claim A: \"{claim1}\"\nClaim B: \"{claim2}\"";

        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, userPrompt) };

        var response = await ChatClient
            .GetResponseAsync(messages, options)
            .ConfigureAwait(false);

        return JsonSerializer.Deserialize<ClaimMatcher.ClaimMatchResult>(response.Text) is { Match: true };
    }
}

public interface IClaimMatcher {
    Task<bool> IsPureDuplicateAsync(string claim1, string claim2);
}

public static class OpenAIClientExtensions {
    public static void AddOnnxRuntimeClaimMatcher(this IServiceCollection services, Model loadedModel) {
        services.AddSingleton<IClaimMatcher>(sp =>
            new ClaimMatcher(new OnnxRuntimeGenAIChatClient(loadedModel, ownsModel: false)));
    }
    
    public static void AddOpenAIClaimMatcher(this IServiceCollection services, string apiKey, string modelId = "gpt-4o-mini") {
        services.AddSingleton<IClaimMatcher>(sp =>
            new ClaimMatcher(new OpenAIClient(apiKey).GetChatClient(modelId).AsIChatClient()));
    }
}