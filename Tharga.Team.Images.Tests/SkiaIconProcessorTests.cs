using Microsoft.Extensions.Options;
using SkiaSharp;
using Tharga.Team;
using Tharga.Team.Images;

namespace Tharga.Team.Images.Tests;

/// <summary>
/// <see cref="SkiaIconProcessor"/>: squares uploaded raster images and downscales oversized ones to fit the
/// max dimension (aspect preserved, transparent padding, PNG output), leaves square within-bounds images and
/// non-decodable data untouched, and respects a disabled (0) max dimension.
/// </summary>
/// <remarks>
/// Ported from <c>ImageSharpIconProcessorTests</c> unchanged in intent when the package moved off
/// <c>SixLabors.ImageSharp</c>. The assertions are deliberately the same ones: this was a dependency swap,
/// so a behaviour difference here is a defect rather than an improvement.
/// </remarks>
public class SkiaIconProcessorTests
{
    private static SkiaIconProcessor Build(int maxDimension = 256)
        => new(Options.Create(new IconOptions { MaxDimension = maxDimension }));

    private static byte[] PngOf(int width, int height)
        => Encode(width, height, SKColors.Empty, SKEncodedImageFormat.Png);

    /// <summary>Fully opaque, so padded pixels are distinguishable from original ones by alpha alone.</summary>
    private static byte[] OpaquePngOf(int width, int height)
        => Encode(width, height, new SKColor(10, 20, 30, 255), SKEncodedImageFormat.Png);

    /// <summary>
    /// A JPEG has no alpha channel at all, which is the case the transparent padding has to survive.
    /// </summary>
    private static byte[] OpaqueJpegOf(int width, int height)
        => Encode(width, height, new SKColor(10, 20, 30, 255), SKEncodedImageFormat.Jpeg);

    private static byte[] Encode(int width, int height, SKColor fill, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(fill);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 100);
        return encoded.ToArray();
    }

    private static SKBitmap Decode(byte[] data)
    {
        var bitmap = SKBitmap.Decode(data);
        Assert.NotNull(bitmap);
        return bitmap;
    }

    /// <summary>
    /// Downscaled to fit, then padded to square. Before squaring shipped this returned 256x128, and
    /// every avatar surface letterboxed it differently.
    /// </summary>
    [Fact]
    public async Task Oversized_IsDownscaledThenPaddedToSquare()
    {
        var data = PngOf(1000, 500);
        var result = await Build(256).ProcessAsync(data, "image/png");

        using var image = Decode(result.Data);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
    }

    /// <summary>
    /// Within bounds is no longer sufficient to skip — it has to be square too. Squares to the long side
    /// (100), not to MaxDimension (256), so no pixel is invented.
    /// </summary>
    [Fact]
    public async Task WithinBoundsButNotSquare_IsPaddedWithoutUpscaling()
    {
        var data = PngOf(100, 50);
        var result = await Build(256).ProcessAsync(data, "image/png");

        using var image = Decode(result.Data);
        Assert.Equal(100, image.Width);
        Assert.Equal(100, image.Height);
    }

    /// <summary>Tall sources take the same path as wide ones.</summary>
    [Fact]
    public async Task TallWithinBounds_IsPaddedToTheLongSide()
    {
        var data = PngOf(50, 100);
        var result = await Build(256).ProcessAsync(data, "image/png");

        using var image = Decode(result.Data);
        Assert.Equal(100, image.Width);
        Assert.Equal(100, image.Height);
    }

    /// <summary>Square and within bounds is the one case that still passes through untouched.</summary>
    [Fact]
    public async Task SquareAndWithinBounds_IsUnchanged()
    {
        var data = PngOf(100, 100);
        var result = await Build(256).ProcessAsync(data, "image/png");
        Assert.Same(data, result.Data);
    }

    /// <summary>Square but oversized still downscales — squaring did not make it a no-op.</summary>
    [Fact]
    public async Task SquareButOversized_IsDownscaled()
    {
        var data = PngOf(300, 300);
        var result = await Build(256).ProcessAsync(data, "image/png");

        using var image = Decode(result.Data);
        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
    }

    /// <summary>
    /// The padding is transparent, not black. This is why the canvas carries an alpha channel regardless of
    /// source format.
    /// </summary>
    [Fact]
    public async Task Padding_IsTransparent()
    {
        var data = OpaquePngOf(100, 50);
        var result = await Build(256).ProcessAsync(data, "image/png");

        using var image = Decode(result.Data);
        Assert.Equal(0, image.GetPixel(50, 5).Alpha);    // padded band at the top
        Assert.Equal(0, image.GetPixel(50, 95).Alpha);   // padded band at the bottom
        Assert.Equal(255, image.GetPixel(50, 50).Alpha); // original content in the middle
    }

    /// <summary>
    /// <b>The alpha-less source case, which had no test before the move to Skia.</b> A JPEG decodes to RGB
    /// with no alpha channel; padding it on a canvas without one produces black bars rather than
    /// transparency — the exact failure the XML docs cite as the reason the canvas is created with alpha,
    /// and precisely the kind of thing swapping the decoder could have regressed silently.
    /// </summary>
    [Fact]
    public async Task Padding_IsTransparent_ForAnAlphaLessSource()
    {
        var data = OpaqueJpegOf(100, 50);
        var result = await Build(256).ProcessAsync(data, "image/jpeg");

        Assert.Equal("image/png", result.ContentType);

        using var image = Decode(result.Data);
        Assert.Equal(0, image.GetPixel(50, 5).Alpha);
        Assert.Equal(0, image.GetPixel(50, 95).Alpha);
        Assert.Equal(255, image.GetPixel(50, 50).Alpha);
    }

    /// <summary>Content is never cropped — the source pixels survive squaring.</summary>
    [Fact]
    public async Task Padding_DoesNotCropContent()
    {
        var data = OpaquePngOf(100, 50);
        var result = await Build(256).ProcessAsync(data, "image/png");

        using var image = Decode(result.Data);
        Assert.Equal(255, image.GetPixel(0, 50).Alpha);  // left edge of the original row
        Assert.Equal(255, image.GetPixel(99, 50).Alpha); // right edge of the original row
    }

    [Fact]
    public async Task MaxDimensionZero_Disabled_PassesThrough()
    {
        var data = PngOf(1000, 1000);
        var result = await Build(0).ProcessAsync(data, "image/png");
        Assert.Same(data, result.Data);
    }

    /// <summary>
    /// The SVG case: data Skia cannot build a codec for. It reports that by <b>throwing</b>
    /// <see cref="ArgumentNullException"/>, not by returning null, so the pass-through depends entirely on
    /// that being caught — without the catch every SVG upload becomes a failed request.
    /// </summary>
    [Fact]
    public async Task NonImageData_PassesThroughUnchanged()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        var result = await Build(256).ProcessAsync(data, "image/svg+xml");
        Assert.Same(data, result.Data);
        Assert.Equal("image/svg+xml", result.ContentType);
    }

    /// <summary>
    /// A half-uploaded file, which is a likelier real failure than arbitrary bytes and must not fail the
    /// request either.
    /// </summary>
    /// <remarks>
    /// This was written expecting to cover a second branch — bytes that yield a codec and then fail to
    /// decode, which Skia signals with null. Probed, and it throws exactly like
    /// <see cref="NonImageData_PassesThroughUnchanged"/>: 30 bytes is not enough header to build a codec
    /// from. Kept because a truncated upload is worth pinning on its own terms, but it exercises the same
    /// path, so it is not evidence that the null branch works.
    /// </remarks>
    [Fact]
    public async Task TruncatedImageData_PassesThroughUnchanged()
    {
        var data = PngOf(100, 50).Take(30).ToArray();
        var result = await Build(256).ProcessAsync(data, "image/png");
        Assert.Same(data, result.Data);
        Assert.Equal("image/png", result.ContentType);
    }
}
