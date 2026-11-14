namespace Tharga.Team.Support.Cases;

/// <summary>What support's mail is recorded under in the mail log.</summary>
internal static class SupportMailLog
{
    /// <summary>The category the log shows support's mail in; it is not a category users choose.</summary>
    public const string Category = "support";

    /// <summary>The key one answer is recorded under, so sending it again updates the row rather than adding another.</summary>
    public static string AnswerKey(string caseId, int sequence) => $"support:{caseId}:{sequence}";

    /// <summary>The key the receipt of a case is recorded under.</summary>
    public static string ReceiptKey(string caseId) => $"support-receipt:{caseId}";
}
