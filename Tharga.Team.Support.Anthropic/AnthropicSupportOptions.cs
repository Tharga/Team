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

    /// <summary>The token ceiling for one answer. Default 4096.</summary>
    public int MaxOutputTokens { get; set; } = 4096;
}
