# Tharga.Team.Support.Anthropic

Lets each team's support assistant answer through **the team's own Neurolito or Anthropic key**, with the model chosen
from what that provider offers the key.

Both speak the Anthropic Messages API, so this package reaches them with the official `Anthropic` SDK. It is a
package of its own for one reason: it is the only part of the toolkit that takes a vendor SDK, and a host that never
lets teams bring their own AI should not acquire it.

## Use

```csharp
builder.Services.AddThargaSupportCases();
builder.Services.AddThargaSupportAiConnections();   // each team chooses: the platform's model, its own provider, or off
builder.Services.AddThargaSupportAiProviders();     // adds Neurolito and Anthropic as providers a team can choose

// Keys are stored encrypted with Data Protection. Keep the key ring where every instance reads it:
builder.Services.AddDataProtection()
    .SetApplicationName("MySite")
    .PersistKeysToThargaMongo();
```

Then put `<SupportAssistantConnectionView />` on a page for team administrators. It needs the `assistant:manage` team
scope, which administrators hold by default.

## What a team gets

- **Load models** lists what the key may use, from `GET /v1/models`. A refused key, a wrong address or an unreachable
  server is said in words on the form.
- **Test** sends the chosen model one short message and shows the reply.
- **Save** checks once more that the provider offers the model for the key, then stores the key encrypted. It is never
  shown again; it can be replaced.

## Options

```csharp
builder.Services.AddThargaSupportAiProviders(o =>
{
    o.NeurolitoBaseAddress = "https://neurolito.com";      // where a Neurolito connection starts; a team can change it
    o.OfferAnthropic = true;                              // false keeps teams on Neurolito, the platform's model or off
    o.AnthropicBaseAddress = "https://api.anthropic.com"; // where an Anthropic connection starts; a team can change it
    o.MaxOutputTokens = 4096;                             // the ceiling for one answer
});
```

`AddThargaSupportAiProviders` was called `AddThargaSupportAnthropic` until 4.5; the old name still works and is marked
obsolete.

Full documentation: [Support cases](https://github.com/Tharga/Team/blob/master/docs/articles/support-cases.md).

## Links

- [GitHub](https://github.com/Tharga/Team)
- [NuGet](https://www.nuget.org/packages/Tharga.Team.Support.Anthropic)
