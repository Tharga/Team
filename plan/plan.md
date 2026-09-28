# Plan: host-seams

Branch: `feature/host-seams` (from `master` at a155b64). Scope: `plan/feature.md`.

## Steps

- [x] 0. NuGet updates. `dotnet outdated` on the whole solution: **none available** (2026-09-28), so there
      is no `chore(deps)` commit.
- [x] 1. Baseline: build clean, **3110 tests passed**.
- [x] 2. #293: add `GetCasesAwaitingSupportSinceAsync` / `GetCasesWithUnreadAnswerSinceAsync` to
      `ISupportCaseStore` (default: empty), with XML docs covering host-side dedup on `(Id, MessageCount)`.
- [x] 3. #293: implement both in `MongoSupportCaseStore`, with the tail check extracted as an internal static
      predicate. Add predicate tests in `Tharga.Team.MongoDB.Tests`.
- [x] 4. (Changed, see notes.) #293: implement both in `InMemorySupportCaseStore`. Add behaviour tests (window boundary, limit,
      system entry excluded, assistant answer included, read marker, unassigned case, closed case excluded,
      default-member store returns empty).
- [x] 5. (Done, 3128 tests. `SmtpTeamEmailSender` deliberately unchanged, see notes.) #289: `TeamInviteMail` record plus a default member on `ITeamEmailSender`. `SmtpTeamEmailSender`
      implements it. `TeamComponent` passes the team key. Tests for forwarding and for the SMTP sender.
- [x] 6. (Done: `HostProviderContextSurfaceTests`, which checks visibility by reflection because the test assembly sees internals.) MCP: make `McpContextExtensions` / `AsTeamContext` public and document the `TeamMcpContext`
      constructor. Add a test that a host-built context narrows and a foreign one yields null.
- [x] 7. Bump `MAJOR_MINOR` to 3.24 in `.github/workflows/build.yml`.
- [x] 8. Full build and test run: **3132 passed, 0 failed** (baseline 3110, +22). Three milestone commits.
- [~] 9. Push the branch and ask the user to test. **No PR yet.**

## Last session (2026-09-28)
All three items are implemented and tested on `feature/host-seams`, and the branch is pushed for testing.
Next: user testing, then close-out (below). **README/docs changes needed at close-out:**
`docs/articles/implementation-guide.md`: the custom-sender example (~line 1712) should use
`SendInviteAsync(TeamInviteMail)`; add an MCP section on reading the caller in a host provider and building
`TeamMcpContext` in tests; add a support section on the two escalation queries, including dedup on
`(Id, MessageCount)` and newest-first ordering. Release notes: plan 05 should record removing the
four-string `SendInviteAsync` in 4.0.

## Close-out (after user confirms)
Re-run `dotnet outdated`. Update docs (`implementation-guide.md`: custom sender, MCP host providers,
support escalation queries). Mark the Requests.md MCP entry done. Comment on and close #289 / #293.
Archive `feature.md` to `done/host-seams.md`, `git rm -r plan`, `feat: host-seams complete`, then open the PR.

## Notes / decisions
- Assistant answers count as answers for the unread-answer query; system entries never do.
- Unassigned cases are included in both queries.
- The old four-string `SendInviteAsync` stays abstract until v4, which avoids two default members that call
  each other.
- **Newest first** (by `LastMessageAt`) in both queries. Neither query changes the state it reads, so in
  arbitrary order a limited sweep could return the same already-escalated cases every pass and starve the
  rest. This is documented on the port.
- Step 4 changed: the in-memory fake is **not** extended. Tests of it would only test the fake. The adapter
  is tested instead: `SupportEscalationQueryTests` (rendered filter, ordering, limit, predicate, over a
  substituted collection) and `HasUnreadAnswerTests`. `EscalationQueryDefaultTests` pins the empty default.
  The suite is at 3124 after steps 2–4.
- Step 5: `SmtpTeamEmailSender` is **not** changed. It has no use for the key, and the default member
  already forwards to it. Tests: `TeamInviteMailTests` (forwarding, and an override receiving the key) and
  `InviteMailCallSiteTests`, a source scan that every `SendInviteAsync` call in the components passes a
  `new TeamInviteMail`. The old overload still compiles, so reverting to it would silently drop the key.
