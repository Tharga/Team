using Microsoft.Extensions.AI;

namespace Tharga.Team.Sample.Framework;

/// <summary>
/// A chat client that answers from a script, so the sample demonstrates the support assistant with no API
/// key, no network call and no vendor package.
/// </summary>
/// <remarks>
/// <b>This is the sample's stand-in, not something the toolkit ships.</b> What the toolkit consumes is
/// <see cref="IChatClient"/>; what produces one is the host's business. Swapping this for a real model is one
/// registration in <c>Program.cs</c> and touches nothing else — that is the whole point of the design, and
/// having the sample run on a fake is how it stays demonstrable on a machine with no model at all.
/// <para>
/// It answers deterministically and never claims to have fixed anything, which is also what the default
/// instructions tell a real model to do.
/// </para>
/// </remarks>
internal sealed class SampleChatClient : IChatClient
{
    private const string Fallback =
        "I could not work that one out from what I can see on your account. I have left the case open, " +
        "and somebody will pick it up.";

    private static readonly (string Term, string Answer)[] Script =
    [
        ("invit", "Invitation links expire after the lifetime the host configures, and an expired one can be extended without changing the code — so a link already sent keeps working. Ask whoever invited you to extend it rather than sending a new one."),
        ("api key", "An API key is shown once, when it is created. If it has been lost, create a new one and disable the old one rather than trying to recover it."),
        ("team", "You can see the teams you belong to from the team selector. If one is missing, you are probably not a member yet — an administrator of that team can invite you."),
        ("export", "An empty export is usually a filter that matches nothing rather than a failure. Check the date range first, and tell me what it is set to if that is not it.")
    ];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var question = messages?.LastOrDefault(x => x.Role == ChatRole.User)?.Text ?? string.Empty;

        var answer = Array.Find(Script, x => question.Contains(x.Term, StringComparison.OrdinalIgnoreCase)).Answer
            ?? Fallback;

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate(message.Role, message.Text);
        }
    }

    public object GetService(Type serviceType, object serviceKey = null) => null;

    public void Dispose()
    {
    }
}
