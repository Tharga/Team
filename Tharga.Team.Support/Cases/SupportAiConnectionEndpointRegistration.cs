using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Maps a team's AI connection as HTTP endpoints: everything <see cref="ISupportAiConnectionService"/> does, for a REST
/// API that offers what the UI offers.
/// </summary>
public static class SupportAiConnectionEndpointRegistration
{
    /// <summary>Default path the endpoints are mapped under.</summary>
    public const string DefaultPath = "/api/support/assistant/connection";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// Maps the AI connection endpoints under <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Route</term><description>Does</description></listheader>
    /// <item><term><c>GET {path}</c></term><description>The connection, without the key.</description></item>
    /// <item><term><c>GET {path}/choices</c></term><description>What the team may choose.</description></item>
    /// <item><term><c>POST {path}/models</c></term><description>The models a provider offers, for the key in the body or the stored one.</description></item>
    /// <item><term><c>POST {path}/test</c></term><description>Asks the model one short question; saves nothing.</description></item>
    /// <item><term><c>PUT {path}</c></term><description>Connects to a provider. The key is accepted here and never returned.</description></item>
    /// <item><term><c>PUT {path}/platform</c></term><description>Uses the platform's model.</description></item>
    /// <item><term><c>PUT {path}/off</c></term><description>Turns the assistant off for the team.</description></item>
    /// </list>
    /// <para>
    /// <b>The team comes from the credential, never the request</b>, as on the audit API: a team API key's own team, or
    /// the team a system key names in <c>X-Team-Key</c>. A caller bound to no team is a 400.
    /// </para>
    /// <para>
    /// <b>No authorization here.</b> The service requires <c>assistant:manage</c> on the team and audits every change, so
    /// the endpoints inherit both; a refusal is a 403. They require the toolkit's API policy
    /// (<see cref="ApiKeyConstants.ThargaApiPolicyName"/>, registered by <c>AddThargaControllers</c>), so API keys reach
    /// them as they reach the audit API.
    /// </para>
    /// <para>
    /// Kinds are written by name (<c>"OpenAI"</c>), and read by name or number. Listing models, testing and connecting
    /// answer 200 when they worked and 422 when they did not, with why in the body; nothing in any answer carries the
    /// key, not even a provider's error that echoed it.
    /// </para>
    /// </remarks>
    /// <returns>The group, for a host to add its own conventions to, such as a tag for OpenAPI.</returns>
    public static RouteGroupBuilder MapThargaSupportAiConnection(this IEndpointRouteBuilder endpoints, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(path).RequireAuthorization(ApiKeyConstants.ThargaApiPolicyName);

        // Every handler takes the service, not only the HttpContext: a handler taking only an HttpContext binds as a
        // plain request delegate, and the result it returns is never written.

        group.MapGet("", (HttpContext context, ISupportAiConnectionService service) => RunAsync(context, async (teamKey, cancellationToken)
            => Results.Json(await service.GetConnectionAsync(teamKey, cancellationToken), Json)));

        group.MapGet("/choices", (HttpContext context, ISupportAiConnectionService service) => RunAsync(context, async (teamKey, cancellationToken)
            => Results.Json(await service.GetChoicesAsync(teamKey, cancellationToken), Json)));

        group.MapPost("/models", (HttpContext context, ISupportAiConnectionService service) => RunWithRequestAsync(context, async (teamKey, request, cancellationToken) =>
        {
            var list = await service.ListModelsAsync(teamKey, request, cancellationToken);
            return Results.Json(list, Json, statusCode: list.Problem == null ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
        }));

        group.MapPost("/test", (HttpContext context, ISupportAiConnectionService service) => RunWithRequestAsync(context, async (teamKey, request, cancellationToken)
            => Outcome(await service.TestAsync(teamKey, request, cancellationToken))));

        group.MapPut("", (HttpContext context, ISupportAiConnectionService service) => RunWithRequestAsync(context, async (teamKey, request, cancellationToken)
            => Outcome(await service.ConnectAsync(teamKey, request, cancellationToken))));

        group.MapPut("/platform", (HttpContext context, ISupportAiConnectionService service) => RunAsync(context, async (teamKey, cancellationToken) =>
        {
            try
            {
                await service.UsePlatformModelAsync(teamKey, cancellationToken);
                return Results.NoContent();
            }
            catch (InvalidOperationException)
            {
                return Results.Json(SupportAiConnectionResult.Failure("This site has no platform model to answer with."), Json, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }));

        group.MapPut("/off", (HttpContext context, ISupportAiConnectionService service) => RunAsync(context, async (teamKey, cancellationToken) =>
        {
            await service.TurnOffAsync(teamKey, cancellationToken);
            return Results.NoContent();
        }));

        return group;
    }

    private static IResult Outcome(SupportAiConnectionResult result)
        => Results.Json(result, Json, statusCode: result.Succeeded ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);

    /// <summary>Runs an operation for the caller's team, answering 400 when the caller is bound to none and 403 when the service refuses.</summary>
    private static async Task<IResult> RunAsync(HttpContext context, Func<string, CancellationToken, Task<IResult>> operation)
    {
        // The team the caller acts on: its own for a team key, or the one a system key named in the header, which
        // TeamContextMiddleware put here as a claim. Never a parameter beside it, which could disagree.
        var teamKey = context.User.FindFirst(TeamClaimTypes.TeamKey)?.Value;
        if (string.IsNullOrEmpty(teamKey))
            return Results.Problem("This needs a team: a team API key, or a system key naming one in X-Team-Key.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            return await operation(teamKey, context.RequestAborted);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    /// <summary>As <see cref="RunAsync"/>, with the provider request read from the body; a body that is not one is a 400.</summary>
    private static Task<IResult> RunWithRequestAsync(HttpContext context, Func<string, SupportAiProviderRequest, CancellationToken, Task<IResult>> operation)
        => RunAsync(context, async (teamKey, cancellationToken) =>
        {
            SupportAiProviderRequest request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SupportAiProviderRequest>(Json, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Not the exception's text: it can quote the body, and the body holds the key.
                return Results.Problem("The body must be a provider request in JSON.", statusCode: StatusCodes.Status400BadRequest);
            }

            return await operation(teamKey, request, cancellationToken);
        });
}
