using System.Reflection;
using System.Runtime.CompilerServices;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Entra;
using Tharga.Team.Images;
using Tharga.Team.Mcp;
using Tharga.Team.MongoDB;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every decorator in the toolkit implements every default member of the interface it decorates.
/// </summary>
/// <remarks>
/// A default interface member is how a contract grows without breaking the hosts that implement it — and
/// exactly how it breaks a decorator silently. A decorator that does not implement the new member compiles,
/// and every call through it runs the interface's own default body instead of reaching the service it wraps.
/// <para>
/// That shipped: <c>ITeamService.GetTeamKeyByInviteKeyAsync</c> defaults to null, neither team-service
/// decorator implemented it, and every short invitation link resolved to nothing in a real host while the
/// store could answer it (Tharga/Team#272). Decorators are discovered by shape rather than listed, so a new
/// one is covered the day it is written.
/// </para>
/// </remarks>
public class DecoratorDefaultMemberTests
{
    private static readonly Assembly[] ToolkitAssemblies =
    [
        typeof(ITeamService).Assembly,
        typeof(AuthorizationTeamServiceDecorator).Assembly,
        typeof(ThargaBlazorOptions).Assembly,
        typeof(TeamServiceRepositoryBase<,>).Assembly,
        typeof(SupportRegistration).Assembly,
        typeof(McpTeamOptions).Assembly,
        typeof(EntraDirectoryRegistration).Assembly,
        typeof(ImageProcessingRegistration).Assembly
    ];

    /// <summary>A class that implements an interface and takes that same interface on its constructor.</summary>
    internal static IEnumerable<(Type Decorator, Type Contract)> Decorations(IEnumerable<Type> types)
        => types
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)
                .Where(p => p.IsInterface && p.IsAssignableFrom(t))
                .Distinct()
                .Select(contract => (t, contract)));

    /// <summary>The default members of <paramref name="contract"/>, and of the interfaces it extends, that <paramref name="decorator"/> leaves to the interface.</summary>
    internal static string[] UnforwardedDefaults(Type decorator, Type contract)
        => contract.GetInterfaces().Append(contract).Distinct()
            .SelectMany(i =>
            {
                var map = decorator.GetInterfaceMap(i);
                return map.InterfaceMethods
                    .Select((method, index) => (Method: method, Target: map.TargetMethods[index]))
                    .Where(x => x.Method is { IsAbstract: false, IsStatic: false })
                    .Where(x => x.Target == null || x.Target.DeclaringType.IsInterface)
                    .Select(x => $"{i.Name}.{x.Method.Name}");
            })
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    public static TheoryData<Type, Type> ToolkitDecorations()
    {
        var data = new TheoryData<Type, Type>();
        foreach (var (decorator, contract) in Decorations(ToolkitAssemblies.SelectMany(a => a.GetTypes()))
                     .OrderBy(x => x.Decorator.FullName, StringComparer.Ordinal))
        {
            data.Add(decorator, contract);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ToolkitDecorations))]
    public void ADecorator_ImplementsEveryDefaultMember(Type decorator, Type contract)
    {
        var unforwarded = UnforwardedDefaults(decorator, contract);

        Assert.True(
            unforwarded.Length == 0,
            $"'{decorator.Name}' decorates '{contract.Name}' but leaves {string.Join(", ", unforwarded)} to the " +
            "interface's default body, so a call through it never reaches the service it wraps. Implement each " +
            "one — forward it to the inner service, with whatever check or audit the decorator applies.");
    }

    /// <summary>A scan that finds nothing passes forever, so the two decorators that caused this have to be in it.</summary>
    [Fact]
    public void TheGuard_FindsTheTeamServiceDecorators()
    {
        var decorations = Decorations(ToolkitAssemblies.SelectMany(a => a.GetTypes())).ToArray();

        Assert.Contains((typeof(AuthorizationTeamServiceDecorator), typeof(ITeamService)), decorations);
        Assert.Contains((typeof(AuditingTeamServiceDecorator), typeof(ITeamService)), decorations);
    }

    [Fact]
    public void TheGuard_CatchesADecoratorThatLeavesADefaultToTheInterface()
    {
        Assert.Equal([$"{nameof(IGreeter)}.{nameof(IGreeter.Farewell)}"], UnforwardedDefaults(typeof(ForgetfulGreeter), typeof(IGreeter)));
    }

    [Fact]
    public void TheGuard_PassesADecoratorThatForwardsEveryMember()
    {
        Assert.Empty(UnforwardedDefaults(typeof(FaithfulGreeter), typeof(IGreeter)));
    }

    [Fact]
    public void TheGuard_RecognisesADecoratorByItsShape()
    {
        var decorations = Decorations([typeof(ForgetfulGreeter), typeof(FaithfulGreeter), typeof(PlainGreeter)]).ToArray();

        Assert.Equal([(typeof(ForgetfulGreeter), typeof(IGreeter)), (typeof(FaithfulGreeter), typeof(IGreeter))], decorations);
    }

    public interface IGreeter
    {
        string Greet();
        string Farewell() => null;
    }

    private sealed class PlainGreeter : IGreeter
    {
        public string Greet() => "hello";
    }

    private sealed class ForgetfulGreeter(IGreeter inner) : IGreeter
    {
        public string Greet() => inner.Greet();
    }

    private sealed class FaithfulGreeter(IGreeter inner) : IGreeter
    {
        public string Greet() => inner.Greet();
        public string Farewell() => inner.Farewell();
    }
}
