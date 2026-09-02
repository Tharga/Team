# Plan: Slack support — link, presence, and an installable app

Feature scope in `plan/feature.md`. Spec:
`$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/11-slack-support-polish.md`.

## Steps

- [x] **0. Package updates and baseline.** `dotnet outdated`: only the held xunit 4.0 /
      Microsoft.Testing.Platform pair, its own PR. **Nothing to apply.** Baseline on this branch: build
      0 errors, **2258 passed, 0 failed**.

- [~] **1. The case link.** A `CaseUrlTemplate` setting plus a `{case.url}` placeholder.
      The case id needs no work — `NotificationRouter` already falls through to audit metadata, and
      `support.case.id` is written by the auditing decorator. So this is the template substitution and the
      one thing the toolkit cannot know: the host's route.
      Renders **empty rather than broken** when unset, so a message with a link in its template is still
      readable on a host that has not configured one.
      Decide where the setting lives: `NotificationOptions` (it is a routing concern) rather than
      `SupportCaseOptions` (which is about the cases themselves) — the router is what renders it.

- [ ] **2. A default route for a raised case.** So "notify me when somebody asks for help" needs no
      hand-written route.
      **Extend `EveryBuiltInRoute_NamesAnEventTheToolkitEmits`, and verify the list rather than appending to
      it.** The guard holds a hard-coded set of emitted events; a default naming something nothing raises
      looks configured and does nothing, which is the failure that test exists to prevent.
      Note `support:raise` is emitted only when `AddThargaSupportCases` is registered — a notifications-only
      host simply never triggers it, which is the same harmless shape as a route for an event that has not
      happened yet.

- [ ] **3. Presence.** `users.getPresence` on `ISlackClient`, needing the `users:read` scope.
      **Cached, and the cache shared across instances** — Slack rate-limits this endpoint and calling it per
      render is a documented way to get a deployment throttled. `ITeamCache` is deliberately not reused: it is
      purpose-built for three named claims lookups and has no general key/value surface, exactly as
      `ISupportEventLedger` records.
      **Advisory, never a gate.** Unknown renders as nothing rather than "offline", because telling a customer
      not to bother when support is there is worse than saying nothing. Never on the path that raises a case.

- [ ] **4. The manifest.** A `slack-app-manifest.json` (or yml) in the repository, declaring exactly the
      scopes these features need — `chat:write`, `users:read`, and the event subscriptions the inbound
      endpoint expects.
      **Verified by following it, not by reading it**: create an app from it and check the resulting scopes
      match what the code requires, and record the result.

- [ ] **5. Tests for every acceptance criterion**, including the two that are about restraint: an unset URL
      template leaves the message intact, and unknown presence renders as nothing.

- [ ] **6. Sample.** Configure it from the manifest, and show presence somewhere honest — next to the support
      form, where it changes what a customer expects rather than decorating a dashboard.

- [ ] **7. Docs** (own `docs:` commit): the link placeholder and the URL template in
      `docs/articles/notifications.md`; presence and the manifest in `docs/articles/support-cases.md`,
      including that Slack tokens are per workspace and why there is no shared Tharga app to install.

- [ ] **8. Close-out** (only when the user says it is done): re-run `dotnet outdated`; comment on #142; move
      spec 11 to `../done/`; update `planned/README.md`; `git rm -r plan`; final commit
      `feat: slack support polish complete`. **`MAJOR_MINOR` is already 3.17** from #245 — check rather than
      assume when this rebases.

## Last session

**2026-09-02.** Branch stacked on `feature/support-case-lifecycle` because both touch
`SupportRegistration.cs` and #245 is not merged yet. Step 0 done: no package updates to apply, baseline green
at 2258.

**Investigation before planning changed two steps.** The case id already reaches a notification template
through audit metadata, so step 1 is only about the URL; and no support event is in the default routes, so
step 2 exists at all. Both were assumptions in the spec worth checking before writing code against them.
