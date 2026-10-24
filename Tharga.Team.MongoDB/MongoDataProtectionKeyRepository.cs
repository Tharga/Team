using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// The Data Protection key ring, kept in MongoDB so every instance protects and unprotects with the same keys.
/// </summary>
/// <remarks>
/// <b>Why it matters here:</b> a team's AI key is stored encrypted with Data Protection. Without a key ring every
/// instance shares and every restart keeps, an instance that did not write a key cannot read it, and every stored
/// key becomes unreadable the day the keys are lost. Modelled on FortDocs' <c>MongoXmlRepository</c>.
/// <para>
/// <see cref="IXmlRepository"/> is synchronous, so the database work runs on a pool thread, off any synchronization
/// context, to avoid a deadlock.
/// </para>
/// </remarks>
internal sealed class MongoDataProtectionKeyRepository(IServiceProvider services) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements() => Task.Run(GetAllElementsAsync).GetAwaiter().GetResult();

    public void StoreElement(XElement element, string friendlyName) => Task.Run(() => StoreElementAsync(element, friendlyName)).GetAwaiter().GetResult();

    private async Task<IReadOnlyCollection<XElement>> GetAllElementsAsync()
    {
        using var scope = services.CreateScope();
        var collection = scope.ServiceProvider.GetRequiredService<IDataProtectionKeyCollection>();

        var elements = new List<XElement>();
        await foreach (var key in collection.GetAsync(_ => true)) elements.Add(XElement.Parse(key.Xml));

        return elements;
    }

    private async Task StoreElementAsync(XElement element, string friendlyName)
    {
        using var scope = services.CreateScope();
        var collection = scope.ServiceProvider.GetRequiredService<IDataProtectionKeyCollection>();

        await collection.AddAsync(new DataProtectionKeyEntity
        {
            Id = ObjectId.GenerateNewId(),
            FriendlyName = string.IsNullOrEmpty(friendlyName) ? Guid.NewGuid().ToString() : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting)
        });
    }
}

/// <summary>Keeping the Data Protection key ring in MongoDB.</summary>
public static class DataProtectionMongoRegistration
{
    /// <summary>
    /// Keeps the Data Protection key ring in MongoDB, in the database the toolkit already uses, so every instance shares it
    /// and it survives a restart.
    /// </summary>
    /// <remarks>
    /// Needed wherever keys are protected on more than one instance, or on a host whose file system does not survive a
    /// restart, such as a container. Set an application name too, with <c>SetApplicationName</c>, so instances of the
    /// same site agree on which keys are theirs.
    /// </remarks>
    public static IDataProtectionBuilder PersistKeysToThargaMongo(this IDataProtectionBuilder builder)
    {
        builder.Services.AddTransient<IDataProtectionKeyCollection, DataProtectionKeyCollection>();
        builder.Services.TrackMongoCollection(typeof(IDataProtectionKeyCollection), typeof(DataProtectionKeyCollection));
        builder.Services.TryAddSingleton<MongoDataProtectionKeyRepository>();
        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<MongoDataProtectionKeyRepository>((options, repository) => options.XmlRepository = repository);

        return builder;
    }
}
