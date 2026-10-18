using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Messages from staff, in memory: two teams and their members on top of everything mail to users needs.</summary>
internal sealed class UserMessageFixture
{
    public const string Alpha = "team-alpha";
    public const string Beta = "team-beta";
    public const string StaffKey = "staff-1";

    public UserMessageFixture(Action<UserMailOptions> configureMail = null)
    {
        Mail = new UserMailFixture(configureMail);
        Users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(_ =>
        {
            var user = Substitute.For<IUser>();
            user.Key.Returns(StaffKey);
            user.Name.Returns("Sam Staff");
            return Task.FromResult(user);
        });

        Directory.Teams.Add(new UserMessageTeam { Key = Alpha, Name = "Alpha" });
        Directory.Teams.Add(new UserMessageTeam { Key = Beta, Name = "Beta" });

        Member(Alpha, "anna", "Anna", AccessLevel.Owner);
        Member(Alpha, "bo", "Bo", AccessLevel.Administrator, "Editor");
        Member(Alpha, "cia", "Cia", AccessLevel.User);
        Member(Beta, "anna", "Anna", AccessLevel.User, "Editor");
        Member(Beta, "dan", "Dan", AccessLevel.Owner);
        Member(Beta, "eva", "Eva", AccessLevel.Viewer, "Auditor");
    }

    public UserMailFixture Mail { get; }
    public UserMessageOptions Options { get; } = new() { StaffMessageCategories = [UserMailCategories.ServiceKey, UserMailFixture.News, UserMailFixture.Digest] };
    public InMemoryMessageMemberDirectory Directory { get; } = new();
    public IUserMessageMemberDirectory Members { get; set; }
    public InMemoryUserMessageLog Log { get; } = new();
    public IUserService Users { get; } = Substitute.For<IUserService>();
    public ClaimsPrincipal Principal { get; set; } = Staff();

    public UserMessageService Service => new(
        Mail.Categories,
        Microsoft.Extensions.Options.Options.Create(Options),
        Mail.Wrapped,
        Members ?? Directory,
        Mail.Store,
        Mail.Queue,
        Log,
        Mail.Recipients,
        new TeamAuthorizer(new PrincipalAccessor(this)),
        Mail.Time,
        Users,
        new CompositeAuditLogger([Mail.Audit], Microsoft.Extensions.Options.Options.Create(new AuditOptions())),
        new AuditEntryFactory(null));

    public static ClaimsPrincipal Staff(params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam Staff"), new Claim(ClaimTypes.NameIdentifier, "sam"), .. extra], "Test"));

    public void Member(string teamKey, string userKey, string name, AccessLevel? level, params string[] roles)
    {
        Directory.Members.Add(new UserMessageMember { TeamKey = teamKey, UserKey = userKey, Name = name, EMail = $"{userKey}@example.org", AccessLevel = level, Roles = roles });
        Mail.Recipients.Add(new UserMailRecipient { Key = userKey, EMail = $"{userKey}@example.org", Name = name });
    }

    public static UserMessage Message(UserMessageRecipients recipients, string category = UserMailFixture.News, UserMailAttachment[] attachments = null) => new()
    {
        Recipients = recipients,
        Category = category,
        Subject = "Planned maintenance",
        BodyHtml = "<p>We will be <b>down</b> on Sunday.</p>",
        Attachments = attachments ?? []
    };

    private sealed class PrincipalAccessor(UserMessageFixture fixture) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(fixture.Principal);
    }
}

internal sealed class InMemoryMessageMemberDirectory : IUserMessageMemberDirectory
{
    public List<UserMessageTeam> Teams { get; } = [];
    public List<UserMessageMember> Members { get; } = [];

    public Task<UserMessageTeam[]> GetTeamsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Teams.ToArray());

    public Task<UserMessageMember[]> GetActiveMembersAsync(string[] teamKeys, CancellationToken cancellationToken = default)
        => Task.FromResult(Members.Where(x => teamKeys.Contains(x.TeamKey)).ToArray());

    public Task<UserMessageMember[]> GetActiveMembersOfEveryTeamAsync(CancellationToken cancellationToken = default) => Task.FromResult(Members.ToArray());
}

internal sealed class InMemoryUserMessageLog : IUserMessageLog
{
    public List<UserMessageLogEntry> Entries { get; } = [];

    public Exception FailWith { get; set; }

    public Task AddAsync(UserMessageLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (FailWith != null) throw FailWith;

        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<UserMessageLogEntry> GetAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(Entries.FirstOrDefault(x => x.Id == id));

    public Task<UserMessageLogPage> ListAsync(string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var ordered = Entries.OrderByDescending(x => x.SentAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        var start = cursor == null ? 0 : ordered.FindIndex(x => x.Id == cursor) + 1;
        var page = ordered.Skip(start).Take(pageSize).ToArray();

        return Task.FromResult(new UserMessageLogPage { Items = page, NextCursor = start + pageSize < ordered.Count ? page[^1].Id : null });
    }
}
