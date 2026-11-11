using MimeKit.Cryptography;

namespace Tharga.Team.Email;

/// <summary>
/// Hands the verifier a key only for the one domain alignment was checked against.
/// </summary>
/// <remarks>
/// <b>This is what ties the alignment decision to the verification.</b> Alignment reads <c>d=</c> and <c>s=</c>
/// with this transport's parser and MimeKit verifies with its own; if the two ever read one header differently,
/// the key asked for here would belong to a domain nobody checked. Answering only the one name,
/// <c>{selector}._domainkey.{domain}</c>, that this transport read turns that disagreement into a failed
/// verification rather than a verified stranger.
/// </remarks>
internal sealed class SingleDomainKeyLocator(IDnsResolver inner, DkimSignature aligned) : IDnsResolver
{
    private readonly string _name = aligned.Selector == null ? null : $"{aligned.Selector}._domainkey.{aligned.Domain}";

    public DnsTxtResponse QueryTxt(string domain, CancellationToken cancellationToken = default)
        => IsAligned(domain) ? inner.QueryTxt(domain, cancellationToken) : Refused();

    public Task<DnsTxtResponse> QueryTxtAsync(string domain, CancellationToken cancellationToken = default)
        => IsAligned(domain) ? inner.QueryTxtAsync(domain, cancellationToken) : Task.FromResult(Refused());

    private bool IsAligned(string asked)
        => _name != null && string.Equals(asked?.Trim().TrimEnd('.'), _name, StringComparison.OrdinalIgnoreCase);

    private static DnsTxtResponse Refused() => new(DnsQueryStatus.NonExistentDomain);
}
