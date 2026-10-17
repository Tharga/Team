# Plan: support mail, complete

See `feature.md` for scope. Steps follow its numbering.

## Steps

- [x] 1. Binding kept on a mail-opened case (`MongoSupportCaseStore.AddCaseAsync`); `MailClient.Newer` drops UIDs at or below the position, because IMAP answers `n:*` with the highest UID even when nothing is newer. 4 tests. (7430421)
- [x] 1b. Mail thread: every answer's and reply's Message-ID is recorded as a binding, an answer names the whole thread (In-Reply-To the newest), quotes the conversation so far newest first, and outside production a reply from the policy's override address corresponds. 8 tests.
- [~] 2. Ledger duplicates not logged as errors.
- [ ] 3. Sender → team matching.
- [ ] 4. Attachments in and out.
- [ ] 5. Rich text.
- [ ] 6. Several mailboxes.
- [ ] 7. Delivery status and retry.
- [ ] 8. Unassigned queue like the cases view, with filters.
- [ ] 9. No no-reply defaults (guard test).
- [ ] 10. Mail to users: categories and opt-out, writing to users, notices on events.
- [ ] Docs, version, close-out.

## Last session

- 2026-10-17: Branch started as a fix; widened to the whole support-mail scope at the consumer's request (one release, not several). Consumer links this source with its switcher.
