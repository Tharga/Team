# Feature: format keys and honest text measurement (#204)

## Goal

Make the remaining #204 work measurable and expressible, before sweeping two large components against a
number that turned out to be wrong.

## What went wrong, and how it surfaced

`TeamComponent` was recorded as **24 strings**. Reading it — rather than trusting the scan — showed three
categories of user-facing text, only one of which was being measured:

| Category | Measured before? |
|---|---|
| Attribute literals (`Text=`, `Title=`) | yes |
| Inline prose in markup | no |
| **Display strings built in the C# block** | **no** |

The third is the largest and the most user-visible: dialog titles, notifications, confirmation prompts.
**11 of them are interpolated**, which the mechanism could not express at all.

So the recorded number understated the work by roughly three times, and it was the number quoted in two PR
bodies, the implementation guide and Eplicta's own request file.

## Scope

- **`TextSet.Format(key, args)`** — positional placeholders, so a translation can reorder them. An
  interpolated C# string cannot be translated at all: the text is compiled in.
- **`TextScan`** — one scan covering all three categories, replacing the attribute-only regex. Heuristic by
  necessity; validated by running it over the already-migrated components and inspecting everything it
  reported.
- **Honest counts.** `TeamComponent` **61** (was recorded 24), `AuditLogView` **43** (was 47 — the old regex
  counted some non-display attributes).
- **A real miss fixed.** The widened scan immediately found a string in `TeamSelector` —
  *"Your access to this team is suspended…"* — in a component I had already declared fully migrated.

## Design notes

**Rendering never throws.** A template can come from a consumer's content system, so it is untrusted input on
a render path. A malformed translation falls back to the English default; a malformed default falls back to
the raw template. A provider that throws degrades that one key, not the set.

**The scan's exclusions were validated, not tuned.** Running it over `LoginDisplay`, `TeamSelector` and
`UsersView` and reading every hit is what established that identifiers, enum-qualified names and exception
messages are the false-positive classes — and that one hit was real.

## Acceptance criteria

- [x] A message with a value resolves through a template a translator can reorder.
- [x] A malformed translation, an unbalanced brace, a throwing provider and an unresolved key all degrade
      rather than throw.
- [x] The scan finds all three categories and rejects identifiers, enum names, doc comments and exception
      messages — asserted both ways.
- [x] `TeamSelector` is genuinely clean; the three migrated components report zero.
- [x] Guide and Eplicta's entry carry the corrected counts and say *why* they moved.
- [x] Full suite green, no new warnings.

## Remaining for #204

`TeamComponent` (61) and `AuditLogView` (43). Both now have an accurate number and a mechanism that can
express every string in them.
