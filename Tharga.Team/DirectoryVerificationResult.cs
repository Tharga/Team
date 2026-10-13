namespace Tharga.Team;

/// <summary>
/// Result of verifying a local user against the external directory.
/// </summary>
/// <param name="Status">The verification outcome.</param>
/// <param name="DirectoryId">
/// The directory id the user resolved to, when found. May differ from the id stored on the local user
/// (e.g. resolved via email fallback for a user created before directory ids were captured) — callers
/// should persist it so the user is linked for subsequent operations.
/// </param>
/// <param name="DirectoryName">
/// The display name the directory holds for the user, when found. Compared with the local name to offer
/// moving a name in either direction; see <see cref="DirectoryNames"/>.
/// </param>
public sealed record DirectoryVerificationResult(DirectoryUserStatus Status, string DirectoryId = null, string DirectoryName = null);
