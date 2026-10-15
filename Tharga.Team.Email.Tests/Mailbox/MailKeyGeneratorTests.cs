using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Email.Tests.Mailbox;

public class MailKeyGeneratorTests
{
    [Fact]
    public void AKey_IsLowercaseLettersAndDigits_OfTheAskedLength()
    {
        var key = MailKeyGenerator.New(12);

        Assert.Equal(12, key.Length);
        Assert.Matches("^[a-z0-9]{12}$", key);
    }

    [Fact]
    public void Keys_DoNotRepeat()
    {
        var keys = Enumerable.Range(0, 1000).Select(_ => MailKeyGenerator.New(12)).ToHashSet();

        Assert.Equal(1000, keys.Count);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(65)]
    public void ALengthTooShortToBeUnguessable_OrUnreasonablyLong_IsRefused(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MailKeyGenerator.New(length));
    }
}
