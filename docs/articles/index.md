# Articles

Feature guides for **Tharga.Team**. If you're new, start with [Getting started](getting-started.md) for a runnable single-call setup, then move to the full [Implementation guide](implementation-guide.md) when you need the details.

- **[Getting started](getting-started.md)** — install the packages and register everything with one `AddThargaTeam` call
- **[Access simulation](access-simulation.md)** — let an administrator view the app as a less privileged user, so setting someone's access can be checked rather than guessed
- **[Team access requests](access-requests.md)** — ask a team for access at a level for a stated time; its managers approve, and the grant expires back to what the team consented to before
- **[Slack notifications](notifications.md)** — route audited events to Slack channels, worded per event (optional `Tharga.Team.Support` package)
- **[Support cases](support-cases.md)** — cases raised on the site or by mail, from several mailboxes, with files and formatting both ways, answered from the back office or Slack
- **[Mail to users](user-mail.md)** — categories each user chooses from, unsubscribe links, a persisted queue, each user's mail history and bounces (optional `Tharga.Team.Email` package)
- **[Messages to users](user-messages.md)** — your staff write to chosen users of chosen teams, and see what became of each mail
- **[Messages to contacts](contact-messages.md)** — your staff write to people who are not users, listed from your own records, with an opt-out per address that holds for good
- **[Automatic notices](user-notices.md)** — mail raised by events, at once or in a daily summary, chosen per user over each team's default
- **[Implementation guide](implementation-guide.md)** — the complete reference: UI foundation, authentication, API controllers, team management, API-key authentication, scopes, tenant roles, system scopes & consent, and audit logging — both the single-call setup and the step-by-step alternative

The full type-level reference is in the [API](xref:Tharga.Team) tab.
