namespace Tharga.Team;

/// <summary>What to do with a member's private API keys in a team when they stop being a member of it.</summary>
public enum PrivateKeyOwnerRemovedAction
{
    /// <summary>Delete the keys. They cannot be recovered; lifecycle handlers are told, as for any delete.</summary>
    Delete,

    /// <summary>Disable the keys and keep them, e.g. for audit. They stay visible to whoever has oversight.</summary>
    Disable
}
