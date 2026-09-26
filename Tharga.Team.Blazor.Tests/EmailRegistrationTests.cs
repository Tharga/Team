using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Blazor.Tests;

public class EmailRegistrationTests
{
    [Fact]
    public void AddThargaTeam_WithEmailOptions_RegistersSmtpSender()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.Auth.ValidateConfiguration = false;
            o.Email = new EmailOptions { SmtpHost = "smtp.test.com", FromAddress = "test@test.com" };
        });

        var provider = builder.Services.BuildServiceProvider();
        var sender = provider.GetService<ITeamEmailSender>();

        Assert.NotNull(sender);
        Assert.IsType<SmtpTeamEmailSender>(sender);
    }

    [Fact]
    public void AddThargaTeam_WithCustomEmailService_RegistersCustomSender()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.Auth.ValidateConfiguration = false;
            o.Email = new EmailOptions { SmtpHost = "smtp.test.com" };
            o.AddEmailService<FakeEmailSender>();
        });

        var provider = builder.Services.BuildServiceProvider();
        var sender = provider.GetService<ITeamEmailSender>();

        Assert.NotNull(sender);
        Assert.IsType<FakeEmailSender>(sender);
    }

    /// <summary>Tharga/Team#290: the built-in sender goes through the outbound-mail policy.</summary>
    [Fact]
    public void AddThargaTeam_WithEmailOptions_RegistersTheOutboundMailPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.Auth.ValidateConfiguration = false;
            o.Email = new EmailOptions { SmtpHost = "smtp.test.com", FromAddress = "test@test.com" };
        });

        var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IOutboundMailPolicy>());
    }

    /// <summary>A custom sender can inject the same policy and apply the same rules.</summary>
    [Fact]
    public void AddThargaTeam_WithCustomEmailService_StillRegistersTheOutboundMailPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.Auth.ValidateConfiguration = false;
            o.AddEmailService<FakeEmailSender>();
        });

        var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IOutboundMailPolicy>());
    }

    /// <summary>The policy reads <c>Email:Override</c> from the host's own configuration.</summary>
    [Fact]
    public void AddThargaTeam_ThePolicyReadsTheEmailOverrideSection()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Email:Override:Address"] = "test-inbox@example.com";
        builder.AddThargaTeam(o =>
        {
            o.Auth.ValidateConfiguration = false;
            o.Email = new EmailOptions { SmtpHost = "smtp.test.com", FromAddress = "test@test.com" };
        });

        var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboundMailPolicyOptions>>().Value;

        Assert.Equal("test-inbox@example.com", options.Address);
    }

    [Fact]
    public void AddThargaTeam_WithoutEmail_DoesNotRegisterSender()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.Auth.ValidateConfiguration = false;
        });

        var provider = builder.Services.BuildServiceProvider();
        var sender = provider.GetService<ITeamEmailSender>();

        Assert.Null(sender);
    }

    /// <summary>
    /// The one behaviour that changed layers when the registration moved to <c>AddThargaTeamBlazor</c> for
    /// Tharga/Team#176. The facade used to read <c>options.Blazor.Title</c> for the fallback directly; the
    /// Blazor layer now reads its own <c>Title</c>, which the options forwarder copies from the same place. The
    /// resolved value must be identical, and nothing else asserts that.
    /// </summary>
    [Fact]
    public void AddThargaTeam_FromNameStillFallsBackToTheTitle()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.Auth.ValidateConfiguration = false;
            o.Blazor.Title = "Contoso Docs";
            o.Email = new EmailOptions { SmtpHost = "smtp.test.com", FromAddress = "test@test.com" };
        });

        var provider = builder.Services.BuildServiceProvider();

        Assert.Equal("Contoso Docs", provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value.FromName);
    }

    /// <summary>
    /// A host that configured email on the Blazor section rather than the facade's own option must not be
    /// silently overwritten. The facade forwards its <c>Email</c> only when set, precisely so this works.
    /// </summary>
    [Fact]
    public void AddThargaTeam_HonoursEmailConfiguredOnTheBlazorSection()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.Auth.ValidateConfiguration = false;
            o.Blazor.Email = new EmailOptions { SmtpHost = "smtp.blazor.test", FromAddress = "test@test.com" };
        });

        var provider = builder.Services.BuildServiceProvider();

        Assert.IsType<SmtpTeamEmailSender>(provider.GetService<ITeamEmailSender>());
        Assert.Equal("smtp.blazor.test", provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value.SmtpHost);
    }

    private class FakeEmailSender : ITeamEmailSender
    {
        public Task SendInviteAsync(string recipientEmail, string recipientName, string inviteLink, string teamName)
            => Task.CompletedTask;
    }
}
