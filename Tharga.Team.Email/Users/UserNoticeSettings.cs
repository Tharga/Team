namespace Tharga.Team.Email.Users;

/// <summary>What is in force for a notice: the user's choice, else the team's default, else the catalogue's, each part on its own.</summary>
internal static class UserNoticeSettings
{
    /// <summary>The team's default for a notice: what the team set, else the catalogue's; a required notice is always on.</summary>
    public static (bool Enabled, UserNoticeDelivery Delivery) TeamDefault(UserNoticeType notice, UserNoticeSetting team)
        => (notice.Required || (team?.Enabled ?? notice.OnByDefault), Allowed(notice, team?.Delivery) ?? notice.DefaultDelivery);

    /// <summary>What reaches a user for an occurrence in a team: their choice, else that team's default, else the catalogue's; a required notice is always on.</summary>
    public static (bool Enabled, UserNoticeDelivery Delivery) Effective(UserNoticeType notice, UserNoticeSetting user, UserNoticeSetting team)
    {
        var fallback = TeamDefault(notice, team);
        return (notice.Required || (user?.Enabled ?? fallback.Enabled), Allowed(notice, user?.Delivery) ?? fallback.Delivery);
    }

    /// <summary>Refuses a change the notice does not allow: a delivery it does not offer, or turning a required notice off.</summary>
    /// <exception cref="ArgumentException">The notice cannot be delivered <paramref name="delivery"/>, or is required and <paramref name="enabled"/> is false.</exception>
    public static void RequireAllowed(UserNoticeType notice, bool enabled, UserNoticeDelivery delivery)
    {
        if (!notice.Allows(delivery)) throw new ArgumentException($"The notice '{notice.Key}' cannot be delivered {delivery}.", nameof(delivery));
        if (notice.Required && !enabled) throw new ArgumentException($"The notice '{notice.Key}' is required and cannot be turned off.", nameof(enabled));
    }

    /// <summary>The delivery when the notice still allows it, otherwise null so the next default applies.</summary>
    public static UserNoticeDelivery? Allowed(UserNoticeType notice, UserNoticeDelivery? delivery)
        => delivery is { } value && notice.Allows(value) ? value : null;

    /// <summary>What a user sees for a notice: their choice, else the default their eligible teams agree on, else the catalogue's with the difference flagged.</summary>
    public static UserNoticeChoice Choice(UserNoticeType notice, UserMailCategory category, UserNoticeSetting user, IReadOnlyCollection<UserNoticeSetting> teamDefaults, bool categoryReceives)
    {
        var defaults = InForce(notice, teamDefaults);
        var enabledDefaults = defaults.Select(x => x.Enabled).Distinct().ToArray();
        var deliveryDefaults = defaults.Select(x => x.Delivery).Distinct().ToArray();
        var chosenDelivery = Allowed(notice, user?.Delivery);

        return new UserNoticeChoice
        {
            Notice = notice,
            Category = category,
            Enabled = notice.Required || (user?.Enabled ?? (enabledDefaults.Length == 1 ? enabledDefaults[0] : notice.OnByDefault)),
            Delivery = chosenDelivery ?? (deliveryDefaults.Length == 1 ? deliveryDefaults[0] : notice.DefaultDelivery),
            EnabledChosen = !notice.Required && user?.Enabled != null,
            DeliveryChosen = chosenDelivery != null,
            VariesByTeam = (user?.Enabled == null && enabledDefaults.Length > 1) || (chosenDelivery == null && deliveryDefaults.Length > 1),
            CategoryReceives = categoryReceives
        };
    }

    /// <summary>The part of a user's choice worth storing: each part only where some default in force for them differs from it.</summary>
    public static UserNoticeSetting Deviation(UserNoticeType notice, bool enabled, UserNoticeDelivery delivery, IReadOnlyCollection<UserNoticeSetting> teamDefaults)
    {
        var defaults = InForce(notice, teamDefaults);

        return new UserNoticeSetting
        {
            Enabled = notice.Required || defaults.All(x => x.Enabled == enabled) ? null : enabled,
            Delivery = defaults.All(x => x.Delivery == delivery) ? null : delivery
        };
    }

    private static (bool Enabled, UserNoticeDelivery Delivery)[] InForce(UserNoticeType notice, IReadOnlyCollection<UserNoticeSetting> teamDefaults)
        => teamDefaults == null || teamDefaults.Count == 0 ? [TeamDefault(notice, null)] : [.. teamDefaults.Select(x => TeamDefault(notice, x))];

    /// <summary>The part of a team's default worth storing: each part only where it differs from the catalogue's.</summary>
    public static UserNoticeSetting TeamDeviation(UserNoticeType notice, bool enabled, UserNoticeDelivery delivery) => new()
    {
        Enabled = notice.Required || enabled == notice.OnByDefault ? null : enabled,
        Delivery = delivery == notice.DefaultDelivery ? null : delivery
    };
}
