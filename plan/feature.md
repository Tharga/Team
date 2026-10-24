# Feature: a support side panel beside every page (#376)

GitHub: #376 (filed for SpartaBok). Branch: `feature/support-side-panel`, from `feature/support-ai-connection`
(PR #383). Its PR follows once #383 merges. Modelled on FortDocs' `AssistantPanel`
(`Eplicta.FortDocs.Web/Components/Layout/AssistantPanel.razor`, `wwwroot/assistant.js`), so hosts stop building
their own.

## Goal

A host places one component in its layout and gets the support conversation, and the assistant where the team has
one, beside every page.

## Scope

- **`SupportPanel`** in `Tharga.Team.Blazor`, rendered as a right-hand `RadzenSidebar` inside the host's
  `RadzenLayout`.
  - **Collapsed:** a narrow rail with one button per channel offered (assistant, support), and a button to the
    host's support page carrying the unread count.
  - **Expanded:**
    - Assistant and Support tabs.
    - *New conversation*, and *Earlier conversations* from `GetMyCasesAsync` (open cases and ones still with
      the assistant).
    - A link to the host's full support page.
    - The conversation itself: the log, a composer, and a "working" indicator while the assistant answers.
  - **Resizable** with a drag handle on its left edge. Width and open state are remembered per browser in local
    storage, with a default width (320 px) and a minimum (280 px).
  - **Hidden on narrow screens**, where the host's support page is used instead.
  - **Texts** go through the text provider (`SupportPanelText`).
- **Behaviour, as FortDocs' panel has it:**
  - Writing on the Assistant tab raises a case with the assistant, then asks it to answer. Writing on the
    Support tab while the conversation is with the assistant hands it to a person, keeping the conversation.
  - Going back to the assistant after a handover starts a new conversation beside it.
  - When the assistant does not answer, the conversation says so, as `SupportCasesView` does.
  - An answer is marked read only while the panel is open and the page is looked at (`SupportAttention`).
  - The panel redraws when the selected team changes, and when a case changes (`ISupportCaseNotifier`).
- **Which channels show:**
  - The assistant shows when the team is ready for one (`GetAssistantReadinessAsync`), so a team on Off, or
    without a model, gets no assistant tab.
  - Support shows by default.
  - `Offered` narrows both, as on `SupportCasesView`.
  - An optional `ISupportPanelAvailability` the host implements answers per team, with a change event. It is
    the toolkit's form of FortDocs' feature check, for a host that switches support on and off per team.
- **The sample** places it in `MainLayout`.

## Out of scope

- Tool calls in the assistant tab, because they are not stored with the answer (decided on #376).
- Streaming answers. The answer arrives whole, as it does on the support page today.
- The semi-agentic assistant, which is its own issue.

## Acceptance criteria

- With the panel in the sample's layout, a member talks to the assistant and to support from any page, and the
  conversation is the same case the support page shows.
- A team on Off, or with no model, sees no assistant tab; a host's `Offered` and `ISupportPanelAvailability`
  narrow what shows, and a change event redraws it.
- Width and open state survive a reload in the same browser.
- Changing team, or a reply arriving from Slack or mail, redraws the open conversation.
- No literal text: the panel is listed as migrated in `TextCoverageTests`.
- Docs: a section in `docs/articles/support-cases.md` and the `Tharga.Team.Blazor` README.

## Done when

All criteria hold, the full suite passes, the build stays at the warning baseline (14), and the user has tested
in the sample and said it is done.
