namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// What <see cref="SupportCasesView"/> offers the <see cref="SupportCasesExtraTab"/>s rendered inside it, so it knows which names it has.
/// </summary>
internal interface ISupportCasesTabHost
{
    /// <summary>Registers a tab, in the order the tabs are drawn.</summary>
    void Add(string name);

    /// <summary>Forgets a tab that has gone, for instance because the host's condition for it stopped holding.</summary>
    void Remove(string name);
}
