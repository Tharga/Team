using System.Reflection;
using Tharga.Team.Support.Email;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Each transport must stay ignorant of teams, users and audit entries, so lifting one into a standalone
/// package is a move rather than a rewrite.
/// </summary>
/// <remarks>
/// Written as a guard rather than a comment because this is the kind of rule that decays quietly: one
/// convenience overload taking an <c>AuditEntry</c> is all it takes, and nothing else would complain.
/// <para>
/// One theory over every transport rather than a file per transport. A copied guard is the version that gets
/// extended for one namespace and not the other, and the one nobody writes at all for the third.
/// </para>
/// <para>
/// Each test carries a self-check. Three guards in this repo have shipped passing while examining nothing —
/// a scan that found no files, an assembly that was never loaded — so a reflection guard that cannot
/// demonstrate it looked at something is not evidence.
/// </para>
/// </remarks>
public class TransportNamespaceIsolationTests
{
    private const string SlackNamespace = "Tharga.Team.Support.Slack";
    private const string EmailNamespace = "Tharga.Team.Support.Email";
    private const string TeamAssemblyPrefix = "Tharga.Team";

    public static TheoryData<string> TransportNamespaces => [SlackNamespace, EmailNamespace];

    private static Type[] TypesIn(string transportNamespace) =>
        typeof(ISlackClient).Assembly.GetTypes()
            .Where(t => t.Namespace == transportNamespace)
            .ToArray();

    /// <summary>The self-check: without this, an empty scan would satisfy every assertion below.</summary>
    [Fact]
    public void TheScanFindsTheTransportTypes()
    {
        var slack = TypesIn(SlackNamespace);
        Assert.Contains(slack, t => t == typeof(SlackClient));
        Assert.Contains(slack, t => t == typeof(ISlackClient));
        Assert.Contains(slack, t => t == typeof(SlackOptions));

        var email = TypesIn(EmailNamespace);
        Assert.Contains(email, t => t == typeof(MailOptions));
        Assert.Contains(email, t => t == typeof(MailServerOptions));
        Assert.Contains(email, t => t == typeof(RecipientFilter));
    }

    [Theory]
    [MemberData(nameof(TransportNamespaces))]
    public void NoTransportTypeExposesATeamType(string transportNamespace)
    {
        var types = TypesIn(transportNamespace);
        Assert.NotEmpty(types);

        var offenders = new List<string>();

        foreach (var type in types)
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var referenced in ReferencedTypes(member))
                {
                    if (IsTeamType(referenced, transportNamespace)) offenders.Add($"{type.Name}.{member.Name} -> {referenced.FullName}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The self-check for the guard above: a type that <i>does</i> expose a Team type must be detected,
    /// otherwise "no offenders" only means the detector never fires.
    /// </summary>
    [Fact]
    public void TheDetector_RecognisesATeamType()
    {
        Assert.True(IsTeamType(typeof(Tharga.Team.Service.Audit.AuditEntry), SlackNamespace));
        Assert.True(IsTeamType(typeof(AccessLevel), SlackNamespace));
        Assert.True(IsTeamType(typeof(SupportChannelType), EmailNamespace));
        Assert.False(IsTeamType(typeof(string), SlackNamespace));
        Assert.False(IsTeamType(typeof(SlackPostResult), SlackNamespace));
        Assert.False(IsTeamType(typeof(MailOptions), EmailNamespace));

        // A transport type is only exempt inside its own namespace. Without this the two would be able to
        // reach into each other, which is the same coupling by a longer route.
        Assert.True(IsTeamType(typeof(MailOptions), SlackNamespace));
    }

    /// <remarks>
    /// Recurses into element and argument types only. <c>GetGenericTypeDefinition()</c> looks like the
    /// natural third case and is a trap: on a definition it returns itself, so the recursion never ends.
    /// It is also unnecessary — <c>List&lt;AuditEntry&gt;</c> and <c>List&lt;&gt;</c> report the same
    /// assembly, so the outer type is already covered by the check below.
    /// </remarks>
    private static bool IsTeamType(Type type, string transportNamespace)
    {
        if (type == null || type.IsGenericParameter) return false;
        if (type.HasElementType) return IsTeamType(type.GetElementType(), transportNamespace);
        if (type.IsGenericType && type.GetGenericArguments().Any(x => IsTeamType(x, transportNamespace))) return true;

        var assembly = type.Assembly.GetName().Name;
        if (assembly == null || !assembly.StartsWith(TeamAssemblyPrefix, StringComparison.Ordinal)) return false;

        // Types in this namespace are the transport itself, not something crossing in.
        return type.Namespace?.StartsWith(transportNamespace, StringComparison.Ordinal) != true;
    }

    private static IEnumerable<Type> ReferencedTypes(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method:
                yield return method.ReturnType;
                foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
                break;
            case ConstructorInfo constructor:
                foreach (var parameter in constructor.GetParameters()) yield return parameter.ParameterType;
                break;
            case PropertyInfo property:
                yield return property.PropertyType;
                break;
            case FieldInfo field:
                yield return field.FieldType;
                break;
        }
    }
}
