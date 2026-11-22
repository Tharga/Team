namespace Tharga.Team.Sample.Framework;

/// <summary>
/// Gives the sample's scripted assistant a price, so the toolkit's own allowance has something to count: what each answer costs follows the tokens it used.
/// </summary>
/// <remarks>
/// <b>Demonstration prices, deliberately high</b>, so a handful of questions moves the bar. A script has no real price. A host with a real model keeps its
/// prices in the platform's AI catalogue (<c>AddThargaSupportPlatformAiCatalogue</c>) and registers nothing like this. A team on its own provider is not the
/// platform's to pay for, so it has no price and is never limited, as with the toolkit's own pricing.
/// </remarks>
internal sealed class SampleAssistantPricing(ISupportAiConnectionStore connections) : ISupportAssistantPricing
{
    private static readonly PlatformAiModel Model = new()
    {
        Id = "sample",
        ModelId = SampleChatClient.ModelName,
        DisplayName = "Sample assistant",
        Offered = true,
        PriceInput = 50m,
        PriceCachedInput = 5m,
        PriceOutput = 250m,
        Currency = "USD"
    };

    public async Task<PlatformAiModel> GetAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (teamKey == null) return null;

        var stored = await connections.GetAsync(teamKey, cancellationToken);
        return stored != null && stored.Kind != SupportAiConnectionKind.Platform ? null : Model;
    }
}
