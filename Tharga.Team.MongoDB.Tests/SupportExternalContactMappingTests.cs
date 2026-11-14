using MongoDB.Bson;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Who wrote through a public form is stored with the case and read back, and a case raised any other way stores nothing
/// for it.
/// </summary>
public class SupportExternalContactMappingTests
{
    private static readonly DateTime Consent = new(2026, 11, 1, 11, 59, 0, DateTimeKind.Utc);

    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();

    private static SupportCase Case(ExternalSupportContact contact = null) => new()
    {
        Id = "case-1",
        TeamKey = null,
        AuthorIdentity = null,
        AuthorName = "Vera Visitor",
        Subject = "Question",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        ExternalContact = contact
    };

    private static ExternalSupportContact Contact() => new()
    {
        Name = "Vera Visitor",
        Email = "visitor@example.com",
        Phone = "070-1234567",
        Organisation = "Acme AB",
        ConsentGivenAt = Consent,
        Fields = new Dictionary<string, string> { ["Product"] = "Export", ["a.b"] = "dotted", ["$odd"] = "dollar" }
    };

    private static SupportMessage First => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorName = "Vera Visitor",
        Body = "Hello",
        SentAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
        Source = SupportChannelType.Email
    };

    private async Task<SupportCaseEntity> StoredAsync(SupportCase supportCase)
    {
        SupportCaseEntity added = null;
        await _collection.AddAsync(Arg.Do<SupportCaseEntity>(x => added = x));

        await new MongoSupportCaseStore(_collection).AddCaseAsync(supportCase, First, TestContext.Current.CancellationToken);

        return added;
    }

    [Fact]
    public async Task TheContact_IsStoredWithItsConsent()
    {
        var stored = await StoredAsync(Case(Contact()));

        Assert.Equal("visitor@example.com", stored.ExternalContact.Email);
        Assert.Equal("Acme AB", stored.ExternalContact.Organisation);
        Assert.Equal(Consent, stored.ExternalContact.ConsentGivenAt);
    }

    /// <summary>A field name is whatever a host's form calls it, and a document key may not hold a dot or start with a dollar, so they are stored as pairs.</summary>
    [Fact]
    public async Task HostFields_AreStoredAsPairs_SoAnyNameIsSafeAsAKey()
    {
        var document = (await StoredAsync(Case(Contact()))).ToBsonDocument();

        var fields = document[nameof(SupportCaseEntity.ExternalContact)][nameof(SupportExternalContactEntity.Fields)].AsBsonArray;
        Assert.Equal(3, fields.Count);
        Assert.Contains(fields, x => x["Name"].AsString == "a.b" && x["Value"].AsString == "dotted");
    }

    [Fact]
    public async Task ACaseRaisedAnyOtherWay_StoresNoContact()
    {
        var document = (await StoredAsync(Case())).ToBsonDocument();

        Assert.False(document.Contains(nameof(SupportCaseEntity.ExternalContact)));
    }

    [Fact]
    public async Task TheContact_IsReadBack()
    {
        var stored = await StoredAsync(Case(Contact()));
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);

        var read = (await new MongoSupportCaseStore(_collection).GetCaseByIdAsync("case-1", TestContext.Current.CancellationToken)).ExternalContact;

        Assert.Equal("Vera Visitor", read.Name);
        Assert.Equal("visitor@example.com", read.Email);
        Assert.Equal("070-1234567", read.Phone);
        Assert.Equal("Acme AB", read.Organisation);
        Assert.Equal(Consent, read.ConsentGivenAt);
        Assert.Equal("dotted", read.Fields["a.b"]);
        Assert.Equal("dollar", read.Fields["$odd"]);
    }

    /// <summary>A case support starts by mail waits on the reply, not on support: it must not be listed as needing an answer.</summary>
    [Fact]
    public async Task ACaseOpenedWithSupportsOwnMessage_IsNotWaitingOnSupport()
    {
        SupportCaseEntity added = null;
        await _collection.AddAsync(Arg.Do<SupportCaseEntity>(x => added = x));

        await new MongoSupportCaseStore(_collection).AddCaseAsync(Case(Contact()), First with { AsSupport = true, Source = null, AuthorName = "Anna" }, TestContext.Current.CancellationToken);

        Assert.False(added.LastMessageFromAuthor);
        Assert.True((await StoredAsync(Case(Contact()))).LastMessageFromAuthor);
    }

    [Fact]
    public async Task ACaseStoredBeforeContactsExisted_ReadsAsHavingNone()
    {
        var stored = await StoredAsync(Case());
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);

        var read = await new MongoSupportCaseStore(_collection).GetCaseByIdAsync("case-1", TestContext.Current.CancellationToken);

        Assert.Null(read.ExternalContact);
    }
}
