namespace Tharga.Team;

/// <summary>
/// Who wrote to support through a public form: a person nobody has signed in or verified.
/// </summary>
/// <remarks>
/// <b>Everything here is what the visitor typed.</b> Nothing was checked, and the address in particular is a claim:
/// it is where answers are mailed, never a reason to place the case in a team.
/// <para>
/// <b>Consent is the host's to ask for and the toolkit's to keep.</b> A form that asks for consent to process
/// personal data records here whether it was given, and when, so the case can show it later. The toolkit does not
/// decide what the consent covers.
/// </para>
/// </remarks>
public record ExternalSupportContact
{
    /// <summary>The visitor's name, as they wrote it. Optional: the address names them when it is empty.</summary>
    public string Name { get; init; }

    /// <summary>The address answers are mailed to. Required, and must look like an address.</summary>
    public required string Email { get; init; }

    /// <summary>A telephone number, when the form asks for one.</summary>
    public string Phone { get; init; }

    /// <summary>The organisation the visitor writes on behalf of, when the form asks for one.</summary>
    public string Organisation { get; init; }

    /// <summary>When the visitor gave consent to process their personal data, or <c>null</c> when they did not or the form never asked.</summary>
    public DateTime? ConsentGivenAt { get; init; }

    /// <summary>Anything else the host's form collects, by field name. Bounded; see <see cref="SupportCaseLimits"/>.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}
