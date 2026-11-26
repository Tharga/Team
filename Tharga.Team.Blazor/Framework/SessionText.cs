namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>SessionWatch</c> when it asks whether the person is still there, before an idle sign-out.</summary>
public static class SessionText
{
    /// <summary>Heading of the dialog.</summary>
    public static readonly TextKey Title = new("team.session.idle.title", "Are you still there?");

    /// <summary>Explains why the dialog is shown.</summary>
    public static readonly TextKey Body = new("team.session.idle.body", "You have not been active for a while. For your security you will be signed out soon.");

    /// <summary>The countdown; <c>{0}</c> is the time left, as minutes and seconds.</summary>
    public static readonly TextKey Countdown = new("team.session.idle.countdown", "Signing out in {0}");

    /// <summary>The button that keeps the person signed in.</summary>
    public static readonly TextKey Stay = new("team.session.idle.stay", "Yes, stay signed in");

    /// <summary>The button that signs the person out now.</summary>
    public static readonly TextKey SignOut = new("team.session.idle.signout", "Sign out");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Title, Body, Countdown, Stay, SignOut];
}
