namespace Tharga.Team;

/// <summary>
/// Whether an assistant is answering a case, and whether it has handed over.
/// </summary>
/// <remarks>
/// Three states rather than a flag, because "never had one" and "had one until the customer asked for a
/// person" are different facts, and only the second explains why a human is now reading the transcript.
/// <para>
/// Absent on every case raised before assistants existed, which deserializes as <see cref="None"/> — the
/// behaviour those cases already had.
/// </para>
/// </remarks>
public enum SupportAssistantState
{
    /// <summary>No assistant. A person answers.</summary>
    None,

    /// <summary>An assistant answers replies on this case.</summary>
    Active,

    /// <summary>
    /// The customer asked for a person, so the assistant has stopped answering.
    /// </summary>
    /// <remarks>
    /// Terminal on purpose. Handing back to an assistant after somebody has asked for a human would answer a
    /// question they have already declined to have answered that way.
    /// </remarks>
    HandedOff
}
