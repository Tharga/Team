# Plan: short opaque invitation tokens, and invitations that expire

Feature scope in `plan/feature.md`. Issue: [Tharga/Team#249](https://github.com/Tharga/Team/issues/249).

## Package updates — none

`dotnet outdated` on master, 2026-09-05: **"No outdated dependencies were detected."** Third check today and
still clean; #252 took the solution current this morning.

## Order, and why it is this way

Part 2 (expiry) is **independent of part 1** and touches no storage seam. Part 1 is where the risk is: a new
seam method, a new index, and a claim about MongoDB uniqueness that has to be proven rather than reasoned.

So **expiry ships first**. If part 1 turns out to need a separate collection after all, part 2 is already done
and reviewable rather than stuck behind it.

## Part 2 — expiry (do this first)

- [x] **1. `Invitation.ExpiresAt`**, optional and nullable. Not `required` — the other three are, and hosts
  construct `Invitation` themselves, so a required member is a compile break for them.
- [x] **2. `InvitationOptions`** in `Tharga.Team` with `Lifetime` (a `TimeSpan?`, default null = never
  expires). Follows `ConsentOptions`, which lives in the core package for the same reason: it is policy, not
  presentation, and more than one surface has to agree on it. Resolved as `IOptions<InvitationOptions>`.
- [x] **3. Enforce at acceptance.** An invitation whose effective expiry has passed is refused.
  **`TeamInvitation` gains a state so the screen can say *expired* rather than *invalid*** — the issue asks for
  this specifically, and it is the difference between a user retrying and a user asking for a new link.
  Careful: expiry is reported to someone holding a valid code, so it discloses nothing; a *malformed* code must
  stay indistinguishable from an unknown one, per the existing rule.
- [x] **4. `ExtendInvitationAsync`** on the management service — an operation, authorized by `member:manage`,
  audited as one fact. Moves `ExpiresAt`; **must not touch `InviteKey`**.
- [x] **5. Re-inviting extends rather than replaces.** The issue's real requirement: someone who already mailed
  a link can give it another fortnight without the recipient's link dying.
- [x] **6. Tests**: no lifetime = never expires (existing behaviour preserved); expired is refused and reported
  as expired; extend moves the expiry and leaves the key identical; re-invite extends.

## Part 1 — the short token

- [~] **7. Token generation.** 16 bytes from `RandomNumberGenerator`, base64url, 22 chars. Assert the source is
  cryptographic, not that the output "looks random" — a test that only checks length and alphabet passes just
  as happily for `Guid.NewGuid().ToString("N")[..22]`, which is the mistake worth guarding against.
- [ ] **8. The seam method** on `TeamServiceBase` — resolve a team by an outstanding invite key. **`virtual`,
  returning null by default**, matching `GetAllTeamsInternalAsync` and the other later additions. Null means
  "this store cannot resolve a bare token", which is what makes criterion 10's fallback work.
- [ ] **9. The MongoDB implementation and its index.** A non-unique multikey index on
  `"Members.Invitation.InviteKey"`, by string path — the same shape as the existing `"Members.Key"` index in
  `TeamRepositoryCollection.Indices`.
  **Verify the uniqueness reasoning against a real index before trusting it** (see the note below).
- [ ] **10. Resolve, with the old format still working.** `GetInvitationAsync` tries base64-JSON first, falls
  back to a bare token. **Ambiguity returns null** rather than picking a team.
- [ ] **11. Mint the short link.** `BuildInviteLink` emits `?tic=<token>`; `TeamInviteView` reads `tic` and
  still accepts the old `TeamInviteCode` parameter, including the copy it stashes in local storage across the
  login redirect.
- [ ] **12. Tests** for every acceptance criterion, including the two that are easy to leave unexecuted: the
  multi-match null and the store-without-the-override fallback.

## Then

- [ ] **13. Docs** — both surfaces. The link format is consumer-visible, and the expiry is new configuration.
- [ ] **14. Close-out.** Comment on and close #249 with evidence; sweep `Requests.md` and the backlog; archive
  `feature.md` to `done/`; `git rm -r plan`; final commit; push; open the PR.

## Notes

**The MongoDB uniqueness claim is the one thing in this plan I have reasoned but not proven.** A unique
multikey index on the invite path would collide across documents on the null entries of non-invited members,
and `partialFilterExpression` does not help because it filters documents rather than array elements. That is
why step 9 specifies a *non-unique* index. **If that reasoning is wrong the failure is at team creation, in
production, for every host** — so it gets a test against a real index, not a comment.

**Two criteria describe paths that are easy to write and never run:** an ambiguous token returning null, and a
store that has not overridden the seam falling back to the old format. Both need a test that actually drives
them.

**Architecture v4**: neutral-to-positive. The new seam method is semantic ("resolve a team by invite key"), not
store-shaped, so it respects rule 4. `ExtendInvitationAsync` is an operation rather than a property set, which
is rule 1. Nothing new is built on spec: every piece has a caller in this feature.

## Part 2 notes — what implementing it turned up

**A wiring gap that would have made expiry silently not work.** `TeamServiceRepositoryBase` calls
`base(userService, iconStore:, cache:)` and would not have forwarded `InvitationOptions`, so every MongoDB
host would have configured a lifetime and had it ignored. Found by reading the constructor rather than by a
failing test, because no test would have failed — the same forwarding hazard `ITeamCache` already documents.

**Re-invite had to do more than dedupe.** `InviteUserModel` carries `AccessLevel`, so an administrator
re-inviting at a different level plainly means the new one. Renewing while silently keeping the old level
would have been a new footgun in the name of fixing an old one, so changed level and name are applied to the
renewed invitation.

**Extending is skipped entirely when no lifetime is configured.** Otherwise re-invite would start calling the
expiry seam on hosts that never opted into expiry and have not implemented it — turning a working re-invite
into a `NotSupportedException`.

## Last session

2026-09-05 — Branch cut from master after #253 merged. Research done and it changed the shape of the work: the
public contract already takes an opaque code and returns the team key, so **no contract change is needed** and
the team key was never required in the URL. Plan written, **nothing implemented**; step 1 next, after the plan
is confirmed.
