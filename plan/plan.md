# Plan: support-desk-automatic

- [x] 1. NuGet check: nothing new beyond the recorded holds.
- [x] 2. Enum, desk presence, options, component, texts.
- [x] 3. Tests and docs.
- [~] 4. Push for testing; on the users word close-out, PR, close #393.

## Decisions
- `Automatic = 0`, `Away = 1`, `Hidden = 2`, `Online = 3`: the old Online was 0, so a remembered choice (stored as a number) and a status in memory come back as Automatic with no migration. Values are explicit so the order can never change them.
- A new person starts on Automatic.
- Online lapses: after `SupportCaseOptions.StaffOnlineLapseAfter` (20 minutes by default, configurable) without acting it counts as away, as Automatic does after ten minutes, so support is not shown as there long after the person left. Acting brings it back.

## Release notes
`SupportStaffStatus.Automatic` added; `Online` now means online regardless of activity (until it lapses). Existing remembered "Online" choices become Automatic.
