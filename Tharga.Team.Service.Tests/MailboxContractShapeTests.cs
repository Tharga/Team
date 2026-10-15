using System.Collections;
using System.Reflection;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The mailbox contracts stay wire-shaped, and each service stays on one side of the team boundary.
/// </summary>
/// <remarks>
/// A team service whose every operation names its team, and a system service whose operations name none, are
/// what let one enforcement point decide each call. A method that drifts across — a team operation without
/// its team, a postmaster operation guarded by a team scope — is the authorization gap these tests exist to
/// make loud.
/// </remarks>
public class MailboxContractShapeTests
{
    private static readonly Type[] Records =
    [
        typeof(TeamMailKey), typeof(TeamMailAddresses), typeof(MailEnvelope), typeof(MailThreadOwner),
        typeof(InboundMailItem), typeof(InboundMailPage), typeof(PostmasterMailSummary), typeof(PostmasterPage),
        typeof(MailContent)
    ];

    private static readonly Type[] Services = [typeof(ITeamMailboxService), typeof(IPostmasterService)];

    private static MethodInfo[] MethodsOf(Type service) => service.GetMethods();

    private static RequireScopeAttribute ScopeOf(MethodInfo method) => method.GetCustomAttribute<RequireScopeAttribute>();

    private static string[] ConstantsOf(Type scopes) =>
        [.. scopes.GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => x.IsLiteral).Select(x => (string)x.GetRawConstantValue())];

    [Fact]
    public void TheScanFindsTheMailboxSurface()
    {
        Assert.True(MethodsOf(typeof(ITeamMailboxService)).Length >= 9);
        Assert.True(MethodsOf(typeof(IPostmasterService)).Length >= 5);
        Assert.All(Records, x => Assert.NotEmpty(x.GetProperties()));
    }

    [Fact]
    public void EveryMailboxOperation_DeclaresItsScope()
    {
        var unscoped = Services.SelectMany(s => MethodsOf(s).Where(m => ScopeOf(m) == null).Select(m => $"{s.Name}.{m.Name}")).ToArray();

        Assert.True(unscoped.Length == 0, string.Join(Environment.NewLine, unscoped));
    }

    [Fact]
    public void EveryTeamMailboxOperation_NamesItsTeamFirst_AndIsGuardedByATeamScope()
    {
        var teamScopes = ConstantsOf(typeof(MailboxScopes));

        Assert.All(MethodsOf(typeof(ITeamMailboxService)), m =>
        {
            var first = m.GetParameters().FirstOrDefault();
            Assert.True(first is { Name: "teamKey" } && first.ParameterType == typeof(string), $"{m.Name} does not take the team first.");
            Assert.Contains(ScopeOf(m).Scope, teamScopes);
        });
    }

    [Fact]
    public void EveryPostmasterOperation_IsGuardedByASystemScope_AndNamesNoTeamFirst()
    {
        var systemScopes = ConstantsOf(typeof(SystemMailboxScopes));

        Assert.All(MethodsOf(typeof(IPostmasterService)), m =>
        {
            Assert.Contains(ScopeOf(m).Scope, systemScopes);
            Assert.False(ScopeOf(m).AllowSystemGrant);
            Assert.NotEqual("teamKey", m.GetParameters().FirstOrDefault()?.Name);
        });
    }

    [Fact]
    public void TheTeamAndSystemScopesNeverOverlap()
    {
        Assert.Empty(ConstantsOf(typeof(MailboxScopes)).Intersect(ConstantsOf(typeof(SystemMailboxScopes))));
    }

    [Theory]
    [InlineData(nameof(IPostmasterService.OpenAsync))]
    [InlineData(nameof(IPostmasterService.GetContentAsync))]
    public void OpeningUnroutedMail_IsAlwaysRecorded(string method)
    {
        Assert.Equal(AuditMode.Access, ScopeOf(typeof(IPostmasterService).GetMethod(method)!).Audit);
    }

    [Theory]
    [InlineData(typeof(ITeamMailboxService), nameof(ITeamMailboxService.EnsureKeyAsync))]
    [InlineData(typeof(ITeamMailboxService), nameof(ITeamMailboxService.RotateKeyAsync))]
    [InlineData(typeof(ITeamMailboxService), nameof(ITeamMailboxService.SetOutcomeAsync))]
    [InlineData(typeof(IPostmasterService), nameof(IPostmasterService.RouteAsync))]
    public void EveryChange_IsRecordedAsAChange(Type service, string method)
    {
        Assert.Equal(AuditMode.Change, ScopeOf(service.GetMethod(method)!).Audit);
    }

    [Fact]
    public void HandlingMail_AndChangingTheAddress_AreSeparateScopes()
    {
        Assert.Equal(MailboxScopes.Handle, ScopeOf(typeof(ITeamMailboxService).GetMethod(nameof(ITeamMailboxService.SetOutcomeAsync))!).Scope);
        Assert.Equal(MailboxScopes.Manage, ScopeOf(typeof(ITeamMailboxService).GetMethod(nameof(ITeamMailboxService.RotateKeyAsync))!).Scope);
    }

    [Fact]
    public void NoMailboxContract_StreamsOrReturnsAnInterface()
    {
        var offenders = new List<string>();

        foreach (var type in Records)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsStreamingOrInterface(property.PropertyType)) offenders.Add($"{type.Name}.{property.Name}");
            }
        }

        foreach (var method in Services.Concat([typeof(IInboundMailContentStore)]).SelectMany(MethodsOf))
        {
            if (method.IsGenericMethodDefinition) offenders.Add($"{method.Name} is generic");
            if (IsStreamingOrInterface(Unwrap(method.ReturnType))) offenders.Add($"{method.Name} returns {method.ReturnType.Name}");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static Type Unwrap(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) ? type.GetGenericArguments()[0] : type;

    private static bool IsStreamingOrInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
        || type.IsInterface && typeof(IEnumerable).IsAssignableFrom(type);
}
