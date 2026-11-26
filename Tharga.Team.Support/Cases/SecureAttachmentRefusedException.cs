namespace Tharga.Team.Support.Cases;

/// <summary>
/// Opening a secure file was refused to a caller who may read the case but not the file.
/// </summary>
/// <remarks>
/// An <see cref="UnauthorizedAccessException"/> like any refusal, so every caller handles it as one. Its own type only so the auditing
/// decorator, which wraps the authorizing one, can record a refused secure file without auditing every read.
/// </remarks>
internal sealed class SecureAttachmentRefusedException(string message) : UnauthorizedAccessException(message);
