using Microsoft.Extensions.Options;
using SkiaSharp;
using Tharga.Team;

namespace Tharga.Team.Images;

/// <summary>
/// <see cref="IIconProcessor"/> that squares an uploaded raster image and fits it within
/// <see cref="IconOptions.MaxDimension"/>, re-encoding as PNG. The short side is extended with
/// transparent padding — never cropped — so avatar surfaces that reserve a square box no longer
/// letterbox wide or tall sources inconsistently. Images that are already square and within the box, and
/// formats Skia cannot decode (e.g. SVG), pass through unchanged.
/// </summary>
/// <remarks>
/// <b>Content is never upscaled and never cropped.</b> The output side is
/// <c>min(max(width, height), MaxDimension)</c>, so the fit-inside scale is exactly 1 whenever the source
/// already fits — a 100×50 becomes 100×100, not 256×256. Cropping would solve squaring too, and is
/// precisely the failure to avoid: it takes a face out of a portrait photo.
/// <para>
/// The canvas is created with an alpha channel regardless of source format. A JPEG decodes to RGB with no
/// alpha, and padding that with a transparent colour would yield black bars rather than transparency.
/// </para>
/// </remarks>
public sealed class SkiaIconProcessor : IIconProcessor
{
    /// <summary>
    /// Mitchell resampling, which is what a photographic downscale wants: bicubic quality without the
    /// ringing a sharper kernel puts on an icon's edges.
    /// </summary>
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    private readonly IconOptions _options;

    public SkiaIconProcessor(IOptions<IconOptions> options = null)
    {
        _options = options?.Value ?? new IconOptions();
    }

    public Task<IconContent> ProcessAsync(byte[] data, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Process(data, contentType));
    }

    private IconContent Process(byte[] data, string contentType)
    {
        var max = _options.MaxDimension;
        if (max <= 0 || data == null || data.Length == 0)
            return new IconContent(data, contentType);

        using var source = TryDecode(data);
        if (source == null || source.Width == 0 || source.Height == 0)
            return new IconContent(data, contentType);

        // "Within bounds" alone is not enough to skip: a 100x50 fits the box and is still not square.
        if (source.Width == source.Height && source.Width <= max)
            return new IconContent(data, contentType);

        var side = Math.Min(Math.Max(source.Width, source.Height), max);
        var scale = Math.Min((float)side / source.Width, (float)side / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);

        using var decoded = SKImage.FromBitmap(source);
        var target = SKRect.Create((side - width) / 2f, (side - height) / 2f, width, height);
        surface.Canvas.DrawImage(decoded, target, Sampling);

        using var squared = surface.Snapshot();
        using var encoded = squared.Encode(SKEncodedImageFormat.Png, 100);
        return new IconContent(encoded.ToArray(), "image/png");
    }

    /// <summary>
    /// The decoded bitmap, or null for anything that is not a raster image Skia can read — an SVG, or
    /// bytes that are truncated or corrupt.
    /// </summary>
    /// <remarks>
    /// <b>Skia reports undecodable input by throwing, not by returning null.</b> It builds a codec first
    /// and passes the result straight to the decode, so when no codec matches — an SVG, or bytes truncated
    /// before the header completes — what surfaces is an <see cref="ArgumentNullException"/> about a null
    /// <c>codec</c> parameter. Both were verified against SkiaSharp 4.151.1 rather than assumed; the
    /// obvious reading of the API, that it returns null, is wrong and quietly turns every SVG upload into a
    /// failed request.
    /// <para>
    /// The null check is kept regardless, because the underlying <c>Decode(SKCodec)</c> is documented to
    /// return null on a decode that fails after the codec is built. No input was found that reaches it, so
    /// treat it as defensive rather than as a covered path.
    /// </para>
    /// <para>
    /// The catch is deliberately broad. These are untrusted uploaded bytes, and
    /// <see cref="IIconProcessor"/> asks implementations to return the input unchanged when no processing
    /// applies — so a decoder failing on a malformed file must not fail the upload.
    /// </para>
    /// </remarks>
    private static SKBitmap TryDecode(byte[] data)
    {
        try
        {
            return SKBitmap.Decode(data);
        }
        catch
        {
            return null;
        }
    }
}
