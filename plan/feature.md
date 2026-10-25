# Feature: pin System.Security.Cryptography.Xml 10.0.13 in Tharga.Team.Blazor (#380)

GitHub: #380. Branch: `fix/pin-cryptography-xml`, from master after #385.

## Goal

A consumer of `Tharga.Team.Blazor` on the plain SDK, such as a test project, restores without the three NU1903
warnings `System.Security.Cryptography.Xml` 10.0.10 brings in through `Microsoft.Identity.Web.TokenCache` 4.16.1
(GHSA-7hqp-7c5m-4vq6, GHSA-c399-9853-6jmj, GHSA-r955-8q5j-3h83, fixed in 10.0.13).

## Scope

- Pin `System.Security.Cryptography.Xml` 10.0.13 beside the existing `System.Security.Cryptography.Pkcs` pin, with
  why it is there and when to drop it: once `Microsoft.Identity.Web` depends on a patched version.
- A web host is not affected: it takes the assembly from the ASP.NET Core shared framework.

## Done when

A plain-SDK project referencing the packed package restores with no NU1903, the repo builds at the warning baseline
(14), and the full suite passes.
