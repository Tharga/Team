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
    System
}
