namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// The assistant's answer as written so far, brought onto the component's own thread as it grows.
/// </summary>
/// <remarks>
/// Reports arrive on whatever thread the model's stream is read on. Once the answer is kept or declined,
/// <see cref="End"/> stops any report still on its way from drawing over the conversation read back.
/// </remarks>
internal sealed class SupportAnswerSoFar(Func<Action, Task> invoke, Action<string> show) : IProgress<string>
{
    private volatile bool _ended;

    public void Report(string value)
    {
        if (_ended) return;

        _ = invoke(() =>
        {
            if (!_ended) show(value);
        });
    }

    /// <summary>Shows nothing more.</summary>
    public void End() => _ended = true;
}
