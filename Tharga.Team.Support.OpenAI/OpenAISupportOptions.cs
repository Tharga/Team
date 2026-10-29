namespace Tharga.Team.Support.OpenAI;

/// <summary>
/// Options for reaching AI providers over the OpenAI-compatible API.
/// </summary>
/// <remarks>
/// <b>An address includes the API's version path</b>, as each provider documents it: <c>https://api.openai.com/v1</c>,
/// Ollama's <c>…:11434/v1</c>, llama.cpp's <c>…:8080/v1</c>.
/// </remarks>
public sealed class OpenAISupportOptions
{
    /// <summary>Whether a team may connect its own OpenAI API key. Default <c>true</c>.</summary>
    public bool OfferOpenAI { get; set; } = true;

    /// <summary>
    /// The address an OpenAI connection starts from. Default <c>https://api.openai.com/v1</c>. A team can change it, for
    /// a proxy in front of the API.
    /// </summary>
    public string OpenAIBaseAddress { get; set; } = "https://api.openai.com/v1";

    /// <summary>Whether a team may connect an Ollama server of its own. Default <c>true</c>.</summary>
    public bool OfferOllama { get; set; } = true;

    /// <summary>
    /// The address an Ollama connection starts from. Default none, so the team gives its server's address: the host's
    /// own machine is not the team's server. Set it where every team shares one server the host runs.
    /// </summary>
    public string OllamaBaseAddress { get; set; }

    /// <summary>Whether a team may connect a llama.cpp server of its own. Default <c>true</c>.</summary>
    public bool OfferLlamaCpp { get; set; } = true;

    /// <summary>The address a llama.cpp connection starts from. Default none, as for <see cref="OllamaBaseAddress"/>.</summary>
    public string LlamaCppBaseAddress { get; set; }

    /// <summary>The token ceiling for one answer. Default 4096.</summary>
    public int MaxOutputTokens { get; set; } = 4096;
}
