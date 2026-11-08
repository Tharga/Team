using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Tharga.Team.Analyzers;

namespace Tharga.Team.Analyzers.Tests;

/// <summary>
/// A consuming application that injects an internal-path service into a page, controller or MCP provider is told
/// at build time (Tharga/Team#398). Fixtures compile against the real contracts and the real ASP.NET types, so the
/// "internal-path" rule is read from the real markers rather than from a stand-in that could drift.
/// </summary>
public class UngatedInjectionAnalyzerTests
{
    private static readonly MetadataReference[] References = BuildReferences();

    private static MetadataReference[] BuildReferences()
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!.Split(Path.PathSeparator);
        var paths = trusted.Append(typeof(ITeamService).Assembly.Location).Distinct();
        return [.. paths.Select(p => MetadataReference.CreateFromFile(p))];
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Host",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, "The fixture does not compile: " + string.Join("; ", errors.Select(e => e.ToString())));

        var withAnalyzers = compilation.WithAnalyzers([new UngatedInjectionAnalyzer()]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static string[] Ids(ImmutableArray<Diagnostic> diagnostics) => [.. diagnostics.Select(d => d.Id)];

    private const string Usings = """
        using Microsoft.AspNetCore.Components;
        using Microsoft.AspNetCore.Mvc;
        using Tharga.Team;
        """;

    [Fact]
    public async Task AComponent_InjectingTheInternalService_ThroughAProperty_IsReported()
    {
        var diagnostics = await RunAsync(Usings + """

            public class Page : ComponentBase
            {
                [Inject] private ITeamService TeamService { get; set; }
            }
            """);

        var d = Assert.Single(diagnostics);
        Assert.Equal(UngatedInjectionAnalyzer.DiagnosticId, d.Id);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("ITeamService", d.GetMessage());
        Assert.Contains("ITeamManagementService", d.GetMessage());
    }

    [Fact]
    public async Task AComponent_TakingTheInternalService_OnItsConstructor_IsReported()
    {
        var diagnostics = await RunAsync(Usings + """

            public class Page : ComponentBase
            {
                public Page(ITeamService teamService) { }
            }
            """);

        Assert.Equal([UngatedInjectionAnalyzer.DiagnosticId], Ids(diagnostics));
    }

    [Fact]
    public async Task APrimaryConstructor_IsInspectedToo()
    {
        var diagnostics = await RunAsync(Usings + """

            public class Page(ITeamService teamService) : ComponentBase;
            """);

        Assert.Equal([UngatedInjectionAnalyzer.DiagnosticId], Ids(diagnostics));
    }

    [Fact]
    public async Task AController_IsReported()
    {
        var diagnostics = await RunAsync(Usings + """

            public class TeamsController(ITeamService teamService) : ControllerBase;
            """);

        Assert.Equal([UngatedInjectionAnalyzer.DiagnosticId], Ids(diagnostics));
    }

    [Fact]
    public async Task AnMcpProvider_IsReported()
    {
        // The provider interfaces live in a package this project does not reference; the analyzer keys on the shape
        // of the name (IMcp...Provider), so a stand-in with that name exercises the same path.
        var diagnostics = await RunAsync(Usings + """

            public interface IMcpToolProvider { }
            public class Tools(ITeamService teamService) : IMcpToolProvider;
            """);

        Assert.Equal([UngatedInjectionAnalyzer.DiagnosticId], Ids(diagnostics));
    }

    [Fact]
    public async Task EveryInternalServiceIsCovered_NotOnlyTeamService()
    {
        // No list in the analyzer: the markers decide. Pick another contract that carries one.
        var other = typeof(ITeamService).Assembly.GetTypes()
            .First(t => t.IsInterface && t.IsPublic && t != typeof(ITeamService) && t.Namespace == "Tharga.Team"
                        && t.GetCustomAttributes(typeof(System.ComponentModel.EditorBrowsableAttribute), false).Length == 1
                        && !t.IsGenericTypeDefinition);

        var diagnostics = await RunAsync(Usings + $$"""

            public class Page(Tharga.Team.{{other.Name}} service) : ComponentBase;
            """);

        Assert.Equal([UngatedInjectionAnalyzer.DiagnosticId], Ids(diagnostics));
    }

    [Fact]
    public async Task AComponent_InjectingAGatedService_IsNotReported()
    {
        var diagnostics = await RunAsync(Usings + """

            public class Page : ComponentBase
            {
                [Inject] private ITeamManagementService Teams { get; set; }
                [Inject] private ITeamDirectoryService Directory { get; set; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AHostsOwnImplementationOfTheContract_IsNotReported()
    {
        // Implementing the contract is what it is for; only a surface that consumes it is wrong.
        var diagnostics = await RunAsync(Usings + """

            public class HostTeamService
            {
                public HostTeamService(ITeamService inner) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AnAbstractBaseComponent_IsLeftToItsConcreteTypes()
    {
        var diagnostics = await RunAsync(Usings + """

            public abstract class PageBase : ComponentBase
            {
                protected PageBase(ITeamService teamService) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TheDiagnostic_IsAWarning_OnByDefault_InTheSecurityCategory()
    {
        var rule = Assert.Single(new UngatedInjectionAnalyzer().SupportedDiagnostics);

        Assert.Equal(DiagnosticSeverity.Warning, rule.DefaultSeverity);
        Assert.True(rule.IsEnabledByDefault);
        Assert.Equal("Security", rule.Category);
    }
}
