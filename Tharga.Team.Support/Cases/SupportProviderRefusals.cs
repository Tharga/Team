using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Tells a provider refusing the account from a request that went wrong.
/// </summary>
/// <remarks>
/// <b>The two need opposite handling.</b> A bad request, a rate limit that clears in a minute or an overloaded model deserves another try; an account whose
/// key was revoked, whose billing failed or whose spend limit is reached fails every request the same way until a person acts, so trying again is spending
/// tokens and goodwill on nothing. The status is read from the exception by name, so no provider's package is referenced: <c>StatusCode</c> on Anthropic's
/// exceptions and <c>Status</c> on OpenAI's, which Mistral, Ollama and llama.cpp are reached through.
/// <para>
/// 401, 402 and 403 are refusals of the account. 400 and 429 are only when the provider's own words say so, because Anthropic reports an empty balance as
/// a 400 and an exhausted quota as a 429, which also carries ordinary rate limiting. With no status at all, the words decide.
/// </para>
/// </remarks>
internal static class ProviderRefusal
{
    private static readonly string[] AccountWords =
    [
        "credit balance", "billing", "insufficient_quota", "exceeded your current quota", "quota", "usage limit", "spend limit", "spending limit",
        "monthly limit", "payment required", "account is not active", "account has been disabled", "organization has been disabled", "invalid x-api-key",
        "invalid api key", "incorrect api key", "api key has been revoked"
    ];

    public static bool IsAccountLevel(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var status = StatusOf(current);
            if (status is 401 or 402 or 403) return true;

            var worded = status is null or 400 or 429;
            if (worded && AccountWords.Any(x => current.Message.Contains(x, StringComparison.OrdinalIgnoreCase))) return true;
        }

        return false;
    }

    private static int? StatusOf(Exception exception)
    {
        foreach (var name in new[] { "StatusCode", "Status" })
        {
            var value = exception.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(exception);
            switch (value)
            {
                case HttpStatusCode code: return (int)code;
                case int number and > 0: return number;
            }
        }

        return null;
    }
}

/// <summary>
/// Remembers for a while which teams the provider has refused, so the assistant stops asking, says why, and tries again later.
/// </summary>
/// <remarks>
/// <b>In memory, per team, and on purpose.</b> The refusal is the provider's, so it heals by itself when a person tops up the account or replaces the key; the
/// only cost of forgetting it on a restart is one more refused request, which finds it out again. Held per team because a team may be on its own provider with its
/// own key, and one team's revoked key says nothing about another's. A singleton, because the answer is given in one request's scope and read in another's.
/// </remarks>
internal sealed class SupportProviderRefusals(TimeProvider timeProvider, SupportAssistantOptions options)
{
    private readonly ConcurrentDictionary<string, DateTime> _until = new();

    public void Refused(string teamKey)
    {
        if (!string.IsNullOrEmpty(teamKey)) _until[teamKey] = timeProvider.GetUtcNow().UtcDateTime + options.ProviderRefusalRetryAfter;
    }

    public void Answered(string teamKey)
    {
        if (!string.IsNullOrEmpty(teamKey)) _until.TryRemove(teamKey, out _);
    }

    public bool IsRefused(string teamKey)
    {
        if (string.IsNullOrEmpty(teamKey) || !_until.TryGetValue(teamKey, out var until)) return false;
        if (timeProvider.GetUtcNow().UtcDateTime < until) return true;

        _until.TryRemove(teamKey, out _);
        return false;
    }
}
