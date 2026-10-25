namespace Tharga.Team;

/// <summary>
/// Result of <see cref="IUserManagementService.SetUserNameAsync"/>. The local write has always completed
/// when this is returned — a failure there throws instead.
/// </summary>
/// <param name="DirectoryUpdated">True when the name was also written to the external directory.</param>
/// <param name="DirectoryError">
/// When a directory write was attempted and failed, the reason. **The local write is not rolled back.**
/// The two succeed and fail independently, and coupling them would let a directory outage block renaming
/// a user in this application.
/// </param>
/// <remarks>
/// <see cref="DirectoryUpdated"/> false with no <see cref="DirectoryError"/> is the ordinary case: the
/// caller did not ask for the name to be written to the directory. That is not a failure and should not be
/// reported as one. A write that was asked for and cannot be honoured is refused before the rename instead.
/// </remarks>
public sealed record UserNameChangeResult(bool DirectoryUpdated = false, string DirectoryError = null);
