namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>SupportRefusalActions</c> — what a team's administrators can do when the assistant has stopped answering.</summary>
public static class SupportRefusalActionsText
{
    /// <summary>Above the other models, when the account behind the current one has been refused.</summary>
    public static readonly TextKey SwitchModel = new("team.support.refusal.switchModel", "Answer with another model");

    /// <summary>On a model's button. {0} is the model.</summary>
    public static readonly TextKey UseModel = new("team.support.refusal.useModel", "Use {0}");

    /// <summary>Beside a model. {0} is an amount, {1} its unit.</summary>
    public static readonly TextKey CostPerAnswer = new("team.support.refusal.costPerAnswer", "about {0} {1} per answer");

    /// <summary>Beside a model, after its cost. {0} is how many.</summary>
    public static readonly TextKey QuestionsLeft = new("team.support.refusal.questionsLeft", "about {0} questions left");

    /// <summary>The button that opens the connection, where the team gives its own provider and key.</summary>
    public static readonly TextKey UseOwn = new("team.support.refusal.useOwn", "Connect your own subscription");

    /// <summary>Under that button, saying what it does for the team.</summary>
    public static readonly TextKey UseOwnExplanation = new("team.support.refusal.useOwnExplanation", "Questions are then paid for by your own account at the provider, and this limit no longer applies.");

    /// <summary>The title of the dialog holding the connection.</summary>
    public static readonly TextKey ConnectionTitle = new("team.support.refusal.connectionTitle", "The assistant's AI");

    public static readonly TextKey Failed = new("team.support.refusal.failed", "Something went wrong.");

    public static readonly TextKey[] All = [SwitchModel, UseModel, CostPerAnswer, QuestionsLeft, UseOwn, UseOwnExplanation, ConnectionTitle, Failed];
}
