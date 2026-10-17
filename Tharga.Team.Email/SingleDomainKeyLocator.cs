using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto;

namespace Tharga.Team.Email;

/// <summary>
/// Hands the verifier a key only for the one domain alignment was checked against.
/// </summary>
/// <remarks>
/// <b>This is what ties the alignment decision to the verification.</b> Alignment reads <c>d=</c> with this
/// transport's parser and MimeKit verifies with its own; if the two ever read one header differently, the key
/// asked for here would belong to a domain nobody checked. Refusing every other domain turns that disagreement
/// into a failed verification rather than a verified stranger.
/// </remarks>
internal sealed class SingleDomainKeyLocator(IDkimPublicKeyLocator inner, string alignedDomain) : IDkimPublicKeyLocator
{
    public AsymmetricKeyParameter LocatePublicKey(string methods, string domain, string selector, CancellationToken cancellationToken = default)
        => inner.LocatePublicKey(methods, Require(domain), selector, cancellationToken);

    public Task<AsymmetricKeyParameter> LocatePublicKeyAsync(string methods, string domain, string selector, CancellationToken cancellationToken = default)
        => inner.LocatePublicKeyAsync(methods, Require(domain), selector, cancellationToken);

    private string Require(string asked)
        => string.Equals(asked?.Trim(), alignedDomain, StringComparison.OrdinalIgnoreCase)
            ? asked
            : throw new InvalidOperationException("The signature asked for a key from a domain other than the one it was aligned on.");
}
