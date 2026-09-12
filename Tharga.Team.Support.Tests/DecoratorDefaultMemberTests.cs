using System.Reflection;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The support package's half of the guard in Tharga/Team#272: every decorator here declares every default
/// member of the contract it decorates.
/// </summary>
/// <remarks>
/// The same assertion runs in <c>Tharga.Team.Service.Tests</c> over the core assemblies. It has to be stated
/// twice because a test can only reflect over assemblies its own project references, and this one is not
/// reachable from there — so a decorator added here would otherwise be guarded by nothing.
/// <para>
/// <c>ISupportCaseService</c> has no default members today, which is why the self-check below asserts the
/// scan found decorators rather than asserting it found violations to look for. The guard is for the member
/// somebody adds later.
/// </para>
/// </remarks>
public class DecoratorDefaultMemberTests
{
    private static readonly Assembly[] ToolkitAssemblies =
    [
        typeof(ITeamService).Assembly,
        typeof(SupportScopes).Assembly
    ];

    public static TheoryData<Type> Decorators()
    {
        var data = new TheoryData<Type>();
        foreach (var type in DecoratorTypes()) data.Add(type);
        return data;
    }

    private static Type[] DecoratorTypes()
        => [.. typeof(SupportScopes).Assembly.GetTypes()
            .Where(x => x.IsClass && !x.IsAbstract && x.Name.EndsWith("Decorator", StringComparison.Ordinal))
            .OrderBy(x => x.FullName, StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Decorators))]
    public void Decorator_DeclaresEveryDefaultMemberOfItsContract(Type decorator)
    {
        var swallowed = new List<string>();

        foreach (var contract in decorator.GetInterfaces().Where(x => ToolkitAssemblies.Contains(x.Assembly)))
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

    /// <summary>A scan that matches nothing passes every theory it feeds while reading as "everything checked".</summary>
    [Fact]
    public void TheScan_FindsTheDecoratorsInThisAssembly()
    {
        Assert.NotEmpty(DecoratorTypes());
        Assert.Contains(typeof(AuthorizationSupportCaseServiceDecorator), DecoratorTypes());
        Assert.Contains(typeof(AuditingSupportCaseServiceDecorator), DecoratorTypes());
    }
}
