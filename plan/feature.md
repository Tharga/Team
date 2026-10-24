# Feature: the assistant shows its steps while it works

First PR of the assistant that works in steps (issue to be filed: the semi-automatic assistant). Branch:
`feature/support-assistant-steps`, cut from `feature/support-side-panel` (PR #384, open when this started).

## Goal

While the assistant works, the member sees more than dots: which round of calls to the model it is on, and which
tool it is running. A round is one call to the model; a tool's result going back starts the next one.

## Scope

- `SupportAssistantProgress` (`Tharga.Team`): the answer so far, the round, and the tools running now. It replaces
  `IProgress<string>` on `RunAssistantAsync` and on `ISupportResponder.AnswerAsync`, which are not yet released.
- `ChatSupportResponder` reports a round when it starts, the tools the model asks for, and the next round when their
  results go back, read from the stream the tool loop yields.
- The side panel and the support page show "Round 1", and "Round 2: get_my_cases" while a tool runs, under the
  dots or under the text so far.
- The sample's scripted client asks for `get_my_cases` when a question is about cases, so the counter shows in the
  sample without a model.

## Out of scope (later in the same issue)

- Context in the system prompt, a lower round cap, steps recorded with the answer, usage per answer.
- Actions the member confirms.

## Done when

The counter shows in the sample and with the user's model, the full suite passes, the build stays at 14 warnings,
and the user says it is done.
