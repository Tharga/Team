using System.Reflection;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Copies the Blazor options from the <c>AddThargaTeam</c> facade into the layer that consumes them.
/// </summary>
/// <remarks>
/// <b>Everything, by reflection, rather than a hand-written list.</b> The list is how the bug happened:
/// <c>AddThargaTeam</c> assigned nine named properties, so a tenth added later was accepted from the
/// caller and silently discarded — the host configured a feature, nothing failed, and the feature simply
/// never turned on. That is the same failure Eplicta reported for <c>IconOptions.MaxUploadBytes</c>
/// (Tharga/Team#177), on the same path, and it will keep recurring for as long as forwarding is a list
/// somebody has to remember to extend.
/// <para>
/// Copying by default and naming the exceptions inverts that: a new option works without anyone
/// remembering, and deliberately <i>not</i> forwarding one is a decision written down here.
/// </para>
/// <para>
/// Public settable properties only. The internal registration fields (<c>_teamService</c> and the rest)
/// are not properties and are still assigned explicitly by the caller, as are the icon settings, which
/// come from the facade's own options rather than from its <c>Blazor</c> section.
/// </para>
/// </remarks>
internal static class ThargaBlazorOptionsForwarder
{
    /// <summary>
    /// Properties deliberately not copied, with the reason. Empty today — kept so an exception has a
    /// place to be recorded rather than becoming an unexplained omission in a copy loop.
    /// </summary>
    private static readonly string[] NotForwarded = [];

    /// <summary>Copies every public settable property from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static void Copy(ThargaBlazorOptions from, ThargaBlazorOptions to)
    {
        if (from == null || to == null) return;

        foreach (var property in ForwardableProperties())
        {
            property.SetValue(to, property.GetValue(from));
        }
    }

    /// <summary>
    /// Copies every internal registration field — the types a host registered through <c>AddTextProvider</c>,
    /// <c>AddIconSource</c> and the like, and the menu items it added — from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// <b>These were a hand-written list too</b>, and the same failure followed: <c>_textProvider</c> and <c>_menuItems</c>
    /// were left off it, so <c>o.Blazor.AddTextProvider</c> and <c>o.Blazor.AddMenuItem</c> were accepted and never took
    /// effect. A field set on the source is carried over: a type is assigned when the source has one, a list is added to.
    /// A field of any other kind throws, so teaching this about it is not left to memory.
    /// </remarks>
    public static void CopyRegistrations(ThargaBlazorOptions from, ThargaBlazorOptions to)
    {
        if (from == null || to == null) return;

        foreach (var field in RegistrationFields())
        {
            var value = field.GetValue(from);
            if (value == null) continue;

            if (value is Type)
            {
                field.SetValue(to, value);
            }
            else if (value is System.Collections.IEnumerable items && field.GetValue(to) is System.Collections.IList target)
            {
                foreach (var item in items) target.Add(item);
            }
            else
            {
                throw new InvalidOperationException($"{nameof(ThargaBlazorOptions)}.{field.Name} is of a kind {nameof(ThargaBlazorOptionsForwarder)} cannot forward ({field.FieldType.Name}): teach it, so the option is not silently dropped.");
            }
        }
    }

    /// <summary>The internal registration fields <see cref="CopyRegistrations"/> is responsible for. Public so a test can enumerate them.</summary>
    public static IEnumerable<FieldInfo> RegistrationFields()
        => typeof(ThargaBlazorOptions)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.Name.StartsWith('_'));

    /// <summary>The properties <see cref="Copy"/> is responsible for. Public so a test can enumerate them.</summary>
    public static IEnumerable<PropertyInfo> ForwardableProperties()
        => typeof(ThargaBlazorOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => p.SetMethod?.IsPublic == true)
            .Where(p => !NotForwarded.Contains(p.Name));
}
