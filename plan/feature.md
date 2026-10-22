# ScopeView: honour HiddenAccessLevels in the access-level selector

Goal: `ScopeView`'s access-level selector offers only levels the host has not hidden through
`ThargaBlazorOptions.HiddenAccessLevels`, like `ApiKeyView`, `InviteUserDialog`, `TeamAccessRequestDialog` and `TeamComponent`.

Scope:
- New `AccessLevelVisibility.ScopeExplorer` set (Viewer, User, Administrator) and an `Apply` overload that keeps a held level.
- `ScopeView` builds the selector from that set.

Acceptance criteria:
- Hiding Viewer removes Viewer from the selector.
- A level the signed-in member holds stays offered even when hidden.
- Nothing hidden: the selector is unchanged.

Done condition: tests green, PR to master, issue closed with a comment.
