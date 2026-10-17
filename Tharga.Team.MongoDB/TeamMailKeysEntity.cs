using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Every mail key a team has held, in one document.
/// </summary>
/// <remarks>
/// <b>One document per team, so a rotation is one write.</b> Rotating touches three keys; a document of its
/// own per key would need a transaction to keep them consistent, and a replica set to have one. Here the
/// whole change is a single conditional update on <see cref="Revision"/>, which two instances cannot interleave.
/// </remarks>
public record TeamMailKeysEntity : EntityBase
{
    public required string TeamKey { get; init; }

    public required TeamMailKeyEntry[] Keys { get; init; }

    /// <summary>Raised by every change, and required unchanged by the next, so concurrent rotations cannot both apply.</summary>
    public int Revision { get; init; }
}
