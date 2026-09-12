# Plan: a provider-agnostic AI responder for support cases

## Decisions taken before any code

- **Trigger** — the customer chooses human or AI when raising the case, and can hand off to a human mid-case
  keeping the same transcript. What #142 asked for; presence may nudge toward AI when nobody is online, but
  never decides.
- **Queue** — an AI answer clears *awaiting support*; a customer reply re-raises it. Consistent with the
  read-state semantic already shipped: reading is a position, not a flag.
- **Provider** — the host registers an `IChatClient`; the toolkit ships no adapter and references no vendor
  package. `Tharga.Team` gets no AI dependency at all; `Microsoft.Extensions.AI` lands only in
  `Tharga.Team.Support`, which already quarantines MailKit.
- **The AI answers, it does not act.** Its tools are reads. Closing, assigning and reopening stay human.

**This falls out of the existing model rather than changing it:** `LastMessageFromAuthor` already drives
`GetAwaitingSupportCountAsync`. An AI answer is not from the author, so it clears; a customer reply sets it
again, so it re-raises. The chosen semantic needs no store change — verify by test rather than by writing one.

## Steps

- [x] 1. Dependency check. `dotnet outdated` across the solution reports **no outdated dependencies**, so
      there is no `chore(deps)` commit to make on this branch. Re-checked at close-out per the workflow.

- [x] 2. Contracts done, and `Tharga.Team` references no AI package. `SupportAssistance`,
      `SupportAssistantState`, `ISupportResponder`, `SupportAnswer`, `SupportMessageKind.Assistant`,
      `SupportCase.AssistantState`, `ISupportCaseService.RequestHumanAsync` / `RunAssistantAsync`.
      - `SupportMessageKind.Assistant` — appended, and the entity already carries
        `[BsonRepresentation(BsonType.String)]`, so no ordinal hazard and no migration.
      - `SupportAssistantState { None, Active, HandedOff }` on `SupportCase`; absent reads as `None`, which is
        every case that exists today.
      - `ISupportResponder` — one operation, `AnswerAsync(teamKey, caseId, ct)`, returning a record that can
        say *declined* as a first-class answer rather than throwing.
      - `ISupportCaseService.RequestHumanAsync(teamKey, caseId)`, and the assistant choice on the raise path.

- [x] 3. Store and Mongo: persist `AssistantState`, string-represented. `PersistedEnumRepresentationTests`
      already sweeps the assembly, so a slip fails a test rather than a read.

- [x] 4. Authorization. Hand-off is authorized as replying is; the responder writes through
      `ISupportCaseService` and gets no privileged path. A test asserts the responder cannot read a case its
      caller could not — the rule that matters, since an AI with a wider view than its user is a data leak
      wearing a helpful face.

- [x] 5. `ChatSupportResponder` in `Tharga.Team.Support` — the `IChatClient` adapter. Transcript to
      `ChatMessage[]`, tools as `AIFunction`, the answer appended as `Assistant`.

- [x] 6. Tools over case and team state, built with `AIFunctionFactory`, each resolved through the
      scope-checked services so they inherit the caller's authorization rather than re-implementing it.

- [x] 7. Registration that degrades. No `IChatClient` registered means no responder and no behaviour change
      anywhere. Resolve it with `GetService`, never `[Inject]` on a component — that is #266 exactly, where a
      required injection of an optional dependency killed the circuit.

- [x] 8. Blazor: the transcript renders an AI answer as what it is, the raise dialog offers the choice, and a
      case being assisted offers *talk to a human*.

- [x] 9. Sample — the AI responder. The sample takes no vendor package either, so it registers a small
      deterministic `IChatClient` that runs with no key and no network, and the wiring comment says how to
      swap in Ollama, llama.cpp, OpenAI or Anthropic in one line. Same shape as Slack and email in the
      sample: the wiring resolves in the real graph whether or not a provider is configured.

- [x] 10. **Dropped — the premise was wrong, and that is the finding.** Presence, the unread count and the
      awaiting count are all rendered *inside* the components the sample already uses: `SupportCasesView`
      shows presence and unread, `SupportQueueView` shows the awaiting count. Grepping the sample for those
      names found nothing and I read that as a gap; the components own them. The sample therefore already
      had the whole support surface, and the only thing genuinely missing was the assistant. Nothing built
      here, and no redundant sample UI added.

- [x] 11. Full suite green: 2687 passed, 0 failed. A text-coverage guard caught the transcript badge as
      a hardcoded string, so it now resolves `SupportTranscriptText.Assistant` through the provider and the
      component joined the migrated list — the guard doing exactly its job.

- [x] 12. Docs: done on both surfaces — `docs/articles/support-cases.md` gains the responder and the provider-agnostic setup;
      `README.md` gains it under Support. Land as a `docs:` commit.

- [~] 13. Push, hand to the user to test, do not open the PR yet.

- [ ] 14. On confirmation: close-out — re-run `dotnet outdated`, update #142 with what shipped and what
      remains, archive `plan/feature.md`, `git rm -r plan`, final commit, PR.

## Version

**Stays on the 3.21 line — the user's decision, 2026-09-12, and it follows the recorded convention.** A bump
needs a consumer *action*, and everything here is additive for callers: new methods, a new optional parameter
on `RaiseCaseAsync`, a new `SupportMessageKind` member, a new `SupportCase` property, and a store operation
added as a *default* interface member so a host with its own store keeps compiling. `MAJOR_MINOR` in
`build.yml` is untouched, so the merge lands as the next patch.

Recorded so it is not re-litigated: an earlier suggestion in this session that this needed a minor bump was
wrong against the convention, not a judgement the next session should revisit.

**For the release notes:** `ISupportCaseService` gained two methods, so anyone who *implements* it — a test
double, a hand-rolled decorator — has a compile error to fix. Hosts call that interface rather than implement
it, which is why it is not a consumer break, but it is worth naming rather than leaving to be discovered.

## Last session

2026-09-12 — Branch off `master`. Read `architecture-v4.md` first, per `mission.md`: the design is checked
against all six rules in `feature.md` and moves toward the target. Verified the sample already wires Slack,
email and the three case views but renders **none** of presence, unread count or awaiting count. Verified
`SupportMessageKind` is string-represented so appending a member is safe. Confirmed the consumer bot that
prompted this is built on `Microsoft.Extensions.AI` with its vendor SDK in a single registration line — the
design this feature adopts. Steps 2-7 done, with 11 new tests in `SupportAssistantTests`; full suite 2686 green.

**Two design corrections made while building, both worth keeping:**
`ISupportResponder.AnswerAsync` takes the case and its transcript rather than ids — it removed a container
cycle (service -> responder -> tools -> service) and made it structurally impossible for the assistant to read
more than its authorized caller had already read. And `RunAssistantAsync` is its own operation rather than
something raising and replying do silently, because a model taking tens of seconds inside a write would make
reporting a problem feel broken.

**The queue decision needed no store change**, which is worth recording: `LastMessageFromAuthor` already
drives the awaiting count, so an assistant answer clears it and a customer reply re-raises it. Verified by
test rather than assumed.

Steps 8-12 done; docs on both surfaces. Next: push and hand over for testing.
