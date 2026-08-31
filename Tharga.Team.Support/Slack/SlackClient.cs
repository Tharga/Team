using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Support.Slack;

/// <summary>
/// Posts to Slack's <c>chat.postMessage</c> Web API over <see cref="IHttpClientFactory"/>.
/// </summary>
/// <remarks>
/// <b>Never throws.</b> Every failure — no token, no network, an HTTP error, a Slack-level rejection —
/// comes back as a failed <see cref="SlackPostResult"/>. A notification is an observation of something
/// that already happened, so a transport problem must not become the caller's problem.
/// <para>
/// <b>Slack reports its own failures with HTTP 200.</b> A bad token, a channel the bot was never
/// invited to, a rate limit: all arrive as <c>200 OK</c> with <c>{"ok":false,"error":"…"}</c>. Checking
/// only the status code would report every one of those as a successful post, so the body is parsed.
/// </para>
/// </remarks>
public sealed class SlackClient : ISlackClient
{
    /// <summary>Name of the named <see cref="HttpClient"/> this client resolves.</summary>
    public const string HttpClientName = "Tharga.Team.Support.Slack";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SlackOptions _options;
    private readonly ILogger<SlackClient> _logger;

    public SlackClient(IHttpClientFactory httpClientFactory, IOptions<SlackOptions> options, ILogger<SlackClient> logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options?.Value ?? new SlackOptions();
        _logger = logger;
    }

    public async Task<SlackPostResult> PostAsync(string channel, string text, string threadId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channel)) return SlackPostResult.Failed("No channel.");
        if (string.IsNullOrWhiteSpace(text)) return SlackPostResult.Failed("No message.");

        // An unconfigured host is the expected state, not an error worth a stack trace on every event.
        if (string.IsNullOrWhiteSpace(_options.BotToken)) return SlackPostResult.Failed("No Slack bot token configured.");

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress ??= new Uri(_options.ApiBaseAddress);
            client.Timeout = _options.Timeout;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.BotToken);

            using var response = await client.PostAsJsonAsync(
                "chat.postMessage",
                new SlackPostMessageRequest(channel, text, threadId),
                SerializerOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode) return SlackPostResult.Failed($"Slack returned {(int)response.StatusCode}.");

            var body = await response.Content.ReadFromJsonAsync<SlackPostMessageResponse>(SerializerOptions, cancellationToken);
            if (body?.Ok == true) return SlackPostResult.Ok(body.Ts);

            return SlackPostResult.Failed(body?.Error ?? "Slack rejected the message without saying why.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Posting to Slack channel {Channel} failed.", channel);
            return SlackPostResult.Failed(ex.Message);
        }
    }

    /// <remarks>
    /// <c>thread_ts</c> is omitted when null rather than sent as null - Slack rejects an explicit null, and
    /// a message with no thread is a new top-level post, which is exactly what a notification wants.
    /// </remarks>
    private sealed record SlackPostMessageRequest(
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("thread_ts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string ThreadTs);

    private sealed record SlackPostMessageResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("ts")] string Ts);
}
