namespace Tharga.Team.Service.Tests;

/// <summary>
/// The undecorated user service is reachable only by the code that has to authenticate before any caller exists.
/// Anything else resolving it reads users past the <c>users:manage</c> gate.
/// </summary>
public class UserServiceKeysArchitectureTests
{
    private static readonly string[] Allowed =
    [
        "Tharga.Team.Service/UserServiceKeys.cs",
        "Tharga.Team.Service/ApiKeyAuthenticationHandler.cs",
        "Tharga.Team.Blazor/Framework/ThargaBlazorRegistration.cs",
    ];

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tharga.Team.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Tharga.Team.sln was not found above the test output.");
    }

    private static string[] SourcesReferencingTheKey()
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar)[0].EndsWith(".Tests"))
            .Where(f => File.ReadAllText(f).Contains(nameof(UserServiceKeys)))
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void OnlyTheAuthenticationHandlerAndTheRegistration_ReferenceTheUndecoratedKey()
    {
        var found = SourcesReferencingTheKey();

        Assert.Equal(Allowed.OrderBy(x => x, StringComparer.Ordinal), found);
    }
}
