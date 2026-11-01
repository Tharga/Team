# Feature: a user notice composed from current state when it is mailed, at most once per interval (#423)

Branch `feature/user-notice-composer`, from `master` at the merge of PR #436's predecessor (671d964).

## Need
Some things a host must tell its users are a *state*, not an event. Occurrences pushed through the notice machinery mail as often as they arrive, and what a held-back occurrence said would be stale by the time it could be mailed. The host wants one mail, at most weekly, composed from the state when it goes out.

## Scope
Two settings on `UserNoticeType`, usable together or apart:
- `MinimumInterval` (`TimeSpan?`): at most one mail of this notice **per user**, so a person in two teams gets one mail covering both. Occurrences arriving while the interval holds wait, and go together when it is over.
- `Composer`: a host callback (host-side only, never serialized), called when a user's mail for the notice is due. It receives the user, the teams concerned, the occurrences waiting and what the last mail of this notice to this user covered and when, per subject. It returns the subject, the HTML body and the subjects the mail covers, or nothing: then no mail goes and the waiting occurrences are dropped.

The category, the user's choice, team defaults, audience, and the mail log work as for any notice. The subjects a mail covered are recorded on the notice items.

## Decisions (2026-11-01, from the user)
- **At-once only.** A notice with `MinimumInterval` or `Composer` allows only at-once delivery; registering one that also allows the daily summary is refused with a clear message. The summary can be added later without breaking anything.

## My choices (not asked)
- **Where "what the last mail covered" lives:** on the user's stream document for the notice (one per user and notice already), as `LastMailedAt` and the covered subjects with the time each was last covered. No new collection, store or collection name. Subjects not in a later mail keep their earlier time, so "still broken four weeks after it was last mentioned" can be asked.
- **A composer that returns nothing does not start the interval**: no mail was sent, so occurrences arriving afterwards are composed at once.
- **The interval is checked twice**: when an occurrence is distributed (so the stream is due no earlier than the interval allows) and when the stream is flushed (so two instances, or an occurrence distributed before the last mail was recorded, cannot send two).
- **A composer that throws** keeps the occurrences waiting and tries again later (15 minutes); it never drops them and never mails.
- **A mail already begun by an instance that went away** is finished by the instance that takes over, as for any notice; the composer is run again for it and the queue's key keeps it from being mailed twice.
- **The composer sees at most `MaxItemsPerMail` occurrences**, oldest first, as a plain mail would carry; the rest wait.
- **`IUserNoticeOutbox` gains two members and one parameter.** The interface is internal and unchecked (`EditorBrowsable.Never`); a host with its own implementation must add them, which fails loudly at compile time rather than silently ignoring an interval.
- The composer's body is HTML the host is responsible for encoding; the subject is made one line as any notice subject is.

## Not in scope
- The daily summary for such a notice (decided above).
- Per-user language for the composer (notices use the server's language today); the composer gets the user's key.
- A UI for the interval.

## Done when
- A host can register a notice with a minimum interval and a composer, raise occurrences for several channels in two teams, and a user gets one mail covering both, at most once per interval, composed from the state at the time.
- Held-back occurrences are composed together when the interval ends; nothing is lost, nothing stale is sent.
- A composer returning nothing sends nothing and drops what waited.
- The docs describe both settings, the composer contract and the upgrade note for custom outboxes.
