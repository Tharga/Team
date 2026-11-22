using System.Collections.Concurrent;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Remembers which refusal each team has already been announced for, so a team that keeps asking is announced once, not once per question.
/// </summary>
/// <remarks>In memory and per process, like <see cref="SupportProviderRefusals"/>: a restart may announce a spell once more, which costs a repeated message and nothing else.</remarks>
internal sealed class SupportRefusalAnnouncements
{
    private readonly ConcurrentDictionary<string, string> _announced = new();

    /// <summary>True when the team was not already announced for this spell, which is then recorded.</summary>
    public bool IsNew(string teamKey, string spell)
    {
        if (string.IsNullOrEmpty(teamKey)) return true;

        var already = false;
        _announced.AddOrUpdate(teamKey, spell, (_, previous) =>
        {
            already = previous == spell;
            return spell;
        });

        return !already;
    }

    public void Clear(string teamKey)
    {
        if (!string.IsNullOrEmpty(teamKey)) _announced.TryRemove(teamKey, out _);
    }
}
