# Plan: a support side panel beside every page (#376)

- [x] 1. Package updates: `dotnet outdated` over the solution. Nothing outdated, so there is no `chore(deps)` commit.
- [x] 2. `SupportPanelText` (every string the panel shows), and `ISupportPanelAvailability` (`Tharga.Team.Blazor`): per-team support availability with a change event, optional.
  - Done: `SupportPanelText` (18 texts plus the shared decline notice). `ISupportPanelAvailability.GetOfferedAsync(teamKey)` returns `SupportAssistance[]` (Assistant, None for a person), with a `Changed` event; it can only narrow.
- [x] 3. `support-panel.js` in `wwwroot`, an ES module like `support-attention.js`: resize by the left edge (pointer capture, minimum width, 60 % of the window at most, reporting the width on release), and is-at-bottom and scroll-to-bottom for the log.
  - Done: `support-panel.js` with `enableResize`, `isAtBottom` and `scrollToBottom`, plus `install()`, which adds the one narrow-screen rule. Scoped CSS cannot reach the `RadzenSidebar` root, as FortDocs found, and a host should not have to link a stylesheet.
- [x] 4. `SupportPanel.razor` with its `.razor.css`: the rail, the expanded panel, the tabs per channel, the conversation (raise, reply, run the assistant, hand over, a new conversation), earlier conversations, the unread count and the link to the support page, read marking, team and case change events, and local storage for open state and width. Hidden below a breakpoint.
  - Done: `SupportPanel.razor` and `.razor.css`, following FortDocs' panel. Readiness decides the assistant; `Offered` and the host's availability narrow both channels. Nothing is drawn without a team or with nobody to ask. Hidden on narrow screens, as the issue says; FortDocs has since moved to an overlay drawer there (FD-103), which is a choice for the user. **Found by the tests:** a boolean `aria-selected` or `aria-expanded` renders with no value when true and is dropped when false, so they are written as strings. FortDocs' panel has the same bug.
- [x] 5. Render tests: the rail per channel; no assistant tab when the team is not ready, or when `Offered` or the availability says no; a message raises a case with the assistant and asks it to answer; writing to support hands over; the decline notice; earlier conversations; open state and width read from and written to local storage; a team change redraws; a case update redraws; listed as migrated in `TextCoverageTests`.
  - Done: `SupportPanelTests`, 16 tests. The panel is listed as migrated in `TextCoverageTests`. `Blazored.LocalStorage` is added to `SupportComponentSurfaceTests`' permitted namespaces: a public UI package a host reaches too. Blazor suite 2113.
- [x] 6. Sample: `<SupportPanel />` in `MainLayout`.
  - Done: inside an `AuthorizeView`, so an anonymous visitor makes no calls, and in a `CustomErrorBoundary`, as the menu is.
- [x] 7. Docs: a support-cases section, "A side panel beside every page", and the `Tharga.Team.Blazor` README.
  - Done: the components list now starts with `SupportPanel`, and a new section "A side panel beside every page" covers placing it, collapsed and open, handover, what is remembered, narrow screens, and who may be asked (readiness, `Offered`, `ISupportPanelAvailability`). The `Tharga.Team.Blazor` README lists the panel and `SupportAssistantConnectionView`.
- [~] 8. Full build (baseline 14) and the full suite; run the sample for the user to test.

## Decisions

- One component the host places, not a layout of the toolkit's: the host keeps its own `RadzenLayout` and adds the panel into it, as FortDocs' `AssistantHost` does.
- Assistant availability is readiness, from #377, so the AI connection from #381 decides it with no further wiring. Support availability defaults to on, and FortDocs' per-team feature switch becomes `ISupportPanelAvailability`, a host seam with a change event.
- Answers are rendered as Markdown with HTML off (`RadzenMarkdown AllowHtml="false"`), as FortDocs does; a member's own messages as plain text.
- No tool calls and no streaming in this feature (see feature.md).

## Last session

2026-10-24: plan written on `feature/support-side-panel`, branched from `feature/support-ai-connection` (PR #383, CI running). Steps 2 to 7 done and committed; the sample runs with the panel for the user to test.
