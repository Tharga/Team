using Microsoft.JSInterop;

namespace Tharga.Team.Blazor.Tests;

/// <summary>A browser module the test hands to <c>import</c> itself: it keeps the name of every call, and its release can fail as a browser that went away does.</summary>
internal sealed class RecordingModule : IJSObjectReference
{
    public List<string> Calls { get; } = [];

    public bool Disposed { get; private set; }

    /// <summary>What <see cref="DisposeAsync"/> throws, after marking itself disposed; null releases it quietly.</summary>
    public Exception DisposeFailure { get; init; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object[] args)
    {
        Calls.Add(identifier);
        return ValueTask.FromResult<TValue>(default);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object[] args) => InvokeAsync<TValue>(identifier, args);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return DisposeFailure == null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
    }
}
