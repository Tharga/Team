namespace Tharga.Team.Blazor.Features.Authentication;

/// <summary>
/// Options for configuring Tharga authentication registration.
/// </summary>
public class ThargaAuthOptions
{
    /// <summary>
    /// Path for the login endpoint. Defaults to "/login".
    /// </summary>
    public string LoginPath { get; set; } = "/login";

    /// <summary>
    /// Path for the logout endpoint. Defaults to "/logout".
    /// </summary>
    public string LogoutPath { get; set; } = "/logout";

    /// <summary>
    /// When true, validates that the AzureAd configuration section exists at startup.
    /// Defaults to true.
    /// </summary>
    public bool ValidateConfiguration { get; set; } = true;

    /// <summary>
    /// Whether signing out also ends the session at the identity provider. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <b>Setting this to <c>false</c> restores a sign-out that only clears the local cookie</b>, leaving the
    /// provider session alive so the next sign-in silently returns the same user with no prompt. That was the
    /// behaviour before 3.19 and it is a security defect, not a preference — the escape hatch exists only for
    /// a host that cannot yet register the post-logout redirect URI and would rather choose the old behaviour
    /// knowingly than have sign-out land somewhere unexpected.
    /// <para>
    /// <b>Leaving it on requires one thing of the host:</b> the OpenID Connect handler's
    /// <c>SignedOutCallbackPath</c> — <c>/signout-callback-oidc</c> unless you changed it — must be
    /// registered as a post-logout redirect URI on the app registration. Without it the provider still signs
    /// the user out, but shows its own page instead of returning to the application.
    /// </para>
    /// </remarks>
    public bool FederatedSignOut { get; set; } = true;

    /// <summary>
    /// Whether the sign-in request asks the identity provider for an account picker
    /// (<c>prompt=select_account</c>). Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <b>Off by default because it is not what fixes sign-out.</b> With
    /// <see cref="FederatedSignOut"/> on, signing out ends the provider session and the next sign-in prompts
    /// anyway. This option only matters in the narrower case where the browser holds a live single-sign-on
    /// session from somewhere else entirely, and turning it on costs every user a click on every sign-in.
    /// <para>
    /// Turn it on where signing in as a different person is a routine act rather than an exception — a shared
    /// machine, or support staff who hold more than one account.
    /// </para>
    /// </remarks>
    public bool PromptForAccount { get; set; } = false;
}
