namespace Tharga.Team.Blazor.Features.Support;

/// <summary>Reads every page of a case listing, so a list never stops at its first page without saying so.</summary>
internal static class SupportCasePaging
{
    public const int PageSize = 100;

    private const int MaxPages = 50;

    public static async Task<SupportCase[]> AllAsync(Func<string, Task<SupportCasePage>> page)
    {
        var all = new List<SupportCase>();
        string cursor = null;

        for (var i = 0; i < MaxPages; i++)
        {
            var current = await page(cursor);
            all.AddRange(current?.Items ?? []);

            cursor = current?.NextCursor;
            if (string.IsNullOrEmpty(cursor)) break;
        }

        return [.. all.DistinctBy(x => x.Id)];
    }
}
