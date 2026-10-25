using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>The host's <see cref="SupportDisplayOptions"/>, or the defaults where the Blazor options are not registered.</summary>
internal static class SupportDisplay
{
    public static SupportDisplayOptions Of(IServiceProvider services)
        => (services?.GetService(typeof(IOptions<ThargaBlazorOptions>)) as IOptions<ThargaBlazorOptions>)?.Value?.Support ?? new SupportDisplayOptions();
}
