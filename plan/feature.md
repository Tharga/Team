# Feature: rate a whole assistant conversation

Issue: Tharga/Team#533.

## Goal

The person an assistant conversation answered can rate the conversation as a whole, up or down, and say why on
thumbs-down. The rating is kept on the case, audited, shown in the feedback view, and handed to the host as a
`SupportConversationFeedback` (with one shared JSON shape) so the host can deliver it, for example to its support inbox.

## Scope

- `Tharga.Team`: `SupportConversationRating`, `SupportCase.ConversationRating`, `SupportCaseChange.ConversationRated`,
  `SupportAssistantCall.Succeeded`, `ISupportCaseService.RateConversationAsync`, `ISupportCaseStore.SetConversationRatingAsync`,
  `ISupportConversationFeedbackSink`, `SupportConversationFeedback` (+ message and call), `SupportConversationFeedbackJson`.
- `Tharga.Team.Support`: the service, the authorization and auditing decorators, the sinks and which ratings reach them
  (`SupportAssistantOptions.ConversationFeedbackRatings`, Down only by default), `ChatSupportResponder` records whether
  each call succeeded.
- `Tharga.Team.MongoDB`: the rating on the case entity, stored by name; `Succeeded` on the call entity.
- `Tharga.Team.Blazor`: `SupportDisplayOptions.RateConversations` (off by default), the prompt in `SupportPanel`,
  `SupportPanelText` keys with English defaults, rated conversations in `SupportAssistantFeedbackView`.
- Docs: `docs/articles/support-cases.md`, the Support and Blazor READMEs.

Not in scope: delivery. The host implements the sink.

## Acceptance

- The author of an assistant conversation can rate it up or down, with an optional comment; nobody else can, and a
  conversation the assistant never answered cannot be rated.
- Rating again replaces the earlier rating; `None` takes it back.
- The rating is stored, audited as `rate-conversation` (never with the comment), announced as
  `SupportCaseChange.ConversationRated`, and shown in the feedback view with its comment.
- Every sink receives one `SupportConversationFeedback` per rating the option lets through. It carries the case id,
  team key, rating, comment, when, the culture, and the messages with each assistant call's name and whether it
  succeeded. Never call arguments, tool results or internal comments. A sink that throws never fails the rating.
- Every new string is a `TextKey` with an English default.

## Done when

All of the above is tested, the full suite passes on net10.0 and net11.0, the docs are updated, and the PR is open.
