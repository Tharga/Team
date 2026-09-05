using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Every enum persisted by this assembly must be stored by name.
/// </summary>
/// <remarks>
/// The driver's default representation for an enum is <c>Int32</c>, so leaving the attribute off is not a
/// neutral omission — it selects the ordinal, and nothing about the declaration says so. A stored ordinal
/// is correct only while the enum's declaration order never changes; inserting or reordering a member
/// silently re-grades every document already written.
/// <para>
/// This sweeps the assembly rather than naming properties, so an entity added later is covered without
/// anyone remembering to extend a list.
/// </para>
/// </remarks>
public class PersistedEnumRepresentationTests
{
    [Fact]
    public void EveryPersistedEnum_DeclaresStringRepresentation()
    {
        var offenders = PersistedTypes(typeof(TeamEntityBase<>).Assembly)
            .SelectMany(EnumProperties)
            .Where(p => !StoresByName(p))
            .Select(p => $"{p.DeclaringType?.Name}.{p.Name}")
            .OrderBy(x => x)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These persisted enums would be stored as ordinals. Add [BsonRepresentation(BsonType.String)], " +
            "or document on the property why an ordinal is required: " + string.Join(", ", offenders));
    }

    /// <summary>Guards the sweep itself — a filter that silently matches nothing would pass forever.</summary>
    [Fact]
    public void TheSweep_FindsThePersistedEnumsItIsMeantToCheck()
    {
        var found = PersistedTypes(typeof(TeamEntityBase<>).Assembly)
            .SelectMany(EnumProperties)
            .Select(p => $"{p.DeclaringType?.Name}.{p.Name}")
            .ToArray();

        Assert.Contains("TeamEntityBase`1.ConsentAccessLevel", found);
        Assert.Contains("TeamMemberBase.AccessLevel", found);
        Assert.Contains("TeamMemberBase.State", found);
        Assert.Contains("IconEntity.Kind", found);

        // Embedded documents, which the sweep missed entirely until the closure walk replaced the
        // derives-from check. If these drop out, the walk has regressed to roots only.
        Assert.Contains("SupportCaseEntity.Status", found);
        Assert.Contains("SupportMessageEntity.Kind", found);
        Assert.Contains("SupportChannelBindingEntity.ChannelType", found);

        // Nullable, and therefore the shape most likely to slip past a sweep that forgets to unwrap.
        Assert.Contains("SupportMessageEntity.Source", found);
    }

    /// <summary>
    /// Every type whose properties reach the database — the entity roots <b>and the documents embedded in
    /// them</b>.
    /// </summary>
    /// <remarks>
    /// <b>Embedded documents were the hole here, and they are the likeliest place for an ordinal to hide.</b>
    /// The sweep used to match only types deriving from <c>EntityBase</c> or <c>TeamMemberBase</c>, so a
    /// record embedded in one — a message inside a support case, a binding inside that — was persisted,
    /// carried enums, and was checked by nothing. Walking the reachable closure instead means a future
    /// embedded document is covered on the day it is written rather than the day someone remembers.
    /// </remarks>
    private static IEnumerable<Type> PersistedTypes(Assembly assembly)
    {
        var roots = assembly.GetTypes().Where(IsEntityRoot);

        var seen = new HashSet<Type>();
        var pending = new Stack<Type>(roots);

        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seen.Add(type)) continue;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var candidate = Unwrap(property.PropertyType);

                if (candidate.Assembly == assembly && !candidate.IsEnum && !seen.Contains(candidate))
                    pending.Push(candidate);
            }
        }

        return seen;
    }

    private static Type Unwrap(Type type)
    {
        if (type.IsArray) return type.GetElementType()!;

        if (type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return type.GetGenericArguments()[0];

        return Nullable.GetUnderlyingType(type) ?? type;
    }

    private static bool IsEntityRoot(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var definition = current.IsGenericType ? current.GetGenericTypeDefinition() : current;
            if (definition == typeof(EntityBase) || definition == typeof(TeamMemberBase))
                return true;
        }

        return false;
    }

    private static IEnumerable<PropertyInfo> EnumProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType).IsEnum);

    private static bool StoresByName(PropertyInfo property)
        => property.GetCustomAttribute<BsonRepresentationAttribute>()?.Representation == BsonType.String;
}
