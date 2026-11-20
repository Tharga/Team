# Plan: rate a whole assistant conversation (Tharga/Team#533)

Branch `feature/support-conversation-rating` from `master` (cf8cd0d).

## Steps

- [x] 0. NuGet: Anthropic 13.10.0 → 13.12.0 (b075bae). Held as documented in their csproj: MailKit 5 (#469), Roslyn 4.8
      in the analyzer; the 11.x Microsoft lines are the per-framework split; Serilog.AspNetCore 11 in the sample is a
      .NET 11 major. Full suite green on both targets: 15 592.
- [x] 1. Contracts in `Tharga.Team`, with serialization and JSON-shape tests. The JSON keeps letters of every script
      readable (`UnicodeRanges.All`) but still escapes markup; a reflection test pins the feedback shapes to their fields.
- [x] 2. Service: `RateConversationAsync`, authorization (author only, assistant conversations only), audit, change
      notice, sinks (Down only by default, a throwing sink logged), in-memory store; `ChatSupportResponder` records
      whether each call succeeded. A comment is trimmed, a blank one is none, and one longer than a message is refused;
      the audit entry carries the verdict, never the comment. A call reads as succeeded when the tool loop's result for
      its call id carries no exception.
- [x] 3. MongoDB: the rating on the case entity by name, `Succeeded` on the call entity, store method, mapping tests.
      Taking a rating back unsets the field, so the case reads as never rated.
- [x] 4. Panel: `RateConversations`, the prompt (thumbs, comment on down, notice, Send, thank-you), text keys, styles
      for light and dark. Shown once the assistant has answered and not while it answers; thumbs-up is sent at once and
      the same again takes it back; thumbs-down opens the comment and notice and sends on Send; a refused rating keeps
      what was typed and says so. Styles use the Radzen theme variables only.
- [x] 5. Feedback view: rated conversations with rating and comment, newest rated first. Listed when
      `RateConversations` is on or anything is rated, so a host without it sees no empty section; a comment is marked
      on the card and shown above the conversation when it is opened.
- [x] 6. Docs: `docs/articles/support-cases.md` (new section *How the whole conversation was rated*, with a sink
      example), Support README, Blazor README.
- [~] 7. Close-out: NuGet re-check, full suite, archive and remove `plan/`, ask to push and open the PR.

## Decisions

- The rating reuses `SupportAnswerRating` (Up/Down/None) so hosts have one verdict type; `None` takes it back.
- Calls in the feedback carry only the name and whether it succeeded: `SupportAssistantCall.Arguments` can hold the
  person's words, and results can hold data they did not choose to send.
- `Succeeded` is nullable on `SupportAssistantCall`: answers stored before it, and responders that do not say, read as
  unknown rather than as a success.
- Sinks are an `IEnumerable`, like `ISupportAssistantUsageSink`: none registered is the no-op default.

## Last session

2026-11-20 — branch created, NuGet step done, plan written.
