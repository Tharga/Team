using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Interception;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The log-only counterpart of the database-boundary guard (Tharga/Team#397): it finds unscoped calls without
/// refusing them, so it can be registered on a running site.
/// </summary>
public class TeamAccessReportInterceptorTests
{
    private static CollectionCallInfo Call(string collection = "Team", string operation = "GetOneAsync", InterceptionPoint point = InterceptionPoint.Invocation) => new()
    {
        CollectionName = collection,
        Operation = operation,
        OperationType = Operation.Read,
        EntityType = typeof(object),
        Point = point
    };

    [Fact]
    public async Task AnUnscopedCall_Proceeds_AndIsRecorded()
    {
        var interceptor = new TeamAccessReportInterceptor();

        var decision = await interceptor.BeforeCallAsync(Call());

        Assert.False(decision.IsRejected);
        var seen = Assert.Single(interceptor.Observed);
        Assert.Equal("Team", seen.Collection);
        Assert.Equal("GetOneAsync", seen.Operation);
    }

    [Fact]
    public async Task AScopedCall_IsNotRecorded()
    {
        var interceptor = new TeamAccessReportInterceptor();

        using (TeamAccess.System("nightly retention"))
        {
            var decision = await interceptor.BeforeCallAsync(Call());
            Assert.False(decision.IsRejected);
        }

        Assert.Empty(interceptor.Observed);
    }

    [Fact]
    public async Task TheSameCallPath_IsRecordedOnce()
    {
        var interceptor = new TeamAccessReportInterceptor();

        for (var i = 0; i < 5; i++) await interceptor.BeforeCallAsync(Call());

        Assert.Single(interceptor.Observed);
    }

    [Fact]
    public async Task ADifferentCollectionOrOperation_IsRecordedSeparately()
    {
        var interceptor = new TeamAccessReportInterceptor();

        await interceptor.BeforeCallAsync(Call());
        await interceptor.BeforeCallAsync(Call(collection: "User"));
        await interceptor.BeforeCallAsync(Call(operation: "AddAsync"));

        Assert.Equal(3, interceptor.Observed.Count);
    }

    [Fact]
    public async Task TheCallPath_NamesTheCallingCode_NotTheFramework()
    {
        var interceptor = new TeamAccessReportInterceptor();

        await interceptor.BeforeCallAsync(Call());

        var path = Assert.Single(interceptor.Observed).CallPath;
        Assert.Contains(nameof(TheCallPath_NamesTheCallingCode_NotTheFramework), path);
        Assert.DoesNotContain("System.Runtime", path);
        Assert.DoesNotContain("TeamAccessReportInterceptor.", path);
    }

    [Fact]
    public void ItRunsAtInvocationOnly()
    {
        Assert.Equal(InterceptionPoint.Invocation, new TeamAccessReportInterceptor().Points);
    }
}
