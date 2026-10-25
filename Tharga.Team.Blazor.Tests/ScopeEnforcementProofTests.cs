using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every <c>[RequireScope]</c> method on a <see cref="SelfCheckingServices"/> interface refuses a caller who holds
/// no scope.
/// </summary>
/// <remarks>
/// <b>This is what makes the exemption list a proof rather than a promise.</b> Nothing reads the attribute on
/// these interfaces, so each method is called, through the toolkit's own registration, by a signed-in caller who
/// is not a member of the team and holds no system scope. Anything but <see cref="UnauthorizedAccessException"/>
/// fails, including <see cref="ReachedTheStoreException"/>: the call got past every check to the data.
/// <para>
/// A method added to one of these interfaces is covered the day it is written, and fails here until someone writes
/// its check.
/// </para>
/// </remarks>
public class ScopeEnforcementProofTests
{
    private const string TeamKey = "team-the-caller-is-not-in";
    private const string OtherUserKey = "someone-else";
    private static readonly string Identity = $"stranger-{Guid.NewGuid():N}";

    public static TheoryData<Type, string> Methods()
    {
        var data = new TheoryData<Type, string>();
        foreach (var method in AttributedMethods()) data.Add(method.DeclaringType!, method.ToString()!);
        return data;
    }

    private static IEnumerable<MethodInfo> AttributedMethods()
        => SelfCheckingServices.All
            .SelectMany(e => e.Contract.GetMethods())
            .Where(m => m.IsDefined(typeof(RequireScopeAttribute), false));

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task ACallerHoldingNothing_IsRefused(Type contract, string signature)
    {
        var method = contract.GetMethods().Single(m => m.ToString() == signature);
        var scope = method.GetCustomAttribute<RequireScopeAttribute>()!.Scope;
        var checkedBy = SelfCheckingServices.For(contract).CheckedBy.Name;

        using var provider = Provider();
        using var serviceScope = provider.CreateScope();
        var service = serviceScope.ServiceProvider.GetRequiredService(contract);

        var exception = await Record.ExceptionAsync(() => Call(service, method));

        Assert.True(exception is UnauthorizedAccessException,
            $"{contract.Name}.{method.Name} carries [RequireScope(\"{scope}\")], but a caller holding no scope was " +
            (exception == null ? "let through." : $"stopped by {exception.GetType().Name}: {exception.Message}") +
            $" Nothing reads the attribute on this interface: write the check where {checkedBy} does its others.");
    }

    /// <summary>A theory over an empty set passes while checking nothing.</summary>
    [Fact]
    public void TheProof_CoversEveryExemption()
    {
        var contracts = AttributedMethods().Select(m => m.DeclaringType).Distinct().ToArray();

        Assert.All(SelfCheckingServices.All, e => Assert.Contains(e.Contract, contracts));
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, StrangerAuthenticationStateProvider>();
        services.AddThargaTeamBlazor(o =>
        {
            o.SkipAuthStateDecoration = true;
            o.RegisterTeamService<StrangerTeamStore, StrangerUserStore, StubMember>();
        });
        return services.BuildServiceProvider();
    }

    private static async Task Call(object service, MethodInfo method)
    {
        if (method.IsGenericMethodDefinition)
            method = method.MakeGenericMethod(method.GetGenericArguments().Select(_ => typeof(StubMember)).ToArray());

        object result;
        try
        {
            result = method.Invoke(service, method.GetParameters().Select(Argument).ToArray());
        }
        catch (TargetInvocationException e)
        {
            ExceptionDispatchInfo.Capture(e.InnerException!).Throw();
            throw;
        }

        if (result is Task task)
        {
            await task;
            return;
        }

        var sequence = result?.GetType().GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
        if (sequence != null) await FirstOf(result, sequence);
    }

    /// <summary>An async sequence checks nothing until it is pulled, so pull one item.</summary>
    private static async Task FirstOf(object source, Type sequence)
    {
        var enumerator = sequence.GetMethod(nameof(IAsyncEnumerable<object>.GetAsyncEnumerator))!.Invoke(source, [CancellationToken.None])!;
        var enumeratorType = typeof(IAsyncEnumerator<>).MakeGenericType(sequence.GetGenericArguments()[0]);
        await (ValueTask<bool>)enumeratorType.GetMethod(nameof(IAsyncEnumerator<object>.MoveNextAsync))!.Invoke(enumerator, null)!;
    }

    private static object Argument(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;

        if (parameter.Name == "teamKey") return TeamKey;
        if (type == typeof(string)) return OtherUserKey;
        if (type == typeof(CancellationToken)) return CancellationToken.None;
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(IReadOnlyList<>) || definition == typeof(IEnumerable<>)))
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (Nullable.GetUnderlyingType(type) != null) return null;
        if (type.IsValueType) return Activator.CreateInstance(type);
        return type.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(type) : null;
    }

    /// <summary>Thrown by the stores below: the call got past every check to the data.</summary>
    private sealed class ReachedTheStoreException(string operation)
        : Exception($"{operation} reached the store, so nothing before it refused the caller.");

    private sealed class StrangerAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Identity), new Claim(ClaimTypes.Name, "Stranger")], "proof"))));
    }

    private sealed record Stranger(string Key, string Identity, string EMail) : IUser;

    /// <summary>Knows the caller, and nothing else.</summary>
    private sealed class StrangerUserStore(AuthenticationStateProvider authenticationStateProvider) : UserServiceBase(authenticationStateProvider)
    {
        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal)
            => Task.FromResult<IUser>(new Stranger("stranger", Identity, "stranger@example.com"));

        protected override IAsyncEnumerable<IUser> GetAllAsync() => throw new ReachedTheStoreException(nameof(GetAllAsync));
    }

    /// <summary>The caller is a member of no team and in no consented role; every other operation is the data itself.</summary>
    private sealed class StrangerTeamStore(IUserService userService) : TeamServiceBase(userService)
    {
        protected override Task<ITeamMember> GetTeamMembersAsync(string teamKey, string userKey) => Task.FromResult<ITeamMember>(null);
        protected override IAsyncEnumerable<ITeam> GetConsentedTeamsInternalAsync(string[] userRoles) => AsyncEnumerable.Empty<ITeam>();
        protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => AsyncEnumerable.Empty<ITeam>();

        protected override Task<ITeam> GetTeamAsync(string teamKey) => throw new ReachedTheStoreException(nameof(GetTeamAsync));
        protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName) => throw new ReachedTheStoreException(nameof(CreateTeamAsync));
        protected override Task SetTeamNameAsync(string teamKey, string name) => throw new ReachedTheStoreException(nameof(SetTeamNameAsync));
        protected override Task DeleteTeamAsync(string teamKey) => throw new ReachedTheStoreException(nameof(DeleteTeamAsync));
        protected override Task AddTeamMemberAsync(string teamKey, InviteUserModel model) => throw new ReachedTheStoreException(nameof(AddTeamMemberAsync));
        protected override Task RemoveTeamMemberAsync(string teamKey, string userKey) => throw new ReachedTheStoreException(nameof(RemoveTeamMemberAsync));
        protected override Task<ITeam> SetTeamMemberInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept) => throw new ReachedTheStoreException(nameof(SetTeamMemberInvitationResponseAsync));
        protected override Task SetTeamMemberLastSeenAsync(string teamKey, string userKey) => throw new ReachedTheStoreException(nameof(SetTeamMemberLastSeenAsync));
        protected override Task SetTeamMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel) => throw new ReachedTheStoreException(nameof(SetTeamMemberRoleAsync));
        protected override Task SetTeamMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles) => throw new ReachedTheStoreException(nameof(SetTeamMemberTenantRolesAsync));
        protected override Task SetTeamMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides) => throw new ReachedTheStoreException(nameof(SetTeamMemberScopeOverridesAsync));
        protected override Task SetTeamMemberNameAsync(string teamKey, string userKey, string name) => throw new ReachedTheStoreException(nameof(SetTeamMemberNameAsync));
        protected override Task SetTeamConsentInternalAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel) => throw new ReachedTheStoreException(nameof(SetTeamConsentInternalAsync));
        protected override Task SetTeamCustomRolesInternalAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => throw new ReachedTheStoreException(nameof(SetTeamCustomRolesInternalAsync));
    }
}
