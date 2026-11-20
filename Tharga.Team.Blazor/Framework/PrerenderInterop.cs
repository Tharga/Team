namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Recognises the failure of a JavaScript interop call made while the page is still being statically rendered (Tharga/Team#516).
/// </summary>
/// <remarks>
/// <b>Not a fault, and told apart from one.</b> With prerendering on, the first GET of an interactive page runs the component code on the server
/// with no browser behind it, so reading local storage or running a script throws. The interactive render that follows does the same work with
/// a browser, so whoever hit this skips the step and lets that render settle it. Anything else that goes wrong reading storage is still a fault
/// and must be reported as one, which is why this matches the framework's message rather than every <see cref="InvalidOperationException"/>.
/// <para>
/// Server and WebAssembly runtimes throw the same type with the same opening sentence, so one check covers both.
/// </para>
/// </remarks>
internal static class PrerenderInterop
{
    private const string Opening = "JavaScript interop calls cannot be issued at this time";

    /// <summary>Whether <paramref name="exception"/> says JavaScript interop is not available yet because the page is statically rendered.</summary>
    public static bool IsUnavailable(Exception exception)
        => exception is InvalidOperationException && exception.Message.StartsWith(Opening, StringComparison.Ordinal)
           || exception?.InnerException is { } inner && IsUnavailable(inner);
}
