# Feature: host-seams

Three small, additive seams that consumers filed as requests. No breaking changes; ships as **3.24**
(new public API, so `MAJOR_MINOR` moves from 3.23 to 3.24 in this PR).

Priority tier: **3, filed requests** (mission.md). None is a security defect, and none blocks a consumer
outright, because each has a workaround. All three are consumer-filed, though, and every workaround is
something each host has to write again.

Target architecture: neutral-to-towards. Each item is correct on its own terms today. The mail change
introduces a record parameter object (rule 3: contracts serialize by construction). The store queries are
phrased in the domain's language (rule 4). Nothing is built on spec.

## 1. Cross-team escalation queries on `ISupportCaseStore` — Tharga/Team#293

**Goal:** a host's single-instance worker can find, across every team, (a) open cases waiting on support
longer than a window and (b) answers the case author has not read within a window. The host does this
without enumerating tenants and without querying the toolkit's schema itself.

**Scope**
- `ISupportCaseStore.GetCasesAwaitingSupportSinceAsync(DateTime waitingSince, int limit, CancellationToken)`
  — open, newest entry from the case author, written before `waitingSince`.
- `ISupportCaseStore.GetCasesWithUnreadAnswerSinceAsync(DateTime answeredBefore, int limit, CancellationToken)`
  — open, newest entry is an *answer* (a person other than the author, or an assistant; never a system
  entry), written before `answeredBefore`, and the author's read marker is behind it.
- Both default to an empty array, so a store written before them keeps compiling and never escalates.
- Both include unassigned (teamless) cases. An unassigned case waits on support just as much as a team's case.
- Mongo adapter: narrow on indexed fields (`Status`, `LastMessageFromAuthor`, `LastMessageAt`), then check
  the transcript tail in memory, the same way `GetCasesForInactivityCloseAsync` does.
- In-memory test store implements both.
- **Deduplication is the host's**, documented rather than built. A case keeps matching on every pass until
  something changes, so the XML docs say to key "already escalated" on `(SupportCase.Id, MessageCount)`:
  that pair names exactly one newest entry. `ISupportEventLedger.TryRecordAsync` fits as-is. The store gets
  no "escalated" marker, because that would be building on spec.

**Out of scope:** a toolkit-owned escalation worker (plan 04 "3d reminders") and mail templates.

## 2. Team key on the invitation mail — Tharga/Team#289

**Goal:** an `ITeamEmailSender` implementation receives the team key without reading ambient state.

**Scope**
- New public record `TeamInviteMail { RecipientEmail, RecipientName, InviteLink, TeamKey, TeamName }`.
- New default interface member `ITeamEmailSender.SendInviteAsync(TeamInviteMail mail)` that forwards to the
  existing four-string overload, so every existing implementation keeps compiling and behaving the same.
- The existing overload stays abstract. Removing it is a v4 item, noted in plan 05.
- `TeamComponent` (the only call site) calls the new member with `updatedTeam.Key`.
- `SmtpTeamEmailSender` implements the new member directly, and the old one forwards to it.
- Docs: the `implementation-guide.md` custom-sender example uses the new member.

## 3. Public MCP context narrowing — Requests.md → Tharga.Team — MCP

**Goal:** a host's own `IMcpToolProvider` can read the calling team and user through a supported API, and
has a documented way to build a context in a unit test.

**Scope**
- `McpContextExtensions` and `AsTeamContext` become `public` (fail-closed null semantics unchanged).
- XML docs on the `TeamMcpContext` constructor state which argument or claim populates `TeamId`, `UserId`
  and `IsDeveloper`.
- A docs section on reading the caller in a host provider and on constructing `TeamMcpContext` in a test.

## Acceptance criteria
- Build and the full test suite pass, and the test count is read, not assumed.
- New tests: the two query predicates are pinned against the real adapter (static predicate tests, like
  `SupportWroteLastTests`), plus the store behaviour through the in-memory store.
- The mail default member forwards the key-less overload unchanged. `TeamComponent` passes the team key.
- `AsTeamContext` is reachable from outside the assembly and returns null for a foreign context.
- `MAJOR_MINOR` is 3.24.
- README / docs updated. Requests.md entry and GitHub #289 / #293 closed with evidence.

## Done condition
User confirms after testing the pushed branch. Then the close-out sequence in shared-instructions.
