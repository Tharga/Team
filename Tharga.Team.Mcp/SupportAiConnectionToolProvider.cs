using System.Text.Json;
using System.Text.Json.Serialization;
using Tharga.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp;

/// <summary>
/// MCP tools over a team's support AI connection: read it, list the models of a provider, test one, connect, use the
/// platform's model and turn the assistant off. The same operations <c>MapThargaSupportAiConnection</c> offers over REST.
/// </summary>
/// <remarks>
/// <b>Authorization is the service's, not this provider's.</b> <see cref="ISupportAiConnectionService"/> requires
/// <c>assistant:manage</c> on the team and audits every change, a refusal included, so an API key's scopes are checked by
/// the same code that checks a user's. This provider adds no check of its own, reaches no storage, and says a refusal in
/// words rather than as a transport error, so an agent reads why.
/// <para>
/// <b>The team is the caller's own</b>, from the same claim the REST endpoints read, and never an argument beside it that
/// could disagree.
/// </para>
/// <para>
/// <b>The key goes in and never comes out.</b> <c>connect</c>, <c>test</c> and <c>list_models</c> take it; nothing this
/// provider returns carries it, and an error never quotes what was sent, because that holds it. It does travel through
/// whatever conversation the agent keeps, which is the agent's to protect.
/// </para>
/// <para>
/// <b>Registered only when the host asks</b> (<see cref="McpTeamOptions.ExposeSupportAiConnection"/>), as the REST endpoints
/// are only mapped when the host maps them. Without a registered service, or without a team, nothing is offered.
/// </para>
/// </remarks>
public sealed class SupportAiConnectionToolProvider : IMcpToolProvider
{
    public const string GetConnection = "support.ai.get_connection";
    public const string GetChoices = "support.ai.get_choices";
    public const string ListModels = "support.ai.list_models";
    public const string Test = "support.ai.test";
    public const string Connect = "support.ai.connect";
    public const string UsePlatformModel = "support.ai.use_platform_model";
    public const string TurnOff = "support.ai.turn_off";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonElement NoArguments = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private static readonly JsonElement ProviderArguments = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "description": "The provider: one of the kinds support.ai.get_choices lists, such as Neurolito, Anthropic, OpenAI, Ollama or LlamaCpp." },
            "baseAddress": { "type": "string", "description": "The provider's address. Leave out for the provider's default, where it has one." },
            "apiKey": { "type": "string", "description": "The provider's key. Accepted here and never returned. Leave out to use the key the team already stored, allowed only while the kind and the address are unchanged." },
            "model": { "type": "string", "description": "The model to answer with. Required to connect and to test; not used to list models." }
          },
          "required": ["kind"]
        }
        """).RootElement.Clone();

    private readonly ISupportAiConnectionService _service;

    public SupportAiConnectionToolProvider(ISupportAiConnectionService service = null) => _service = service;

    public McpScope Scope => McpScope.Team;

    public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(IMcpContext context, CancellationToken cancellationToken)
    {
        if (_service == null || string.IsNullOrEmpty(context.AsTeamContext()?.TeamId))
            return Task.FromResult<IReadOnlyList<McpToolDescriptor>>([]);

        IReadOnlyList<McpToolDescriptor> tools =
        [
            new() { Name = GetConnection, Description = "Reads what the support assistant answers the caller's team with: the platform's model, a provider and model, or off. Never returns the key; says whether one is stored.", InputSchema = NoArguments },
            new() { Name = GetChoices, Description = "Reads what the team may choose: the kinds on offer, where each provider starts from, and what the platform's model is.", InputSchema = NoArguments },
            new() { Name = ListModels, Description = "Lists the models a provider offers for a key, so one can be chosen. Saves nothing.", InputSchema = ProviderArguments },
            new() { Name = Test, Description = "Sends the model one short message and returns its reply, to show that answering works. Saves nothing.", InputSchema = ProviderArguments },
            new() { Name = Connect, Description = "Answers the team with a provider, after checking that the provider lists the chosen model for the key. Nothing is saved unless that check passes. The key is stored encrypted and never returned. Requires assistant:manage.", InputSchema = ProviderArguments },
            new() { Name = UsePlatformModel, Description = "Answers the team with the platform's model, and forgets any key the team stored. Requires assistant:manage.", InputSchema = NoArguments },
            new() { Name = TurnOff, Description = "Turns the support assistant off for the team, and forgets any key the team stored. Requires assistant:manage.", InputSchema = NoArguments }
        ];

        return Task.FromResult(tools);
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, JsonElement arguments, IMcpContext context, CancellationToken cancellationToken)
    {
        var teamKey = context.AsTeamContext()?.TeamId;
        if (string.IsNullOrEmpty(teamKey)) throw new UnauthorizedAccessException("This needs a team: a team API key, or a system key naming one.");
        if (_service == null) throw new InvalidOperationException("The support AI connection is not registered on this site.");

        try
        {
            switch (toolName)
            {
                case GetConnection:
                    return Ok(await _service.GetConnectionAsync(teamKey, cancellationToken));
                case GetChoices:
                    return Ok(await _service.GetChoicesAsync(teamKey, cancellationToken));
                case ListModels:
                {
                    if (!TryRequest(arguments, out var request, out var problem)) return Error(problem);

                    var list = await _service.ListModelsAsync(teamKey, request, cancellationToken);
                    return list.Problem == null ? Ok(list) : Error(list.Problem);
                }
                case Test:
                {
                    if (!TryRequest(arguments, out var request, out var problem)) return Error(problem);

                    return Outcome(await _service.TestAsync(teamKey, request, cancellationToken));
                }
                case Connect:
                {
                    if (!TryRequest(arguments, out var request, out var problem)) return Error(problem);

                    return Outcome(await _service.ConnectAsync(teamKey, request, cancellationToken));
                }
                case UsePlatformModel:
                    try
                    {
                        await _service.UsePlatformModelAsync(teamKey, cancellationToken);
                        return Ok(new { done = true });
                    }
                    catch (InvalidOperationException)
                    {
                        return Error("This site has no platform model to answer with.");
                    }
                case TurnOff:
                    await _service.TurnOffAsync(teamKey, cancellationToken);
                    return Ok(new { done = true });
                default:
                    throw new InvalidOperationException($"Unknown tool '{toolName}'.");
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Error("Changing or reading the support assistant's connection needs the assistant:manage scope on this team, which the caller does not hold.");
        }
    }

    private static McpToolResult Outcome(SupportAiConnectionResult result) => result.Succeeded ? Ok(result) : Error(result.Problem);

    private static McpToolResult Ok(object value) => new() { Content = [new McpContent { Type = "text", Text = JsonSerializer.Serialize(value, Json) }] };

    private static McpToolResult Error(string message) => new() { IsError = true, Content = [new McpContent { Type = "text", Text = message }] };

    /// <summary>The provider request from the arguments, or why it is not one. Never quotes what was sent: it holds the key.</summary>
    private static bool TryRequest(JsonElement arguments, out SupportAiProviderRequest request, out string problem)
    {
        request = null;

        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
            || !Enum.TryParse<SupportAiConnectionKind>(kind.GetString(), true, out var parsed) || !Enum.IsDefined(parsed))
        {
            problem = "The 'kind' argument is required: the provider, as support.ai.get_choices lists them.";
            return false;
        }

        request = new SupportAiProviderRequest
        {
            Kind = parsed,
            BaseAddress = Text(arguments, "baseAddress"),
            ApiKey = Text(arguments, "apiKey"),
            Model = Text(arguments, "model")
        };
        problem = null;

        return true;
    }

    private static string Text(JsonElement arguments, string name)
        => arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
}
