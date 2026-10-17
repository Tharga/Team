# Plan: support mail, complete

See `feature.md` for scope. Steps follow its numbering.

## Steps

- [x] 1. Binding kept on a mail-opened case (`MongoSupportCaseStore.AddCaseAsync`); `MailClient.Newer` drops UIDs at or below the position, because IMAP answers `n:*` with the highest UID even when nothing is newer. 4 tests. (7430421)
- [x] 1b. Mail thread: every answer's and reply's Message-ID is recorded as a binding, an answer names the whole thread (In-Reply-To the newest), quotes the conversation so far newest first, and outside production a reply from the policy's override address corresponds. 8 tests.
- [x] 2. Ledger duplicates not logged as errors: a repeat is answered by a read. (580f685)
- [x] 3. Sender → team matching: inbound mail is DKIM-verified in Tharga.Team.Email (DnsClient; aligned `d=`, exactly one From, `From` signed, no `l=`; keys cached 10 min, misses 1 min, lookups cut at 5 s) onto `InboundMail.SenderVerified`/`SenderDomain`; `ISupportSenderDirectory` (Mongo adapter, registered by `AddThargaTeamRepository`) counts accepted, unsuspended memberships of enabled accounts; a verified sender with one account in one team opens in it as a site case would, anything else opens unassigned with `SuggestedTeams` + `SuggestionReason` and a one-click assign in the unassigned view; mail replies now carry the case author's identity. 94 tests.
- [ ] 4. Attachments in and out.
- [ ] 5. Rich text.
- [ ] 6. Several mailboxes.
- [x] 7. Delivery status and retry: a failed answer keeps its reason (`DeliveryError`, from `ISupportChannel.PostWithResultAsync`), a bounce (DSN) marks the answer it names Failed via the binding's `MessageSequence` and never opens a case, `ResendAsync` sends a Failed or Pending entry again, the transcript shows Not sent yet / Not delivered + reason / Send again to support, and a policy redirect is logged at Information. 49 tests.
- [~] 8. Unassigned queue like the cases view, with filters.
- [ ] 9. No no-reply defaults (guard test).
- [ ] 10. Mail to users: categories and opt-out, writing to users, notices on events.
- [ ] Docs, version, close-out.

## Last session

- 2026-10-17: Branch started as a fix; widened to the whole support-mail scope at the consumer's request (one release, not several). Consumer links this source with its switcher.
- 2026-10-17: Step 3 done. For the docs step: Tharga.Team.Email README should say mail is DKIM-checked over DNS (new DnsClient dependency) and what `SenderVerified` does and does not prove; the support docs should describe placement, suggestions, and that `ISupportSenderDirectory` trusts the address on the user record (replace it with one that finds nobody if the sign-in provider issues unverified addresses).
