namespace Tharga.Team;

/// <summary>Which kind of mail to users a queued mail is, so a user's history can say where each mail came from.</summary>
public enum UserMailKind
{
    /// <summary>A mail server code queued on its own.</summary>
    Mail,

    /// <summary>A message staff wrote to chosen users.</summary>
    Message,

    /// <summary>An automatic notice, or a summary of several.</summary>
    Notice,

    /// <summary>An invitation to join a team.</summary>
    Invitation,

    /// <summary>A message staff wrote to contacts, people who are not users; never part of a user's history.</summary>
    Contact
}
