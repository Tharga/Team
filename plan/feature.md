# Feature: logout ends the identity-provider session

**Issue:** [Tharga/Team#250](https://github.com/Tharga/Team/issues/250)
**Branch:** `feature/federated-signout` (from `master`)
**Target release:** **3.19** — `MAJOR_MINOR` in `build.yml` must be bumped from 3.18
**New published packages:** none
**Public API change:** additive only (new options on `ThargaAuthOptions`)

## Goal

Pressing Logout actually signs the user out, rather than clearing the local cookie and leaving a live session
at the identity provider.

## The defect

`Tharga.Team.Blazor/Features/Authentication/ThargaAuthRegistration.cs:100-105`, verified against the code:

```csharp
await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
context.Response.Redirect("/");
```

`SignOutAsync` on the OIDC scheme signs nothing out by itself. It builds the provider's `end_session_endpoint`
URL and writes it into the response's `Location` header, expecting the browser to make that trip.
`Response.Redirect("/")` then **overwrites that header**, so the browser goes to the home page and the
end-session endpoint is never visited.

**The dangerous part is that it misreports itself.** The local cookie really is dropped, so the UI renders as
signed out and the user is told they have signed out. The IdP session survives, and the next `/login`
challenge finds it and re-issues the same identity with no prompt. On a shared machine that is a usable
session left behind a Logout button.

Two further facts confirmed while verifying: there is **no `SignedOutCallbackPath`** configured anywhere in
the package, and **no `prompt` parameter** on the challenge.

## Why the tests did not catch it

`ThargaAuthRegistrationTests` asserts that `/login` and `/logout` are **mapped**. Nothing asserts what either
endpoint *does*. The bug is entirely in the body of a handler that no test invokes, which is why it shipped
and why the fix has to come with behavioural tests rather than more registration ones.

## Scope

1. **The sign-out fix.** Cookie scheme first, OIDC scheme last carrying the `RedirectUri`, and no
   `Response.Redirect` afterwards — let the OIDC handler own the response.
2. **An opt-out**, `ThargaAuthOptions.FederatedSignOut` (default `true`). Secure by default; the escape hatch
   exists for a host that cannot register the post-logout redirect URI immediately and would rather keep
   today's behaviour knowingly than have logout land somewhere unexpected.
3. **An opt-in account picker**, `ThargaAuthOptions.PromptForAccount` (default `false`), sending
   `prompt=select_account` on the challenge.
4. **Behavioural tests for both endpoints**, which is the gap that let this ship.
5. **Docs**: the post-logout redirect URI is a required app-registration step and is currently documented
   nowhere.

## Explicitly not in scope

- **Auditing sign-out.** Sign-in is audited via `OnTokenValidated`; there is no matching sign-out entry, and
  for a session-security question that trail is genuinely worth having. It is still a separate concern from
  making logout work, and a tier-1 security fix should stay small enough to review in one sitting. Worth its
  own issue.
- **Anything about `Tharga.Team.Entra`** or directory-side session state.

## The decision that needs the user, and why

**`prompt=select_account` defaults to off, and that is a judgement rather than an obvious call.** Once
scope item 1 lands, logout genuinely ends the IdP session, so the next login prompts for credentials anyway —
the reported symptom is fixed without it. The account picker only matters in the narrower case where the user
has a live SSO session *elsewhere in the same browser*. Turning it on for everyone costs every user an extra
click on every login to solve that narrower case, so it ships as opt-in.

## The risk worth stating plainly

**If a host has not registered the post-logout redirect URI, behaviour changes for them and I cannot test it
here.** The expectation is that Entra/CIAM still signs the user out and shows its own "you have signed out"
page instead of returning to the host's home page — degraded, but the security-relevant half still happens.
That is the documented behaviour rather than something verified against a live tenant, and it is the single
assumption in this feature most worth confirming before release. The reporter offered to test against
`quilt4net.ciamlogin.com`, which is exactly the environment that would settle it.

## Acceptance criteria

1. `/logout` signs out the cookie scheme **and** the OIDC scheme, and does not write its own redirect
   afterwards — asserted by a test that fails if `Response.Redirect` is reintroduced.
2. The OIDC sign-out carries `RedirectUri = "/"`, so the return trip after the IdP callback lands home.
3. With `FederatedSignOut = false`, the old behaviour is restored exactly, including the local redirect.
4. `/login` challenges the OIDC scheme with `RedirectUri = "/"` and, by default, **no** `prompt`.
5. With `PromptForAccount = true`, the challenge carries `prompt=select_account`.
6. Custom `LoginPath` / `LogoutPath` still map, and the behaviour above holds at the custom paths.
7. Existing auth tests continue to pass unchanged.
8. `MAJOR_MINOR` is 3.19, because consumers must act.

## Done condition

All eight met, `docs/` updated with the app-registration step and the two new options, README reviewed, #250
commented and closed with the evidence, and the backlog and `Requests.md` swept.
