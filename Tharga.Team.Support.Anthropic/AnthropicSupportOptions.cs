namespace Tharga.Team.Support.Anthropic;

/// <summary>
/// Options for reaching AI providers over the Anthropic Messages API.
/// </summary>
public sealed class AnthropicSupportOptions
{
    /// <summary>
    /// The address a Neurolito connection starts from. Default <c>https://neurolito.com</c>. A team can change it, for a
    /// Neurolito server of its own.
    /// </summary>
    public string NeurolitoBaseAddress { get; set; } = "https://neurolito.com";

    /// <summary>
    /// Whether a team may connect its own Anthropic API key. Default <c>true</c>. Set it to <c>false</c> to keep teams on
    /// Neurolito, the platform's model or off. A team already connected to Anthropic then falls back as if it had no
    /// connection: the assistant is not offered to it until it chooses again.
    /// </summary>
    public bool OfferAnthropic { get; set; } = true;

    /// <summary>
    /// The address an Anthropic connection starts from. Default <c>https://api.anthropic.com</c>. A team can change it,
    /// for a proxy in front of the API.
    /// </summary>
    public string AnthropicBaseAddress { get; set; } = "https://api.anthropic.com";

    /// <summary>The token ceiling for one answer. Default 4096.</summary>
    public int MaxOutputTokens { get; set; } = 4096;
}
