# Plan: logout ends the identity-provider session

Feature scope in `plan/feature.md`. Issue: [Tharga/Team#250](https://github.com/Tharga/Team/issues/250).

## Package updates — done, and there were none

`dotnet outdated` on master, 2026-09-05: **"No outdated dependencies were detected."** PR #252 took the whole
solution current hours ago, so the mandatory start-of-feature step is satisfied with no work. Recorded rather
than skipped silently, because "nothing to do" and "nobody looked" read identically later.

## Steps

- [x] **1. A failing test for the sign-out, written first this time.** The defect lives in a minimal-API
  handler body, which is why no existing test sees it — `ThargaAuthRegistrationTests` only asserts the routes
  are *mapped*. The seam: pull the endpoint's `RequestDelegate` off the mapped route (the existing
  `GetRouteEndpoints` helper already reaches it), invoke it against a `DefaultHttpContext` whose
  `RequestServices` carry a **fake `IAuthenticationService`** that records each `SignOutAsync` call and, for
  the OIDC scheme, writes a sentinel URL into the `Location` header the way the real handler does.
  The assertion that encodes the bug: **`Location` still holds the sentinel afterwards.** Against today's
  code it holds `/`, so the test fails before the fix.

- [x] **2. Fix `/logout`.** Cookie scheme first, OIDC scheme last carrying
  `new AuthenticationProperties { RedirectUri = "/" }`, and delete the `Response.Redirect`. Guard the whole
  federated path on `FederatedSignOut` so the old sequence is still reachable deliberately.

- [x] **3. `ThargaAuthOptions.FederatedSignOut`** (default `true`) and **`PromptForAccount`** (default
  `false`). Both additive. Document on the properties *why* the defaults differ — the first is secure-by-
  default with an escape hatch, the second is off because it costs every user a click to solve a narrow case.

- [x] **4. `prompt=select_account` on the challenge** when `PromptForAccount` is set. Note the mechanism:
  it goes in `AuthenticationProperties.Items` under the `"prompt"` key, which the OIDC handler reads —
  **not** a property on the challenge itself. Assert the parameter actually reaches the properties rather
  than that the option was stored, or the test proves nothing about the request the browser makes.

- [x] **5. Behavioural tests for `/login` too**, covering the default (no prompt) and the opt-in, at both the
  default and a custom path. This is the coverage gap that let the original defect ship, so closing it for one
  endpoint and not the other would leave the same hole.

- [x] **6. Bump `MAJOR_MINOR` to `3.19`** in `build.yml`. Consumers must register a post-logout redirect URI,
  and a release that needs a consumer action is not a patch.

- [x] **7. Docs — and there is a gap here, not just an edit.** `implementation-guide.md` documents the
  `AzureAd` config section (Authority / ClientId / TenantId / CallbackPath) but **never mentions redirect URI
  registration at all**, so the new required post-logout entry has no existing home to go in. Add the
  app-registration step, document both new options, and state the failure mode if the URI is missing. Land as
  its own `docs:` commit.
  **Done.** A new *Redirect URIs on the app registration* section covers both entries — the sign-in callback
  that was already implied and the post-logout one that is new and required — and names the failure mode:
  nothing throws, the provider signs the user out and shows its own page instead of coming back. Two option
  subsections explain why the defaults differ, with a "Changed in 3.19" note telling upgraders to register
  the URI **before** deploying. **README needs no change** — it documents claim revalidation, not the auth
  setup, which lives entirely in the implementation guide.

- [ ] **8. Close-out.** Comment on and close #250 citing the type, member and test; sweep `Requests.md` and
  the backlog; archive `plan/feature.md` to the Plan directory `done/`; `git rm -r plan`; final commit
  `fix: logout ends the identity-provider session complete`; push; open the PR.

## Notes

**Verify before believing, on step 4.** The `"prompt"` items key is the documented mechanism but it is the
kind of detail that is easy to get subtly wrong and impossible to notice — a wrong key silently produces a
challenge without the parameter, and every test asserting "the option was set" still passes. Assert the
`AuthenticationProperties` handed to the auth service, not the option.

**The one thing this plan cannot settle from here** is what a real Entra/CIAM tenant does when
`post_logout_redirect_uri` is not registered. The expectation is a generic sign-out page rather than a hard
failure, which is why shipping this on by default is defensible — but it is an expectation. See the risk
section in `feature.md`.

**Neutral to architecture v4.** This changes session termination, not authorization: no enforcement point
moves, no claim changes provenance, no contract or port is added. It neither advances the target nor works
against it.

## Last session

2026-09-05 — **Steps 1-7 done; implementation complete and green (2490 tests, 0 failed, +9).** Two commits on
`feature/federated-signout`: the fix with its tests, and the docs.

Done red-first this time, unlike the previous feature: the new tests failed 4 of 6 against the original code,
and the two that passed were the ones that should have (login was never broken, and both schemes *were* being
signed out — just in the order that discards the result).

`MAJOR_MINOR` is now **3.19**, because the release asks consumers to register a post-logout redirect URI.

**Not yet done — step 8, which waits on the user confirming the feature is done:** close #250, sweep records,
archive `feature.md`, `git rm -r plan`, final commit, push, open the PR. **The branch is not pushed** —
pushing needs explicit approval.

**The open question is unchanged and cannot be closed from here:** what a real Entra/CIAM tenant does when
`post_logout_redirect_uri` is not registered. Documented as "signs out, shows its own page", which is the
behaviour the design leans on. The reporter offered `quilt4net.ciamlogin.com`; that is what would settle it.
