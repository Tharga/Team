# Feature: the support assistant's chat client, chosen per team

GitHub: #377 (filed for SpartaBok). Branch: `feature/support-chat-client-per-team`.

## Goal

A host can give each team its own chat model, or none, without replacing the whole responder. The assistant
is offered only to a team that has one, and the toolkit can say why it is not offered.

## Scope

- **`ISupportChatClientResolver`** (`Tharga.Team.Support`): `GetAsync(teamKey, cancellationToken)` returns the
  team's `IChatClient`, or null when the team has none. The default returns the registered `IChatClient`, so
  an existing host changes nothing. `ChatSupportResponder` asks it on every answer instead of holding one
  client; instructions, transcript limit, timeout and tools stay as they are.
- **Registration:** the responder is registered when the host registered an `IChatClient` *or* its own
  resolver. With neither, nothing registers, exactly as today.
- **Readiness per team**, modelled on FortDocs' `AssistantReadiness`: `SupportAssistantReadiness`
  (`Tharga.Team`) says whether the assistant can answer for a team and, if not, why: no assistant in this
  deployment, or none set up for this team.
  - `ISupportResponder.GetReadinessAsync(teamKey)`, a default member answering ready, so a host's own
    responder keeps compiling and behaving as before.
  - `ISupportCaseService.GetAssistantReadinessAsync(teamKey)`: authorized by membership, like the unread
    count; not audited, like the unread count.
- **The service honours it:** `RaiseCaseAsync` gives a case to the assistant only when the team is ready, so
  a team without a client never gets a case waiting on an assistant that will only decline.
- **The view honours it:** `SupportCasesView` offers the assistant from the team's readiness instead of from
  whether a responder is registered. `Offered` still narrows it.
- **A decline is not silent.** A team whose client disappears after a case started gets a decline, as when a
  provider is down. `SupportCasesView` then shows a short notice offering a person instead of nothing, as
  FortDocs' panel does (`AssistantPanel.razor:471`, "silence would read as nobody caring"). The notice is on
  screen only and is not stored on the case.

## Out of scope

- Choosing and storing each team's connection (the platform's model, Neurolito or Anthropic), the settings component, and usage tracking: #381, which builds on this.
- The side panel (#376), which will consume the readiness added here.

## Acceptance criteria

- With only an `IChatClient` registered, behaviour is unchanged: every team is ready and uses that client.
- With a host resolver, each team's case is answered by that team's client, and a team for which it returns
  null is `NotConfigured`: it is not offered the assistant, and a case raised asking for one goes to people.
- With neither registered, readiness says there is no assistant, and nothing else changes.
- `GetAssistantReadinessAsync` refuses a caller who is not a member of the team.
- When the assistant does not answer, `SupportCasesView` says so and offers a person.
- The decorators forward the new member (`DecoratorDefaultMemberTests`).
- Docs: the assistant section of `docs/articles/support-cases.md` and the `Tharga.Team.Support` README.

## Done when

All criteria hold, the full test suite passes, the build stays at the warning baseline, and the user has
tested from the pushed branch and said it is done.
