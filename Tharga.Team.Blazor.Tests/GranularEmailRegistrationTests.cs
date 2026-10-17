using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The email sender on the <b>granular</b> path — <c>AddThargaTeamBlazor</c> — which previously could not
/// register it at all (Tharga/Team#176). The three-way choice is: a custom sender wins, then SMTP if
/// <c>Email</c> is set, then nothing.
/// </summary>
/// <remarks>
/// <b>This failed more quietly than its sibling #157.</b> <c>InviteUserDialog</c> and <c>TeamComponent</c>
/// both resolve the sender with <c>GetService</c> and degrade to manual link copying, so a granular host got
/// no error — invitations were simply never sent and the fallback looked like intended behaviour. That is why
/// <see cref="NeitherConfigured_RegistersNothing"/> matters as much as the positive cases: "no sender" has to
/// stay distinguishable from "wiring forgotten", and the only way it can be is if nothing is registered when
/// nothing was asked for.
/// </remarks>
public class GranularEmailRegistrationTests
{
    private sealed class FakeEmailSender : ITeamEmailSender
    {
        public Task SendInviteAsync(TeamInviteMail mail)
            => Task.CompletedTask;
    }

    private static ServiceProvider Build(Action<ThargaBlazorOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddThargaTeamBlazor(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ACustomSender_IsRegistered()
    {
        using var provider = Build(o => o.AddEmailService<FakeEmailSender>());

        Assert.IsType<FakeEmailSender>(provider.GetService<ITeamEmailSender>());
    }

    [Fact]
    public void SmtpOptions_AloneRegisterNoSender_TheSenderComesFromTharga_Team_Email()
    {
        using var provider = Build(o => o.Email = new EmailOptions { SmtpHost = "smtp.test.com", FromAddress = "a@test.com" });

        Assert.Null(provider.GetService<ITeamEmailSender>());
    }

    /// <summary>
    /// Null must stay null. A granular host that configured no email has to be indistinguishable from the
    /// documented "email disabled" state, or the fallback to manual link copying stops meaning anything.
    /// </summary>
    [Fact]
    public void NeitherConfigured_RegistersNothing()
    {
        using var provider = Build(_ => { });

        Assert.Null(provider.GetService<ITeamEmailSender>());
    }

    [Fact]
    public void ACustomSender_WinsOverSmtpOptions()
    {
        using var provider = Build(o =>
        {
            o.Email = new EmailOptions { SmtpHost = "smtp.test.com" };
            o.AddEmailService<FakeEmailSender>();
        });

        Assert.IsType<FakeEmailSender>(provider.GetService<ITeamEmailSender>());
    }

    /// <summary>
    /// The one property with a fallback: an unconfigured sender name becomes the application title, so
    /// invitations are never sent from a blank name.
    /// </summary>
    [Fact]
    public void FromName_FallsBackToTheTitle()
    {
        using var provider = Build(o =>
        {
            o.Title = "Contoso Docs";
            o.Email = new EmailOptions { SmtpHost = "smtp.test.com", FromAddress = "a@test.com" };
        });

        Assert.Equal("Contoso Docs", provider.GetRequiredService<IOptions<EmailOptions>>().Value.FromName);
    }

    [Fact]
    public void AnExplicitFromName_IsKept()
    {
        using var provider = Build(o =>
        {
            o.Title = "Contoso Docs";
            o.Email = new EmailOptions { SmtpHost = "smtp.test.com", FromName = "Invitations" };
        });

        Assert.Equal("Invitations", provider.GetRequiredService<IOptions<EmailOptions>>().Value.FromName);
    }

    /// <summary>
    /// Drives itself from <see cref="EmailOptions"/>'s own shape, so a property added later is forwarded
    /// without anyone remembering. The registration copies the whole instance for exactly this reason: a
    /// named list is what dropped two <c>IconOptions</c> properties on this same path (Tharga/Team#177), and
    /// this block previously assigned seven properties by hand.
    /// </summary>
    [Fact]
    public void EveryProperty_ReachesTheContainer()
    {
        var properties = OptionsForwarder.ForwardableProperties<EmailOptions>().ToArray();
        Assert.True(properties.Length >= 7, $"Expected at least the seven known EmailOptions properties, found {properties.Length}.");

        var expected = new Dictionary<string, object>();
        var configured = new EmailOptions();
        foreach (var property in properties)
        {
            var value = DistinctValueFor(property.PropertyType, property.Name);
            property.SetValue(configured, value);
            expected[property.Name] = value;
        }

        using var provider = Build(o => o.Email = configured);
        var resolved = provider.GetRequiredService<IOptions<EmailOptions>>().Value;

        foreach (var property in properties)
        {
            Assert.Equal(expected[property.Name], property.GetValue(resolved));
        }
    }

    private static object DistinctValueFor(Type type, string propertyName)
    {
        if (type == typeof(string)) return $"forwarded-{propertyName}";
        if (type == typeof(int)) return 2525;
        if (type == typeof(bool)) return false;

        throw new NotSupportedException(
            $"EmailOptions.{propertyName} is a '{type.Name}', which this test does not know how to set. Teach " +
            "it how, rather than excluding the property — the point of this test is that a new property cannot " +
            "silently stop being forwarded.");
    }
}
