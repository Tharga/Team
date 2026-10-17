namespace Tharga.Team;

/// <summary>How a notice reaches a user: by itself as it happens, or listed in one mail a day.</summary>
/// <remarks>Stored by name wherever a choice or a default is kept.</remarks>
public enum UserNoticeDelivery
{
    /// <summary>Mailed as it happens, with others of the same notice that arrive within the burst window.</summary>
    AtOnce,

    /// <summary>Listed in one mail a day, sent at the host's digest time.</summary>
    Digest
}
