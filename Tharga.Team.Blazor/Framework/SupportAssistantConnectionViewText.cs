namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>SupportAssistantConnectionView</c> — what the support assistant answers a team with.</summary>
public static class SupportAssistantConnectionViewText
{
    /// <summary>The section's title.</summary>
    public static readonly TextKey Title = new("team.support.assistant.connection.title", "AI assistant");

    /// <summary>One sentence above the choice.</summary>
    public static readonly TextKey Explanation = new("team.support.assistant.connection.explanation", "What the support assistant answers this team with.");

    /// <summary>Who last changed it and when. <c>{0}</c> is the name, <c>{1}</c> the time.</summary>
    public static readonly TextKey ChangedBy = new("team.support.assistant.connection.changedBy", "Changed by {0}, {1}.");

    /// <summary>The platform's model, chosen by the team or by default.</summary>
    public static readonly TextKey KindPlatform = new("team.support.assistant.connection.kind.platform", "The platform's model");

    /// <summary>The platform's model, named as its client reports it: <c>{0}</c> is the provider and the model, such as "ollama, llama3".</summary>
    public static readonly TextKey KindPlatformNamed = new("team.support.assistant.connection.kind.platformNamed", "The platform's model ({0})");

    /// <summary>The team's own Neurolito account.</summary>
    public static readonly TextKey KindNeurolito = new("team.support.assistant.connection.kind.neurolito", "Neurolito, with the team's own key");

    /// <summary>The team's own Anthropic account.</summary>
    public static readonly TextKey KindAnthropic = new("team.support.assistant.connection.kind.anthropic", "Anthropic, with the team's own key");

    /// <summary>No assistant for the team.</summary>
    public static readonly TextKey KindOff = new("team.support.assistant.connection.kind.off", "Off: no assistant for this team");

    /// <summary>The provider's address.</summary>
    public static readonly TextKey Address = new("team.support.assistant.connection.address", "Address");

    /// <summary>The provider key's field.</summary>
    public static readonly TextKey ApiKey = new("team.support.assistant.connection.apiKey", "Key");

    /// <summary>Under the key field when no key is stored for this provider and address.</summary>
    public static readonly TextKey ApiKeyPlaceholder = new("team.support.assistant.connection.apiKeyPlaceholder", "Paste the key");

    /// <summary>Under the key field when a key is stored and would be kept.</summary>
    public static readonly TextKey ApiKeyStored = new("team.support.assistant.connection.apiKeyStored", "A key is stored. Type a new one to replace it.");

    /// <summary>Lists the models the key may use.</summary>
    public static readonly TextKey LoadModels = new("team.support.assistant.connection.loadModels", "Load models");

    /// <summary>The model picker's label.</summary>
    public static readonly TextKey Model = new("team.support.assistant.connection.model", "Model");

    /// <summary>Sends the model one short message.</summary>
    public static readonly TextKey Test = new("team.support.assistant.connection.test", "Test");

    /// <summary>Above what the model answered a test with.</summary>
    public static readonly TextKey TestReply = new("team.support.assistant.connection.testReply", "The model answered:");

    /// <summary>Saves the choice.</summary>
    public static readonly TextKey Save = new("team.support.assistant.connection.save", "Save");

    /// <summary>After a choice is saved.</summary>
    public static readonly TextKey Saved = new("team.support.assistant.connection.saved", "Saved.");

    /// <summary>When something failed that the service did not explain.</summary>
    public static readonly TextKey Failed = new("team.support.assistant.connection.failed", "That could not be done.");

    /// <summary>When no team is selected or named.</summary>
    public static readonly TextKey SelectTeam = new("team.support.assistant.connection.selectTeam", "Select a team to choose its AI assistant.");

    /// <summary>When the reader may not change the team's assistant.</summary>
    public static readonly TextKey NotPermitted = new("team.support.assistant.connection.notPermitted", "You cannot change this team's AI assistant.");

    /// <summary>Every key the component renders, for building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, Explanation, ChangedBy, KindPlatform, KindPlatformNamed, KindNeurolito, KindAnthropic, KindOff, Address, ApiKey, ApiKeyPlaceholder,
        ApiKeyStored, LoadModels, Model, Test, TestReply, Save, Saved, Failed, SelectTeam, NotPermitted
    ];

    /// <summary>The name of a connection kind.</summary>
    public static TextKey For(SupportAiConnectionKind kind) => kind switch
    {
        SupportAiConnectionKind.Neurolito => KindNeurolito,
        SupportAiConnectionKind.Anthropic => KindAnthropic,
        SupportAiConnectionKind.Off => KindOff,
        _ => KindPlatform
    };
}
