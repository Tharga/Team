namespace Tharga.Team;

/// <summary>
/// Default <see cref="IIconProcessor"/>: returns icon bytes unchanged. Registered by the built-in store
/// so processing is a no-op until a real processor (e.g. the <c>Tharga.Team.Images</c> downsizer) is
/// registered.
/// </summary>
public sealed class NoOpIconProcessor : IIconProcessor
{
    public Task<IconContent> ProcessAsync(byte[] data, string contentType, CancellationToken cancellationToken = default)
        => Task.FromResult(new IconContent(data, contentType));
}
