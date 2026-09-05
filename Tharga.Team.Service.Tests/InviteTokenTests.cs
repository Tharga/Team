using System.Reflection;
using System.Security.Cryptography;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The invitation code is a bearer credential, so where it comes from matters more than what it looks like.
/// </summary>
public class InviteTokenTests
{
    [Fact]
    public void Token_IsShort()
    {
        Assert.Equal(InviteToken.Length, InviteToken.New().Length);
    }

    /// <summary>Survives a URL, a mail client and a query string without escaping.</summary>
    [Fact]
    public void Token_UsesTheUrlSafeAlphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var token = InviteToken.New();

            Assert.All(token, c => Assert.True(
                c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_',
                $"'{c}' is not URL-safe, in token '{token}'."));
        }
    }

    [Fact]
    public void Tokens_DoNotRepeat()
    {
        var tokens = Enumerable.Range(0, 10_000).Select(_ => InviteToken.New()).ToHashSet();

        Assert.Equal(10_000, tokens.Count);
    }

    /// <summary>
    /// <b>The test that actually matters, and the reason it reads IL.</b>
    /// </summary>
    /// <remarks>
    /// Length, alphabet and distinctness are all satisfied just as happily by
    /// <c>Guid.NewGuid().ToString("N")[..22]</c> or by <see cref="Random"/> — neither of which is
    /// unpredictable, and both of which would leave this an invitation anyone could guess their way into
    /// while every other test here stayed green. Asserting the *source* is the only assertion that
    /// distinguishes them.
    /// </remarks>
    [Fact]
    public void Token_ComesFromACryptographicSource()
    {
        var method = typeof(InviteToken).GetMethod(nameof(InviteToken.New), BindingFlags.Public | BindingFlags.Static);
        var body = method?.GetMethodBody();

        Assert.NotNull(body);

        var il = body.GetILAsByteArray();
        Assert.NotNull(il);

        var module = method.Module;
        var callsCryptoSource = false;

        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F) continue;

            MethodBase called;
            try
            {
                called = module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (called?.DeclaringType == typeof(RandomNumberGenerator)) callsCryptoSource = true;
        }

        Assert.True(callsCryptoSource,
            $"{nameof(InviteToken)}.{nameof(InviteToken.New)} must draw from {nameof(RandomNumberGenerator)}. " +
            "An invitation code authorizes joining a team, so a merely-unique value such as a GUID or " +
            "System.Random is not an acceptable substitute however similar the output looks.");
    }

    /// <summary>128 bits. Stated as a test so shortening the token becomes a deliberate act.</summary>
    [Fact]
    public void Token_Carries128Bits()
    {
        Assert.Equal(16, InviteToken.ByteCount);
    }
}
