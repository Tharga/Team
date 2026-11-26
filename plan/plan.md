# Plan: Support staff-only files (#573)

## Decisions (confirmed 2026-11-26)

- **Separate list, not a per-file flag.** `ReplyToCaseAsync(..., attachments, secureAttachments, ct)` and
  `SupportRaiseOptions.SecureAttachments`. Only a member's reply and raise take them, so support's answers,
  mail-started cases and inbound mail cannot produce one by accident.
- **Who sees a secure file in full:** cross-team support readers and the uploader. The team's own
  `support:read` / `support:manage` holders are the member's colleagues, so they get it withheld.
- **Withheld keeps `Id` and `Size`**, blanks `FileName` and `ContentType`, as the issue says.
- **Stripped in one place on the way out**: the domain removes secure files from what it hands to channels,
  the responder and feedback sinks, so a host's own channel or responder is covered too.
- **Fail closed on the store**: after writing, the entry is read back; if the store dropped the secure flag,
  the files are deleted and the write is refused.
- **Removing early takes `support:all:manage`**, the cross-team write grant.
- **Sweep**: deletes content first (idempotent), then marks `RemovedAt` conditionally, so racing instances
  remove once and a crash in between never leaves content behind a "removed" mark. Logged, not audited;
  staff removal is audited.
- **Store port defaults**: the expiry query defaults to nothing; marking removed throws rather than
  reporting success it did not have.

## Steps

- [x] 0. NuGet update — AngleSharp 1.11.0, Anthropic 13.14.0; everything else held by documented rules
      (net10.0 stays on 10.x Microsoft packages, Swashbuckle/Serilog/Roslyn/MailKit holds). 16,732 tests
      pass (one Anthropic test flaked once under load, passes on rerun). Commit `b0bbdb6b`.
- [x] 1. Contracts: `SupportAttachment` fields, `SupportRaiseOptions.SecureAttachments`,
      `ISupportCaseService` reply overload and `RemoveSecureAttachmentAsync` (defaults throw),
      `ISupportCaseStore` expiry query and conditional mark, `SupportAttachmentLocation`,
      `SupportCaseOptions.SecureFiles`. Serialization tests.
      Done. The overload's default replies without secure files when there are none, so a component can call it on a
      host's own service. Store: `GetSecureAttachmentsDueAsync(sentBefore, limit)` (default []) and
      `TryMarkAttachmentRemovedAsync(caseId, sequence, attachmentId, removedAt)` (default throws). Added
      `SupportAttachmentLimits.SecureKeepFor` so the picker can say how long files are kept. `SecureFiles` is a
      `SupportSecureFileOptions` section (`KeepFor` 30 days, zero keeps them; `SweepInterval`; `SweepBatchSize`).
- [x] 2. Domain (`SupportCaseService`): store secure uploads on reply and raise with `Secure` and
      `UploadedBy`, read-back guard, `RemoveSecureAttachmentAsync`, strip outward. Tests.
      Done. Added to the plan: `ISupportCaseStore.KeepsSecureAttachments` (default false) refuses secure files before
      anything is written, because the read-back alone would still leave the file's name visible on a store that
      drops the flag; the read-back stays as the second guard. `SupportAttachmentLimits.AcceptsSecureFiles` tells a
      component whether to offer the secure mode. `SupportCaseChange.AttachmentRemoved` announces a removal.
      `Outward` strips secure files in `DeliverAsync` (covers resend) and from the responder's transcript; feedback
      sinks, the assistant's tools and channel opening never carried files. Constructor takes `secureKeepFor`.
      15 tests in `SupportSecureFileServiceTests`.
- [~] 3. Authorization decorator: withhold in `GetMessagesAsync`, refuse `OpenAttachmentAsync`,
      authorize the new overload and removal. Tests.
- [ ] 4. Auditing decorator: new overload (with secure count), removal, secure open, refused open. Tests.
- [ ] 5. Mail channel skips secure files itself. Test.
- [ ] 6. Sweep, hosted service, options and registration. Tests, including two sweeps racing.
- [ ] 7. MongoDB: entity fields (BSON representation asserted), mapping, expiry query, conditional mark;
      in-memory test store. Tests.
- [ ] 8. Blazor: picker secure mode; transcript secure / withheld / removed and a staff remove button;
      conversation member picker; panel support-tab attach. Texts. Render tests.
- [ ] 9. Docs: `docs/articles/support-cases.md` (Attachments), `Tharga.Team.Support/README.md`,
      `Tharga.Team.Blazor/README.md`.
- [ ] 10. Full test suite, push the branch for testing.

## Last session

2026-11-26: branch created, issue assigned, NuGet updated, plan written. Next: confirm the decisions above,
then step 1.

## README changes needed at completion

Support README and Blazor README: secure files, `SecureFiles.KeepFor`, the picker's secure mode.
