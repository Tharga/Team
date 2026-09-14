# Plan: user-avatar-round

Branch `feature/user-avatar-round` from `origin/master` (aa57e3a), in worktree
`C:\dev\tharga\Toolkit\Team-worktrees\user-avatar-round`.

## Steps
- [x] 1. NuGet update: Tharga.Blazor 2.3.3 -> 2.3.4, MailKit 4.17.0 -> 4.18.0. Release notes read, nothing breaking. Build green, full suite 2687/2687 before and after.
- [x] 2. `UserAvatarShapeTests` renders `UserAvatar` in both branches (initials with a null resolved image, picture with a resolved URL) and asserts the inline style declarations include `flex-shrink:0`. Seen failing: 2/2 failed, each on the expected element.
- [x] 3. Added `flex-shrink:0` to both inline styles in `UserAvatar.razor`. New tests pass; full suite 2689/2689.
- [~] 4. User tests the branch (not pushed yet; pushing awaits approval).
- [ ] 5. Close-out (after user confirmation): re-check NuGet, comment on and close Tharga/Team#279, archive feature.md, remove `plan/`.

## Notes
- Local SDK is 10.0.301, where `dotnet test` reports zero tests; the suite is run via the test executables directly.
- No README or docs change expected: consumer-visible behaviour is only that the avatar stays round.

## Last session
2026-09-14: steps 1-3 done and committed locally. Next: user tests, then close-out.
