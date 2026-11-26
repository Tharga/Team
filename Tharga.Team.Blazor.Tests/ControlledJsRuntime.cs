using Microsoft.JSInterop;

namespace Tharga.Team.Blazor.Tests;

/// <summary>A browser whose <c>import</c> completes when the test says, with the module the test gives it; anything else it is asked completes at once.</summary>
internal sealed class ControlledJsRuntime : IJSRuntime
{
    private const string ImportIdentifier = "import";

    public TaskCompletionSource<IJSObjectReference> Import { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object[] args)
        => identifier == ImportIdentifier ? new ValueTask<TValue>((Task<TValue>)(object)Import.Task) : ValueTask.FromResult<TValue>(default);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object[] args)
        => identifier == ImportIdentifier ? new ValueTask<TValue>((Task<TValue>)(object)Import.Task) : ValueTask.FromResult<TValue>(default);
}
