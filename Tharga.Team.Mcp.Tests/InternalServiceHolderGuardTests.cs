using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Tharga.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp.Tests;

/// <summary>
/// Nothing in the MCP surface other than a provider holds an <b>internal service</b>, and nothing fetches one
/// from a service provider except where it is written down here.
/// </summary>
/// <remarks>
/// The sibling of <see cref="InternalServiceInjectionTests"/>, which reads providers' constructors only. A
/// helper class that takes the unchecked contract, or a provider that resolves it with <c>GetService</c>, would
/// pass that guard and bypass authorization exactly as injecting it does. <c>HttpContextMcpContextAccessor</c>
/// resolves <see cref="ITeamService"/> from the request's own scope to hand to <c>TeamContextResolver</c>; the
/// answer it builds is the caller's own context, and the reason is on that class.
/// <para>
/// <b>Do not add an entry to silence a failure.</b>
/// </para>
/// </remarks>
public class InternalServiceHolderGuardTests
{
    private const string ResolvingFile = "HttpContextMcpContextAccessor.cs";

    private static readonly Regex SourceFile = new(@"\.cs$", RegexOptions.Compiled);

    private static Type[] InternalServices()
        => typeof(ITeamService).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.IsPublic)
            .Where(t => t.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    private static Regex ProviderResolution()
    {
        var names = string.Join("|", InternalServices().Select(t => Regex.Escape(t.Name)));
        return new Regex($@"Get\w*Service\s*(<\s*({names})\s*>|\(\s*typeof\(\s*({names})\s*\))");
    }

    private static string[] Held(Type type)
    {
        var internalServices = InternalServices();

        return type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Where(internalServices.Contains)
            .Select(t => t.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static Type[] NonProviders()
        => typeof(TeamResourceProvider).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && !t.IsDefined(typeof(CompilerGeneratedAttribute)))
            .Where(t => !typeof(IMcpResourceProvider).IsAssignableFrom(t) && !typeof(IMcpToolProvider).IsAssignableFrom(t))
            .ToArray();

    private static DirectoryInfo SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Mcp"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir.FullName, "Tharga.Team.Mcp"));
    }

    private static (string File, string Text)[] Sources()
    {
        var root = SourceRoot();
        var separator = Path.DirectorySeparatorChar;

        return [.. root.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => SourceFile.IsMatch(f.Name))
            .Where(f => !f.FullName.Contains($"{separator}obj{separator}") && !f.FullName.Contains($"{separator}bin{separator}"))
            .Select(f => (Path.GetRelativePath(root.FullName, f.FullName).Replace(separator, '/'), File.ReadAllText(f.FullName)))];
    }

    [Fact]
    public void NothingButAProvider_TakesAnInternalService()
    {
        var offenders = NonProviders().Where(t => Held(t).Length > 0).Select(t => t.Name).ToArray();

        Assert.True(
            offenders.Length == 0,
            $"These are not providers, so the provider guard does not read them, and they take an internal service, which is " +
            $"unchecked: {string.Join(", ", offenders)}. Inject a gated facet instead.");
    }

    [Fact]
    public void NothingFetchesAnInternalService_FromAServiceProvider_ExceptTheOneWrittenDown()
    {
        var pattern = ProviderResolution();

        var offenders = Sources()
            .Where(x => pattern.IsMatch(x.Text))
            .Select(x => x.File)
            .Where(file => file != ResolvingFile)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"These resolve an internal service from a service provider, which no constructor scan sees and which bypasses " +
            $"authorization the same way injecting it does: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void TheExemption_StillResolvesAnInternalService()
    {
        Assert.Matches(ProviderResolution(), Sources().Single(x => x.File == ResolvingFile).Text);
    }

    [Fact]
    public void TheGuard_ActuallyCoversSomething()
    {
        Assert.Contains(typeof(ITeamService), InternalServices());
        Assert.NotEmpty(NonProviders());
        Assert.True(Sources().Length > 10, "The source scan is not reaching the project.");
    }

    [Fact]
    public void TheGuard_CatchesAViolation()
    {
        Assert.Equal([nameof(ITeamService)], Held(typeof(TakesTheInternalService)));
        Assert.Empty(Held(typeof(TakesAGatedFacet)));

        var pattern = ProviderResolution();
        Assert.Matches(pattern, "var service = services.GetRequiredService<ITeamService>();");
        Assert.Matches(pattern, "provider.GetService(typeof(ITeamService))");
        Assert.DoesNotMatch(pattern, "services.GetService<ITeamManagementService>()");
    }

    private sealed class TakesTheInternalService
    {
        public TakesTheInternalService(ITeamService teamService) { }
    }

    private sealed class TakesAGatedFacet
    {
        public TakesAGatedFacet(ITeamManagementService teamManagementService) { }
    }
}
