using Microsoft.AspNetCore.Components.Server.Circuits;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What starts the circuit's session watch (Tharga/Team#471): the circuit handler, so the session is watched without any component asking, and without the
/// circuit ever waiting for it.
/// </summary>
public class SessionCircuitHandlerTests
{
    private readonly FakeSessionCircuit _circuit = new();

    private SessionCircuitHandler Handler() => new(_circuit);

    [Fact]
    public void TheHandler_IsACircuitHandler()
    {
        Assert.IsAssignableFrom<CircuitHandler>(Handler());
    }

    [Fact]
    public async Task WhenTheCircuitOpens_TheWatchIsStarted()
    {
        await Handler().OnCircuitOpenedAsync(null, CancellationToken.None);

        Assert.Equal(1, _circuit.StartCalls);
    }

    /// <summary>Fire and forget: opening a circuit must not wait for a store read.</summary>
    [Fact]
    public async Task WhenTheCircuitOpens_ItDoesNotWaitForTheWatchToStart()
    {
        var pending = new TaskCompletionSource();
        _circuit.OnStart = () => pending.Task;

        var opened = Handler().OnCircuitOpenedAsync(null, CancellationToken.None);

        Assert.True(opened.IsCompletedSuccessfully);
        Assert.Equal(1, _circuit.StartCalls);
        pending.SetResult();
        await Task.Yield();
    }

    [Fact]
    public async Task WhenStartingTheWatchFails_OpeningTheCircuitStillSucceeds()
    {
        _circuit.OnStart = () => Task.FromException(new InvalidOperationException("the session database is down"));

        var exception = await Record.ExceptionAsync(() => Handler().OnCircuitOpenedAsync(null, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task WhenStartingTheWatchThrowsAtOnce_OpeningTheCircuitStillSucceeds()
    {
        _circuit.OnStart = () => throw new InvalidOperationException("broken");

        var exception = await Record.ExceptionAsync(() => Handler().OnCircuitOpenedAsync(null, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task WhenTheCircuitCloses_TheWatchIsStopped()
    {
        await Handler().OnCircuitClosedAsync(null, CancellationToken.None);

        Assert.Equal(1, _circuit.Disposals);
    }
}
