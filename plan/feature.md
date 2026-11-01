# Feature: raise an unassigned support case from an anonymous web contact form (#430)

Branch `feature/external-support-case`, from `master` at 29ff551.

## Need
A public contact form (no signed-in user) opens a support case in the **unassigned queue**, as a mail from an unknown sender does today, and support's answers reach the visitor by mail.

## Today
The only way into the unassigned queue is inbound mail: `EmailEventHandler.RaiseAsync` builds the `SupportCase` with `TeamKey = null`, an Email `SupportChannelBinding` whose `Address` is the sender, and calls `ISupportCaseStore.AddCaseAsync`. `ISupportCaseService.RaiseCaseAsync(teamKey, …)` needs a team and a signed-in user.

## Scope
- A public entry point, callable without a user, that opens an unassigned case with the visitor's message as its first message.
- Placement as for mail: `SupportSenderPlacement` suggestions from the address, **never placed in a team** (the address is unverified).
- The case is bound to the Email channel with the visitor's address and the mailbox, so the existing answer path mails replies to them.
- The same `SupportCaseUpdatedEventArgs` notification and audit entry as a mailed case, with a source showing it came from a web form.
- Limits against abuse in Tharga (field and body length); rate limiting, honeypot and captcha stay with the host, and the docs say which is whose.
- Consent given, and when, kept on the case.
- Optional confirmation mail to the visitor through the same mailbox.

## Decisions (2026-11-01, from the user)
- Entry point: a **separate interface** for external cases, anonymous by design, not passing through the user authorization; the audit entry still happens. `ISupportCaseService` stays user-only.
- Stored data: a **contact record on the case** (name, email, phone, organisation, host-defined fields, consent given and when). New field in the MongoDB case entity; existing cases unaffected.
- Receipt mail: **off by default**, a host turns it on with an option on the case settings.

## Not in scope
- Rate limiting, honeypot, captcha (the host's).
- Verifying the visitor's address.
- A UI form component.

## Done when
- A host can open an unassigned case from an anonymous form with one call.
- A support answer to that case is mailed to the address the visitor gave.
- README / docs document the entry point and the abuse limits.
