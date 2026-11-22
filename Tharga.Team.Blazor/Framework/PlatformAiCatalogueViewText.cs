namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>PlatformAiCatalogueView</c>: the platform's AI providers and models, curated by staff.
/// </summary>
public static class PlatformAiCatalogueViewText
{
    public static readonly TextKey NotPermitted = new("team.support.catalogue.notPermitted", "You cannot curate the platform's AI catalogue.");
    public static readonly TextKey ProvidersTitle = new("team.support.catalogue.providers", "Providers");
    public static readonly TextKey ProvidersExplanation = new("team.support.catalogue.providersExplanation", "The AI providers the platform has accounts with. A key is stored encrypted and is never shown again.");
    public static readonly TextKey ModelsTitle = new("team.support.catalogue.models", "Models");
    public static readonly TextKey ModelsExplanation = new("team.support.catalogue.modelsExplanation", "The models teams may choose from. The default answers a team that has not chosen one.");
    public static readonly TextKey NoProviders = new("team.support.catalogue.noProviders", "No provider yet.");
    public static readonly TextKey NoModels = new("team.support.catalogue.noModels", "No model yet.");
    public static readonly TextKey Name = new("team.support.catalogue.name", "Name");
    public static readonly TextKey Kind = new("team.support.catalogue.kind", "Kind");
    public static readonly TextKey Address = new("team.support.catalogue.address", "Address");
    public static readonly TextKey ApiKey = new("team.support.catalogue.apiKey", "Key");
    public static readonly TextKey ApiKeyStored = new("team.support.catalogue.apiKeyStored", "A key is stored. Leave empty to keep it.");
    public static readonly TextKey HasKey = new("team.support.catalogue.hasKey", "Key stored");
    public static readonly TextKey NoKey = new("team.support.catalogue.noKey", "No key");
    public static readonly TextKey SaveProvider = new("team.support.catalogue.saveProvider", "Save provider");
    public static readonly TextKey NewProvider = new("team.support.catalogue.newProvider", "New provider");
    public static readonly TextKey Edit = new("team.support.catalogue.edit", "Edit");
    public static readonly TextKey Remove = new("team.support.catalogue.remove", "Remove");
    public static readonly TextKey Provider = new("team.support.catalogue.provider", "Provider");
    public static readonly TextKey ModelId = new("team.support.catalogue.modelId", "Model at the provider");
    public static readonly TextKey DisplayName = new("team.support.catalogue.displayName", "Name teams see");
    public static readonly TextKey ProcessedIn = new("team.support.catalogue.processedIn", "Processed in");
    public static readonly TextKey StoredIn = new("team.support.catalogue.storedIn", "Stored in");
    public static readonly TextKey AgreementUrl = new("team.support.catalogue.agreementUrl", "Agreement link");
    public static readonly TextKey PriceInput = new("team.support.catalogue.priceInput", "Input, per million");
    public static readonly TextKey PriceCachedInput = new("team.support.catalogue.priceCachedInput", "Cached, per million");
    public static readonly TextKey PriceCacheWrite = new("team.support.catalogue.priceCacheWrite", "Cache write, per million");
    public static readonly TextKey PriceOutput =new("team.support.catalogue.priceOutput", "Output, per million");
    public static readonly TextKey Offered = new("team.support.catalogue.offered", "Offered to teams");
    public static readonly TextKey Default = new("team.support.catalogue.default", "Default");
    public static readonly TextKey MakeDefault = new("team.support.catalogue.makeDefault", "Make default");
    public static readonly TextKey SaveModel = new("team.support.catalogue.saveModel", "Save model");
    public static readonly TextKey NewModel = new("team.support.catalogue.newModel", "New model");
    public static readonly TextKey LoadModels = new("team.support.catalogue.loadModels", "Load the provider's models");
    public static readonly TextKey Test = new("team.support.catalogue.test", "Test");
    public static readonly TextKey TestReply = new("team.support.catalogue.testReply", "The model answered:");
    public static readonly TextKey Saved = new("team.support.catalogue.saved", "Saved.");
    public static readonly TextKey Failed = new("team.support.catalogue.failed", "That could not be done.");
    public static readonly TextKey ChangedBy = new("team.support.catalogue.changedBy", "Last changed by {0}, {1}.");

    public static readonly TextKey[] All =
    [
        NotPermitted, ProvidersTitle, ProvidersExplanation, ModelsTitle, ModelsExplanation, NoProviders, NoModels, Name, Kind, Address, ApiKey, ApiKeyStored, HasKey, NoKey,
        SaveProvider, NewProvider, Edit, Remove, Provider, ModelId, DisplayName, ProcessedIn, StoredIn, AgreementUrl, PriceInput, PriceCachedInput, PriceCacheWrite, PriceOutput, Offered,
        Default, MakeDefault, SaveModel, NewModel, LoadModels, Test, TestReply, Saved, Failed, ChangedBy
    ];
}

/// <summary>Localizable strings rendered by <c>SupportAssistantAdminView</c>: the tabs around usage and the catalogue.</summary>
public static class SupportAssistantAdminViewText
{
    public static readonly TextKey UsageTab = new("team.support.admin.usage", "Usage");
    public static readonly TextKey ConfigurationTab = new("team.support.admin.configuration", "Configuration");

    public static readonly TextKey[] All = [UsageTab, ConfigurationTab];
}
