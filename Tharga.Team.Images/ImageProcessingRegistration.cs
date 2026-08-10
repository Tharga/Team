using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;

namespace Tharga.Team.Images;

public static class ImageProcessingRegistration
{
    /// <summary>
    /// Registers the SkiaSharp-based <see cref="IIconProcessor"/>, so uploaded icons are squared and any
    /// larger than <see cref="IconOptions.MaxDimension"/> (default 256px) are automatically downscaled
    /// before storage instead of being rejected.
    /// </summary>
    public static IServiceCollection AddThargaImageProcessing(this IServiceCollection services)
    {
        services.AddScoped<IIconProcessor, SkiaIconProcessor>();
        return services;
    }
}
