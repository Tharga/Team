# Tharga.Team.Support.OpenAI

Lets each team's support assistant answer through **the team's own OpenAI key**, or through **an Ollama or llama.cpp
server of the team's own**, with the model chosen from what that provider offers.

All three speak the OpenAI-compatible API, so this package reaches them with the official `OpenAI` SDK through
`Microsoft.Extensions.AI.OpenAI`. It is a package of its own so that a host which never lets teams bring their own AI
does not acquire a vendor SDK.

## Use

```csharp
builder.Services.AddThargaSupportCases();
builder.Services.AddThargaSupportAiConnections();     // each team chooses: the platform's model, its own provider, or off
builder.Services.AddThargaSupportOpenAIProviders();   // adds OpenAI, Ollama and llama.cpp as providers a team can choose
builder.Services.AddThargaSupportAiProviders();       // optional, from Tharga.Team.Support.Anthropic: Neurolito and Anthropic

// Keys are stored encrypted with Data Protection. Keep the key ring where every instance reads it:
builder.Services.AddDataProtection()
    .SetApplicationName("MySite")
    .PersistKeysToThargaMongo();
```

Then put `<SupportAssistantConnectionView />` on a page for team administrators, or map the endpoints with
`app.MapThargaSupportAiConnection()`. Both need the `assistant:manage` team scope, which administrators hold by default.

## What a team gets

- **OpenAI** starts from `https://api.openai.com/v1` and needs the team's key.
- **Ollama** and **llama.cpp** start from no address: the team gives its server's, including the version path
  (`https://ollama.example.com/v1`, `https://llama.example.com/v1`). The key is optional and sent only if given.
- **Load models** lists what the provider offers, from `GET {address}/models`. OpenAI lists every model the key may
  use, including some that do not chat; **Test** sends the chosen model one short message and shows the reply.
- **Save** checks once more that the provider offers the model, then stores the key, if any, encrypted. It is never
  shown again; it can be replaced.

An address must be https, or http on the same machine as the site. A server elsewhere on the network needs https in
front of it, such as a reverse proxy.

## Options

```csharp
builder.Services.AddThargaSupportOpenAIProviders(o =>
{
    o.OfferOpenAI = true;                               // false leaves OpenAI out
    o.OpenAIBaseAddress = "https://api.openai.com/v1";  // where an OpenAI connection starts; a team can change it
    o.OfferOllama = true;                               // false leaves Ollama out
    o.OllamaBaseAddress = null;                         // set it where every team shares one server you run
    o.OfferLlamaCpp = true;                             // false leaves llama.cpp out
    o.LlamaCppBaseAddress = null;
    o.MaxOutputTokens = 4096;                           // the ceiling for one answer
});
```

Full documentation: [Support cases](https://github.com/Tharga/Team/blob/master/docs/articles/support-cases.md).

## Links

- [GitHub](https://github.com/Tharga/Team)
- [NuGet](https://www.nuget.org/packages/Tharga.Team.Support.OpenAI)
