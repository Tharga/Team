# Feature: finish the text catalogue — TeamComponent and AuditLogView

**Issue:** [Tharga/Team#204](https://github.com/Tharga/Team/issues/204)
**Branch:** `feature/text-teamcomponent-auditlogview`
**Requested by:** Eplicta FortDocs (FD-14, "call the tenant an Organisation in the UI") — tier 2 in
`mission.md`, and their only open ask against Tharga.Team.

## Goal

Route the last two components that render literal user-facing text through `IThargaTextProvider`, so a
consumer overriding the toolkit's wording gets **all four** embedded surfaces, not two of them.

## Why now

The catalogue foundation, `ThargaTextKeys.All` and the `UsersView` migration are on `master` and
**unreleased**. Shipping the release without these two means FortDocs builds a provider, wires it up, and
still sees "Team" in English on `/teams` and in the audit view — close to the complaint they filed. One
more push and a single release closes the whole ask.

## Scope

- `Features/Audit/AuditLogView.razor` — 43 literal strings.
- `Features/Team/TeamComponent.razor` — 61 literal strings, in a 1,329-line component. The bulk is
  **dialog titles, notifications and confirmation prompts built in the C# block**, not markup attributes —
  which is why the first count of 24 was low and why this is the larger half.
- A `TextKey` catalogue per component, matching `UsersViewText` / `TeamSelectorText`.
- The scan self-check in `TextCoverageTests`, which currently proves itself against real files that are
  about to have zero literals (see plan step 5).

## Out of scope

- **`SixLabors.ImageSharp 3.1.12 → 4.0.0`** — held at the user's call (2026-08-09). Do **not** apply it in
  the close-out package re-check; it gets its own pass.
- Any new text-provider API. FortDocs' preferred option — content keys through an overridable resolver —
  already exists; this feature is coverage, not surface.
- Translating anything. The toolkit ships English defaults; the consumer supplies other languages.

## Acceptance criteria

1. `TextCoverageTests.Pending` is empty; both components sit in `Migrated` and scan to zero literals.
2. The scan's self-check no longer depends on a production file containing literals, and still fails if the
   regex stops matching.
3. Every new key is reachable from `ThargaTextKeys.All` (reflection-discovered, so this is an assertion
   rather than a registration step).
4. `dotnet build -c Release` clean and the full suite green — 1,884 tests as the floor.
5. Docs updated: the localization/text surface documents what a consumer overrides and how to enumerate it.
6. #204 closed citing the zero counts; `Requests.md` and `Eplicta/requests.md` updated the same PR.

## Done condition

FortDocs can enumerate `ThargaTextKeys.All`, supply their own values for every key, and see **no English
and no "Team"** in `TeamSelector`, `TeamComponent`, `UsersView` or `AuditLogView`.
