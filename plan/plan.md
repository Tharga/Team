# Plan: Slack support — link, presence, and an installable app

Feature scope in `plan/feature.md`. Spec:
`$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/11-slack-support-polish.md`.

## Steps

- [x] **0. Package updates and baseline.** `dotnet outdated`: only the held xunit 4.0 /
      Microsoft.Testing.Platform pair, its own PR. **Nothing to apply.** Baseline on this branch: build
      0 errors, **2258 passed, 0 failed**.

- [x] **1 and 2. The case link and its default route.** Done together 2026-09-02 —
      `NotificationOptions.CaseUrlTemplate`, a `{case.url}` placeholder, and a built-in `support:raise` route.
      Suite **2265 passed, 0 failed** (+7), warnings 34 against the limit of 35.
      **The setting lives on `NotificationOptions`, not `SupportCaseOptions`** — the router renders it, and it
      is a routing concern rather than something about the cases themselves.
      **A template, not a base address.** The toolkit has no convention for where a case is shown, so a base
      address would produce a working link only for hosts whose routing matched a guess. `{caseId}` in a
      host-written template puts the knowledge where it exists — and the placeholder is matched
      case-insensitively, because `{caseid}` is what somebody types.
      **Empty on either half missing**, and both directions are tested: no template configured leaves the
      rest of the message intact, and an entry that is not about a case emits no link — so a team event
      borrowing the same wording cannot link to a case that does not exist.
      **The default route ships with the link in its wording**, which is safe precisely because an unset
      template renders nothing rather than `http://localhost/support/`.
      **The guard's emitted list was verified, not appended to.** It was missing all four support events;
      `support:raise`, `reply`, `close` and `reopen` are emitted by `AuditingSupportCaseServiceDecorator` and
      are now listed with a note saying where they come from.
      **One coupling accepted deliberately:** `NotificationRouter` now references
      `SupportAuditMetadataKeys.CaseId` from the `Cases` namespace. Duplicating the key string would leave two
      places to keep in step, and a router that renders a *case* URL necessarily knows about cases.

- [~] **3. Presence.** `users.getPresence` on `ISlackClient`, needing the `users:read` scope.
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
