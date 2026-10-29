using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Email.Mailbox;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every <c>[RequireScope]</c> the toolkit declares is enforced: the registration puts <c>ScopeProxy</c> in front
/// of callers, or the interface is one of the <see cref="SelfCheckingServices"/>.
/// </summary>
/// <remarks>
/// <b>The attribute does nothing by itself.</b> <c>ScopeProxy</c> reads it, and only for an interface registered
/// through <c>AddTeamService</c> or <c>AddSystemService</c>. On an interface registered any other way it is a
/// comment that looks like a check, which is how <c>team:read</c> sat on <see cref="ITeamManagementService"/>
/// while nothing enforced it (Plan 01 §3b).
/// <para>
/// <b>Registrations are run, not resolved.</b> Resolving for real would need every module's stores, and the guard
/// would then fail for reasons that have nothing to do with it. Instead each interface's last registration runs
/// against <see cref="StandInProvider"/>, which answers every dependency with a stand-in, and the guard looks at
/// the type that comes back: what the toolkit puts in front of the caller.
/// </para>
/// </remarks>
public class ScopeEnforcementRegistrationTests
{
    private const string MailCategory = "news";

    internal static Type[] ScopeBearingInterfaces()
        => ShippedAssemblies.All()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsInterface && t.IsPublic)
            .Where(t => t.GetMethods().Any(m => m.IsDefined(typeof(RequireScopeAttribute), false)))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

    public static TheoryData<Type> Interfaces() => new(ScopeBearingInterfaces());

    /// <summary>Every module that registers a scope-bearing interface, configured as little as each allows.</summary>
    private static ServiceCollection Composition()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<StubTeamService, StubUserService, StubMember>());
        services.AddThargaApiKeys();
        services.AddThargaAuditLogging();
        services.AddThargaUserMail(o =>
        {
            o.BaseUrl = "https://app.example.com";
            o.Mail.FromAddress = "hello@example.com";
            o.AddCategory(new UserMailCategory { Key = MailCategory, Name = "News" });
        });
        services.AddThargaUserMessages(o => o.StaffMessageCategories = [MailCategory]);
        services.AddThargaUserNotices();
        services.AddThargaContactMessages(o => o.AddCategory(new ContactMailCategory { Key = "offers", Name = "Offers" }));
        services.AddThargaTeamMailbox(o => o.Domain = "example.net");
        return services;
    }

    private static Type Outermost(IServiceCollection services, Type contract)
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == contract && !d.IsKeyedService);
        if (descriptor == null) return null;
        if (descriptor.ImplementationType != null) return descriptor.ImplementationType;
        if (descriptor.ImplementationInstance != null) return descriptor.ImplementationInstance.GetType();
        return descriptor.ImplementationFactory!(new StandInProvider()).GetType();
    }

    [Theory]
    [MemberData(nameof(Interfaces))]
    public void EveryScopeBearingInterface_IsEnforced(Type contract)
    {
        var outermost = Outermost(Composition(), contract);

        Assert.True(outermost != null,
            $"No module in this guard's composition registers '{contract.Name}'. Add the module that does to {nameof(Composition)}.");

        var exemption = SelfCheckingServices.For(contract);
        if (exemption != null)
        {
            Assert.True(Is(outermost, exemption.CheckedBy),
                $"'{contract.Name}' is listed as checked by {exemption.CheckedBy.Name}, but the registration puts {outermost.Name} in front of callers. " +
                "Correct the entry, or remove it if the interface is now behind the proxy.");
            return;
        }

        Assert.True(typeof(ScopeProxy<>).MakeGenericType(contract).IsAssignableFrom(outermost),
            $"'{contract.Name}' carries [RequireScope], but the registration puts {outermost.Name} in front of callers, so nothing reads the attribute. " +
            $"Register it with AddTeamService or AddSystemService. If it checks access itself, list it in {nameof(SelfCheckingServices)} and prove it in {nameof(ScopeEnforcementProofTests)}.");
    }

    [Fact]
    public void EveryExemption_IsAScopeBearingInterface()
    {
        var stale = SelfCheckingServices.All.Select(e => e.Contract).Except(ScopeBearingInterfaces()).Select(t => t.Name).ToArray();

        Assert.True(stale.Length == 0, $"Listed in {nameof(SelfCheckingServices)} but carrying no [RequireScope]: {string.Join(", ", stale)}. Remove them.");
    }

    /// <summary>
    /// A theory over an empty set passes while checking nothing, so discovery is pinned to interfaces known to
    /// carry the attribute, behind the proxy and not.
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyCoversSomething()
    {
        var found = ScopeBearingInterfaces();

        Assert.Contains(typeof(IApiKeyManagementService), found);
        Assert.Contains(typeof(IAuditReadService), found);
        Assert.All(SelfCheckingServices.All, e => Assert.Contains(e.Contract, found));
    }

    [Fact]
    public void EveryShippedPackage_IsSearched()
    {
        var names = ShippedAssemblies.Names();

        Assert.Contains("Tharga.Team", names);
        Assert.Contains("Tharga.Team.Support.Anthropic", names);
        Assert.Contains("Tharga.Team.Support.OpenAI", names);
        Assert.DoesNotContain("Tharga.Team.Sample", names);
        Assert.DoesNotContain(names, n => n.EndsWith(".Tests", StringComparison.Ordinal));
        Assert.Equal(names.Length, ShippedAssemblies.All().Length);
    }

    private static bool Is(Type type, Type expected)
        => expected.IsGenericTypeDefinition
            ? type.IsGenericType && type.GetGenericTypeDefinition() == expected
            : expected.IsAssignableFrom(type);

    /// <summary>Answers every request with a stand-in, so a registration's factory runs without the graph behind it.</summary>
    /// <remarks>
    /// Nothing is called on what it hands out. Options are real, since factories read their values while building;
    /// interfaces and abstract types are mocks; anything else is an uninitialized instance of the requested type.
    /// </remarks>
    private sealed class StandInProvider : IServiceProvider, IKeyedServiceProvider
    {
        public object GetService(Type serviceType) => StandIn(serviceType);
        public object GetKeyedService(Type serviceType, object serviceKey) => StandIn(serviceType);
        public object GetRequiredKeyedService(Type serviceType, object serviceKey) => StandIn(serviceType);

        private object StandIn(Type type)
        {
            if (type == typeof(IServiceProvider) || type == typeof(IKeyedServiceProvider)) return this;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptions<>)) return RealOptions(type.GetGenericArguments()[0]);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
            if (type.IsValueType) return Activator.CreateInstance(type);
            if (type == typeof(string)) return string.Empty;
            if (type.IsInterface || type.IsAbstract || typeof(Delegate).IsAssignableFrom(type))
                return ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!).Object;
            return RuntimeHelpers.GetUninitializedObject(type);
        }

        private static object RealOptions(Type optionsType)
            => typeof(Options).GetMethod(nameof(Options.Create))!.MakeGenericMethod(optionsType).Invoke(null, [Activator.CreateInstance(optionsType)]);
    }
}
