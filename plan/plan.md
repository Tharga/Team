# Plan: consented-access-audit

- [x] 1. NuGet update (`chore(deps): nuget update`, 52589b9). xunit.v3, bunit, Identity.Web (the Cryptography.Xml pin
      for #380 is gone — Identity.Web 4.18 depends on the patched version), SkiaSharp, AngleSharp, Anthropic, OpenAI,
      HtmlSanitizer 11 (`AllowedCssClasses` → `AllowedClasses`), MailKit 5 (DKIM ported to `IDnsResolver`). Left: the
      .NET 11 wave, Swashbuckle 11 (needs Microsoft.OpenApi 3, net11-only), Microsoft.CodeAnalysis.CSharp 4.8 (hold).
      **Open:** MailKit 5 breaks a consumer that also has a MailKit/MimeKit 4 dependency at run time (MimeKit 5 ships
      no `MimeKit.dll`) — decision pending with the user.
- [x] 1b. Merged origin/master (#466, #467, #468) — clears the pack-guard failure.
- [x] 2. `TeamScopePolicy.HasConsentedAccess` (internal) + `AuditProvenance.IsConsented` (public); `HasDirectTeamScope`
      reads the same `HoldsDirectly` rule.
- [x] 3. `AuditMetadataKeys.AuthorizedViaConsent`; `ScopeProxy` stamps it on the team-scope path, `AccessLevelProxy`
      once the level check admitted the call. `SystemGrantOnTeamServiceTests.AdmittedByTheTeamScope_...` now asserts
      "no system grant" only: its principal has no member key, so it is consent by the provenance rule.
- [x] 4. `AuditOptions.RecordConsentedAccess` (default true); `CompositeAuditLogger.ShouldLog` drops stamped
      `ServiceCall` entries when off. Both "every option is carried over" tests set it. `ConsentedAccessAuditTests`
      (26 cases). Full suite: 6,841 passed, 0 failed; build 0 warnings.
- [x] 5. Docs: README "Reading the audit log"; implementation guide "Callers let in by a team's consent".
- [~] 6. Close-out: NuGet re-check, archive `plan/feature.md`, remove `plan/`, push, PR (no issue — the PR description
      carries the symptom, cause and fix).

## Last session

2026-11-11 — steps 1–5 done. Waiting on the user about MailKit 5 before the close-out.
