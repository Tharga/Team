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

- [x] **3. Presence.** Done 2026-09-02 — `ISupportPresence` + `SupportPresenceState` in `Tharga.Team`,
      `SlackSupportPresence` in `Support/Cases`, and two new `ISlackClient` calls. Suite **2275 passed,
      0 failed** (+10), warnings back to 34.
      **Who counts as support: members of the configured channel** (user, 2026-09-02). Adding somebody to the
      channel is how they become support, so there is no second list to drift — chosen over a configured id
      list, and over a Slack user group, which is tidier but a paid-plan feature.
      **The contract lives in `Tharga.Team`**, like `ISupportCaseService`, so a component can reach it without
      `Tharga.Team.Blazor` depending on the package that carries MailKit.
      **The plan said "shared across instances". That was wrong, and the distinction is worth keeping.** A
      process-local `ISupportEventLedger` would be a *correctness* defect — two instances would both accept
      the same retry. A process-local presence cache only costs N times the API calls, still bounded by the
      TTL, and a stale answer is already handled because presence is advisory. Sharing it would need a
      backplane that does not exist, to save calls that are not a problem.
      **Two TTLs, because the two questions change at different rates:** the channel roster for ten minutes,
      presence for sixty seconds. One TTL would either hammer `conversations.members` or answer from a stale
      roster.
      **Three ways it refuses to say "away" when it means "cannot tell":** an unreadable channel keeps the
      previous roster rather than concluding support is empty; all-unknown presence stays unknown; and a
      throwing transport is unknown, not an error. Each has a test, because that is the failure that reaches a
      customer.
      A semaphore collapses concurrent refreshes, so a page rendering for twenty people asks Slack once, and
      the loop stops at the first active member — one is the whole answer.

- [x] **4. The manifest and the setup path.** Done 2026-09-02 — `slack-app-manifest.json` at the repository
      root, the sample reading `Slack:CaseUrl`, and the docs' setup section rewritten around the manifest.
      **The four scopes were derived from the call sites, not from memory:** `chat.postMessage`,
      `conversations.members` and `users.getPresence` are the only Slack endpoints the code touches, giving
      `chat:write`, `users:read`, `channels:read` and `groups:read` — the last two because a private support
      channel needs the other one. Documented as a table naming which call needs which, since **removing a
      scope makes the feature that needs it fail quietly**: the client reports failures rather than throwing,
      by design.
      **The docs now say plainly that there is no Tharga app to install and cannot be**, with the reasoning,
      because that is the question a consumer asks first and the answer is not obvious.
      **One criterion is NOT met and needs the user.** "Verified by following it, not by reading it" requires
      creating an app from the manifest in a real workspace, which cannot be done from here. The manifest's
      *shape* is right and its scopes are derived from the code; whether Slack accepts the document verbatim
      is unverified. **Ask before treating this step as finished.**

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
