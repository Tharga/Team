using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Reading how users sign in: a page of rows in one call, or every row in batches in order with a report after each, a
/// failure or a cancellation stopping at a batch boundary with what was read kept.
/// </summary>
public class SignInMethodReaderTests
{
    private static readonly DirectoryUserIdentity Broker = new("federated", "https://broker.example/", "sub-1");
    private static readonly DirectoryUserIdentity Mail = new("emailAddress", "example.com", "anna@example.com");

    private readonly List<string[]> _calls = [];

    private SignInMethodReader Reader(Func<string[], CancellationToken, Task<UserSignInIdentities[]>> identities = null)
    {
        var options = new SignInMethodOptions().Add(x => x.Issuer == "https://broker.example/" ? new SignInMethod("e-ID") : null);

        return new SignInMethodReader(identities ?? Answer, options, new SignInFallbackLabels());
    }

    private Task<UserSignInIdentities[]> Answer(string[] keys, CancellationToken cancellationToken)
    {
        _calls.Add(keys);

        return Task.FromResult(keys.Select(x => new UserSignInIdentities(x, x.EndsWith('1') ? [Broker, Mail] : [Mail])).ToArray());
    }

    private static UserViewModel[] Rows(int count)
        => [.. Enumerable.Range(1, count).Select(x => new UserViewModel { Key = $"user-{x}", Name = $"User {x}", DirectoryId = $"dir-{x}" })];

    [Fact]
    public async Task ABatch_IsReadInOneCall_AndEachRowGetsItsMethodsMapped()
    {
        var rows = Rows(3);

        await Reader().ReadBatchAsync(rows);

        Assert.Equal(["user-1", "user-2", "user-3"], Assert.Single(_calls));
        Assert.Equal(["e-ID", "Email"], rows[0].SignInMethods.Select(x => x.Label));
        Assert.Equal(["Email"], rows[1].SignInMethods.Select(x => x.Label));
    }

    [Fact]
    public async Task AUserTheDirectoryNoLongerKnows_GetsNoMethods_NotNull()
    {
        var rows = Rows(2);
        var reader = Reader((keys, _) => Task.FromResult(new[] { new UserSignInIdentities("user-1", [Mail]) }));

        await reader.ReadBatchAsync(rows);

        Assert.NotNull(rows[1].SignInMethods);
        Assert.Empty(rows[1].SignInMethods);
    }

    [Fact]
    public async Task AFailedBatch_SetsNothing_AndThrows()
    {
        var rows = Rows(2);
        var reader = Reader((_, _) => throw new TimeoutException("The directory is down."));

        await Assert.ThrowsAsync<TimeoutException>(() => reader.ReadBatchAsync(rows));

        Assert.All(rows, x => Assert.Null(x.SignInMethods));
        Assert.All(rows, x => Assert.Null(x.SignInError));
    }

    [Fact]
    public async Task EveryRow_IsReadInBatches_InOrder_WithAReportAfterEach()
    {
        var rows = Rows(SignInMethodReader.BatchSize * 2 + 20);
        var reported = new List<int>();

        await Reader().ReadAsync(rows, batch =>
        {
            reported.Add(batch.Length);
            return Task.CompletedTask;
        });

        Assert.Equal([50, 50, 20], _calls.Select(x => x.Length));
        Assert.Equal([50, 50, 20], reported);
        Assert.Equal(rows.Select(x => x.Key), _calls.SelectMany(x => x));
        Assert.All(rows, x => Assert.NotNull(x.SignInMethods));
    }

    [Fact]
    public async Task ACancellation_StopsBetweenBatches_AndWhatWasReadStaysRead()
    {
        var rows = Rows(SignInMethodReader.BatchSize * 3);
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Reader().ReadAsync(rows, _ =>
        {
            cancel.Cancel();
            return Task.CompletedTask;
        }, cancel.Token));

        Assert.Single(_calls);
        Assert.All(rows.Take(SignInMethodReader.BatchSize), x => Assert.NotNull(x.SignInMethods));
        Assert.All(rows.Skip(SignInMethodReader.BatchSize), x => Assert.Null(x.SignInMethods));
    }

    [Fact]
    public async Task AFailureInALaterBatch_StopsTheReading_AndTheEarlierBatchesStayRead()
    {
        var rows = Rows(SignInMethodReader.BatchSize * 2);
        var calls = 0;
        var reader = Reader((keys, token) => ++calls == 2 ? throw new TimeoutException("The directory is down.") : Answer(keys, token));

        await Assert.ThrowsAsync<TimeoutException>(() => reader.ReadAsync(rows, _ => Task.CompletedTask));

        Assert.All(rows.Take(SignInMethodReader.BatchSize), x => Assert.NotNull(x.SignInMethods));
        Assert.All(rows.Skip(SignInMethodReader.BatchSize), x => Assert.Null(x.SignInMethods));
    }

    [Fact]
    public async Task NoRows_MakeNoCall()
    {
        await Reader().ReadAsync([], _ => Task.CompletedTask);

        Assert.Empty(_calls);
    }

    [Fact]
    public async Task TheToken_ReachesTheDirectoryCall()
    {
        CancellationToken seen = default;
        using var cancel = new CancellationTokenSource();
        var reader = Reader((keys, token) =>
        {
            seen = token;
            return Answer(keys, token);
        });

        await reader.ReadBatchAsync(Rows(1), cancel.Token);

        Assert.Equal(cancel.Token, seen);
    }
}
