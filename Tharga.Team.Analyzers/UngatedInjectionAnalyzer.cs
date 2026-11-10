using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tharga.Team.Analyzers;

/// <summary>
/// Reports a component, controller or MCP provider that takes an <b>internal-path service</b> as a dependency
/// (Tharga/Team#398).
/// </summary>
/// <remarks>
/// <b>What an internal-path service is.</b> A contract in the toolkit that is deliberately unchecked: it exists so
/// framework code can read team data while it builds the claims that would authorize the read. A page that injects
/// one, <c>ITeamService</c> for instance, reads team data with no authorization at all, and nothing about the call
/// site looks wrong. <c>[EditorBrowsable(Never)]</c> hides such a contract from completion but does not stop a typed
/// or copy-pasted use.
/// <para>
/// <b>There is no list in this analyzer.</b> A contract is internal-path when it is marked
/// <c>[EditorBrowsable(EditorBrowsableState.Never)]</c>, which is the same rule the toolkit's own architecture tests
/// use (<c>InternalServiceInjectionTests</c>), so marking a new contract enrols it here and in those tests together
/// and the two cannot drift. What is hard-coded is only the advice naming the gated service, which is wording.
/// </para>
/// <para>
/// <b>What is inspected:</b> constructor parameters (primary constructors included) and <c>[Inject]</c> properties,
/// which is what <c>@inject</c> compiles to, on a type that is a Blazor component, an MVC controller or an MCP
/// provider. A host's own implementation of the contract is not a component and is left alone: implementing the
/// contract is what it is for. Reaching the service some other way, by resolving it from a service provider, is
/// not seen.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UngatedInjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "TTEAM001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Do not inject an internal-path service",
        messageFormat: "'{0}' injects '{1}', which is deliberately unchecked and bypasses authorization entirely. {2}",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An internal-path service is marked [EditorBrowsable(Never)]: it exists so framework code can read team data while building the claims that authorize a read. A component, controller or MCP provider that injects it reads team data with no authorization check.",
        helpLinkUri: "https://github.com/Tharga/Team/blob/master/docs/articles/implementation-guide.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || !IsSurface(type)) return;

        foreach (var constructor in type.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
                Check(context, type, parameter.Type, parameter.Locations);
        }

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "InjectAttribute"))
                Check(context, type, property.Type, property.Locations);
        }
    }

    private static void Check(SymbolAnalysisContext context, INamedTypeSymbol surface, ITypeSymbol dependency, ImmutableArray<Location> locations)
    {
        if (!IsInternalPath(dependency)) return;

        var location = locations.FirstOrDefault(l => l.IsInSource) ?? surface.Locations.FirstOrDefault();
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, surface.Name, dependency.Name, Advice(dependency)));
    }

    /// <summary>A Blazor component, an MVC controller or an MCP provider: the places a caller's request is served.</summary>
    private static bool IsSurface(INamedTypeSymbol type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (t.Name == "ControllerBase" && t.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc") return true;
        }

        return type.AllInterfaces.Any(i =>
            (i.Name == "IComponent" && i.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components")
            || (i.Name.StartsWith("IMcp") && i.Name.EndsWith("Provider")));
    }

    /// <summary>The same rule as the repository's architecture tests: an interface marked EditorBrowsable(Never).</summary>
    private static bool IsInternalPath(ITypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Interface) return false;

        return type.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "EditorBrowsableAttribute"
            && a.ConstructorArguments.Length == 1
            && a.ConstructorArguments[0].Value is int state
            && state == 1); // EditorBrowsableState.Never
    }

    private static string Advice(ITypeSymbol dependency)
        => dependency.Name == "ITeamService"
            ? "Inject a gated service instead: ITeamManagementService, ITeamDirectoryService, ITeamLifecycleService, ITeamOversightService or ITeamAccessRequestService, each member of which carries [RequireScope]."
            : "Inject the gated service that wraps it instead, or move the read behind one that does.";
}
