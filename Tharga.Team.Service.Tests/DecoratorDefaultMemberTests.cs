using System.Reflection;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Every decorator declares every default member of the contract it decorates.
/// </summary>
/// <remarks>
/// A default interface member is exactly the kind of addition a decorator swallows without a sound: the
/// contract gains a member, every implementation keeps compiling, and a call through a decorator that does
/// not declare it runs the interface's own body instead of reaching the service underneath. Nothing fails,
/// nothing logs, and the default — <c>null</c>, an empty list — reads as a legitimate answer. That is
/// Tharga/Team#272, where short invitation links resolved to nothing for three releases.
/// <para>
/// This runs over the types rather than the registrations, so a member added to a decorated contract fails
/// here before anyone wires it up.
/// </para>
/// </remarks>
public class DecoratorDefaultMemberTests
{
    private static readonly Assembly[] ToolkitAssemblies =
    [
        typeof(ITeamService).Assembly,
        typeof(AuthorizationTeamServiceDecorator).Assembly
    ];

    public static TheoryData<Type> Decorators()
    {
        var data = new TheoryData<Type>();
        foreach (var type in DecoratorTypes()) data.Add(type);
        return data;
    }

    private static Type[] DecoratorTypes()
        => [.. ToolkitAssemblies
            .SelectMany(x => x.GetTypes())
            .Where(x => x.IsClass && !x.IsAbstract && x.Name.EndsWith("Decorator", StringComparison.Ordinal))
            .OrderBy(x => x.FullName, StringComparer.Ordinal)];

    private static MethodInfo[] DefaultMembers(Type contract)
        => [.. contract.GetMethods().Where(x => !x.IsAbstract)];

    private static bool IsToolkitContract(Type contract)
        => ToolkitAssemblies.Contains(contract.Assembly);

    [Theory]
    [MemberData(nameof(Decorators))]
    public void Decorator_DeclaresEveryDefaultMemberOfItsContract(Type decorator)
    {
        var swallowed = new List<string>();

        foreach (var contract in decorator.GetInterfaces().Where(IsToolkitContract))
        {
            var map = decorator.GetInterfaceMap(contract);
            for (var i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i].IsAbstract) continue;
                if (map.TargetMethods[i].DeclaringType != contract) continue;

                swallowed.Add($"{contract.Name}.{map.InterfaceMethods[i].Name}");
            }
        }

        Assert.True(swallowed.Count == 0,
            $"{decorator.Name} does not declare {string.Join(", ", swallowed)}, so a call through it runs the " +
            "interface default and never reaches the service it decorates. Forward the member.");
    }

    /// <summary>
    /// The scan's own self-check. A discovery query that matches nothing passes every theory it feeds while
    /// reading as "everything checked" — which is the same class of silent pass this test exists to catch.
    /// </summary>
    [Fact]
    public void TheScan_FindsDecoratorsAndDefaultMembersToCheck()
    {
        Assert.NotEmpty(DecoratorTypes());
        Assert.Contains(typeof(AuthorizationTeamServiceDecorator), DecoratorTypes());
        Assert.Contains(typeof(AuditingTeamServiceDecorator), DecoratorTypes());

        Assert.NotEmpty(DefaultMembers(typeof(ITeamService)));
        Assert.Contains(DefaultMembers(typeof(ITeamService)),
            x => x.Name == nameof(ITeamService.GetTeamKeyByInviteKeyAsync));
    }
}
