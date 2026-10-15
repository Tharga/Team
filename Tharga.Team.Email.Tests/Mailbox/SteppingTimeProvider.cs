namespace Tharga.Team.Email.Tests.Mailbox;

internal sealed class SteppingTimeProvider(DateTime utcNow) : TimeProvider
{
    private DateTime _utcNow = utcNow;

    public void Advance(TimeSpan by) => _utcNow += by;

    public override DateTimeOffset GetUtcNow() => new(_utcNow, TimeSpan.Zero);
}
