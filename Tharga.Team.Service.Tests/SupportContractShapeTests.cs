using System.Collections;
using System.Reflection;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The support contracts and the persistence port must stay wire-shaped and storage-free.
/// </summary>
/// <remarks>
/// <b>These guard two architecture rules that nothing compiled records.</b> Contracts serialize by
/// construction — records, arrays, an explicit cursor, no <c>IAsyncEnumerable</c>, no generic methods, no
/// interface-typed returns. And a port speaks the domain's language: <c>IApiKeyRepository : IRepository</c>,
/// which inherits a <c>Tharga.MongoDB</c> type into a contract, is the shape being avoided.
/// <para>
/// Both are the kind of rule a reviewer agrees with and a later PR breaks anyway, because adding an
/// <c>IAsyncEnumerable</c> overload is locally convenient and looks harmless. A test is what makes it loud.
/// </para>
/// <para>
/// <b>Every scan here self-checks.</b> A reflection scan that matches nothing passes forever while reporting
/// that everything is fine — the same failure mode the dialog-button-order scan shipped with once. So each
/// test asserts it actually found the surface it claims to be checking.
/// </para>
/// </remarks>
public class SupportContractShapeTests
{
    private const string SupportPrefix = "Support";
    private const int KnownSupportContractCount = 7;

    private static Type[] SupportContracts() =>
        [.. typeof(SupportCase).Assembly.GetExportedTypes()
            .Where(t => t.Name.StartsWith(SupportPrefix, StringComparison.Ordinal))
            .OrderBy(t => t.Name)];

    [Fact]
    public void TheScanFindsTheSupportContracts()
    {
        var found = SupportContracts();

        Assert.True(found.Length >= KnownSupportContractCount,
            $"The scan found {found.Length} support contract type(s) but expected at least " +
            $"{KnownSupportContractCount}. Either types were removed, or the scan is looking in the wrong " +
            "place and every other test in this class is passing while checking nothing.");
    }

    /// <summary>
    /// A contract that cannot cross a wire is not a contract. <c>IAsyncEnumerable</c> is the specific
    /// temptation, because it is the natural return type in-process and impossible over HTTP.
    /// </summary>
    [Fact]
    public void NoSupportContract_ExposesAStreamingOrInterfaceTypedMember()
    {
        var offenders = new List<string>();

        foreach (var type in SupportContracts())
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsStreaming(property.PropertyType))
                    offenders.Add($"{type.Name}.{property.Name} returns {property.PropertyType.Name}");

                if (IsDisallowedInterface(property.PropertyType))
                    offenders.Add($"{type.Name}.{property.Name} is interface-typed ({property.PropertyType.Name}); use an array");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ThePort_HasNoGenericMethodsAndNoStreamingReturns()
    {
        var offenders = new List<string>();

        foreach (var method in typeof(ISupportCaseStore).GetMethods())
        {
            if (method.IsGenericMethodDefinition)
                offenders.Add($"{method.Name} is generic");

            var returned = Unwrap(method.ReturnType);

            if (IsStreaming(returned))
                offenders.Add($"{method.Name} returns {returned.Name}");

            if (IsDisallowedInterface(returned))
                offenders.Add($"{method.Name} returns the interface {returned.Name}");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Rule 4: the port speaks the domain's language. Nothing storage-shaped may appear in it — not as a
    /// base interface, not as a parameter, not as a return type.
    /// </summary>
    [Fact]
    public void ThePort_MentionsNoStorageType()
    {
        var port = typeof(ISupportCaseStore);

        Assert.Empty(port.GetInterfaces());

        var mentioned = port.GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(Unwrap(m.ReturnType)))
            .Concat(port.GetMethods().Select(m => m.ReturnType))
            .Distinct();

        var offenders = mentioned
            .Where(t => (t.Namespace ?? string.Empty).StartsWith("Tharga.MongoDB", StringComparison.Ordinal)
                     || (t.Namespace ?? string.Empty).StartsWith("MongoDB", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "The persistence port names a storage type, which is the IApiKeyRepository : IRepository mistake: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// Paging is by explicit cursor, so a page has to be able to say there is another one.
    /// </summary>
    [Theory]
    [InlineData(typeof(SupportCasePage))]
    [InlineData(typeof(SupportMessagePage))]
    public void APage_CarriesItemsAndAnExplicitCursor(Type pageType)
    {
        var items = pageType.GetProperty("Items");
        Assert.NotNull(items);
        Assert.True(items.PropertyType.IsArray, $"{pageType.Name}.Items must be an array.");

        var cursor = pageType.GetProperty("NextCursor");
        Assert.NotNull(cursor);
        Assert.Equal(typeof(string), cursor.PropertyType);
    }

    private static Type Unwrap(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)
            ? type.GetGenericArguments()[0]
            : type;

    private static bool IsStreaming(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>);

    private static bool IsDisallowedInterface(Type type) =>
        type.IsInterface && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
}
