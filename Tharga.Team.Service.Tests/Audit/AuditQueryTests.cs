using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

public class AuditQueryTests
{
    [Fact]
    public void Default_Take_Is_100()
    {
        var query = new AuditQuery();
        Assert.Equal(100, query.Take);
    }

    [Fact]
    public void Default_SortDescending_Is_True()
    {
        var query = new AuditQuery();
        Assert.True(query.SortDescending);
    }

    [Fact]
    public void Default_Skip_Is_Zero()
    {
        var query = new AuditQuery();
        Assert.Equal(0, query.Skip);
    }

    [Fact]
    public void All_Filters_Default_To_Null()
    {
        var query = new AuditQuery();

        Assert.Null(query.TeamKey);
        Assert.Null(query.CallerIdentity);
        Assert.Null(query.MethodName);
        Assert.Null(query.Feature);
        Assert.Null(query.Action);
        Assert.Null(query.CallerSource);
        Assert.Null(query.CallerType);
        Assert.Null(query.EventType);
        Assert.Null(query.Success);
        Assert.Null(query.From);
        Assert.Null(query.To);
        Assert.Null(query.SortField);
    }

    [Fact]
    public void Array_Filters_Default_To_Null()
    {
        var query = new AuditQuery();

        Assert.Null(query.TeamKeys);
        Assert.Null(query.Features);
        Assert.Null(query.Actions);
        Assert.Null(query.Scopes);
        Assert.Null(query.EventTypes);
        Assert.Null(query.ExcludedScopes);
        Assert.Null(query.ExcludedEventTypes);
    }
}
