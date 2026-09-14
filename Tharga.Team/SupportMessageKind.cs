namespace Tharga.Team;

/// <summary>
/// Who produced a message in a support case.
/// </summary>
/// <remarks>
/// <see cref="System"/> covers entries the toolkit writes itself, such as the note appended when a case is
/// closed. Keeping them in the same history rather than in a side channel is what makes the transcript
/// complete — a closure with no trace of who closed it reads as a gap.
/// </remarks>
public enum SupportMessageKind
{
    User,
    System,

    /// <summary>
    /// An answer written by an assistant rather than by a person.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="System"/> because it is an <i>answer</i>, not a note about the case, and a
    /// reader is entitled to know which they are looking at. Appended rather than inserted, and the entity
    /// stores this enum by name, so no existing transcript re-grades.
    /// </remarks>
    Assistant
}
