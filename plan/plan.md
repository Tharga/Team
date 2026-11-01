# Plan: user notice composer and minimum interval (#423)

Branch `feature/user-notice-composer`.

## Steps
- [x] 1. Read the issue, the notice types, the distributor, the mail composer, the Mongo outbox and the fixtures; one question put to the user (digest) and answered.
- [x] 2. Core types: `UserNoticeType.MinimumInterval`, `Composer` and `IsStateful`; `UserNoticeComposition`, `UserNoticeWaiting`, `UserNoticeCoverage`, `UserNoticeComposed`; `UserNoticeItem.CoveredSubjects`; `IUserNoticeOutbox.GetCoverageAsync`, `RecordCoverageAsync` and `coveredSubjects` on `CompleteItemsAsync`.
- [x] 3. Tests first (`UserNoticeStateTests`, 17): validation, the interval holds and releases, per user across teams, the composer's mail and what it is given, covered subjects on items and in coverage, merged coverage with earlier times kept, nothing returned sends nothing and starts no interval, a throwing composer keeps the items, user choice honoured, the flush-time check, a plain notice unchanged.
- [x] 4. Registry validation (at once only, positive interval) and the distributor (due time at distribution, hold and composer at flush, coverage after a real mail).
- [x] 5. `UserNoticeMail.ComposeFrom`.
- [x] 6. In-memory outbox (tests) and the Mongo outbox: coverage on the stream document (newest 1 000 subjects), covered subjects on the item; 6 mapping tests.
- [x] 7. Docs: `user-notices.md` (the two settings, the composer contract, an example, the custom-outbox upgrade note).
- [x] 8. Full suite green (Email 1,002; MongoDB 492; Service 1,543; Blazor 2,383; Support 912; Mcp 121; Entra 58; Anthropic 17; OpenAI 16; Images 11); `dotnet outdated` clean.
- [ ] 9. Close-out, only when the user says to push: archive this plan to `$DOC_ROOT/Tharga/plans/Toolkit/Platform/done/user-notice-composer.md`; `git rm -r plan`; final commit `feat: user-notice-composer complete`; push; PR with release-note text; then close #423.

## Notes
- Not exercised against a real MongoDB or a real mail server; the Mongo adapter is tested by rendering its updates against substituted collections, as the other notice adapters are, and the behaviour through the in-memory outbox.
- A takeover of a begun mail was not given a test of its own for a composed notice; the existing takeover tests cover the shared path, and a composed mail takes the same queue key.
- Possible follow-ups: the daily summary for such a notice (decided out of scope); the user's language for the composer.

## Last session
2026-11-01: steps 1-8 done and committed on the branch, not pushed. Next: the user decides on pushing, then step 9.
