namespace Tharga.Team;

/// <summary>
/// The tokens a model reported for one of the assistant's answers, every round of its tool loop added up.
/// </summary>
/// <remarks>
/// As the provider counted them, so they mean what that provider's tokens mean. Kept with the answer as
/// <see cref="SupportMessage.AssistantUsage"/>; absent when the provider reported nothing.
/// </remarks>
public sealed record SupportAssistantUsage
{
    /// <summary>The tokens the model read: the instructions, the conversation and every tool result it was sent.</summary>
    public long InputTokens { get; init; }

    /// <summary>The tokens the model wrote, its calls for tools included.</summary>
    public long OutputTokens { get; init; }

    /// <summary>All of them, as the provider totalled them; input and output added up when it gave no total.</summary>
    public long TotalTokens { get; init; }

    /// <summary>Of <see cref="InputTokens"/>, those the provider read from its cache, which most providers charge less for; 0 when it said none.</summary>
    public long CachedInputTokens { get; init; }

    /// <summary>Who served the answer, as the chat client names its provider, such as "openai" or "ollama"; null when it does not say.</summary>
    public string Provider { get; init; }

    /// <summary>The model that answered, as the provider named it; null when neither the provider nor the client says.</summary>
    public string Model { get; init; }
}
