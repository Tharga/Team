using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Maps the endpoint Slack posts thread replies to.
/// </summary>
public static class SupportEndpointRegistration
{
    /// <summary>Default path Slack event subscriptions are pointed at.</summary>
    public const string DefaultPath = "/_tharga/support/slack/events";

    /// <summary>
    /// Maps the inbound Slack events endpoint.
    /// </summary>
    /// <remarks>
    /// <b>Public and unauthenticated by design, and that is not an oversight.</b> Slack cannot present a
    /// credential, so the request signature is the credential -- verified in the handler before anything is
    /// read from the body. Putting an authorization policy here would simply stop Slack reaching it.
    /// <para>
    /// <b>The raw body is read before any model binding</b>, because the signature covers the exact bytes
    /// Slack sent. Binding to a type and re-serializing produces different bytes and never verifies, and it
    /// fails looking exactly like a wrong secret.
    /// </para>
    /// <para>
    /// The response is written as plain text: the setup challenge expects its value echoed back verbatim,
    /// not wrapped in JSON.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapThargaSupportSlack(this IEndpointRouteBuilder endpoints, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(path, async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync(context.RequestAborted);

            var handler = context.RequestServices.GetRequiredService<SlackEventHandler>();

            var outcome = await handler.HandleAsync(
                rawBody,
                context.Request.Headers["X-Slack-Request-Timestamp"],
                context.Request.Headers["X-Slack-Signature"],
                context.RequestAborted);

            context.Response.StatusCode = outcome.StatusCode;

            if (outcome.Body != null)
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(outcome.Body, context.RequestAborted);
            }
        }).AllowAnonymous();

        return endpoints;
    }
}
