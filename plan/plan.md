# Plan: pin System.Security.Cryptography.Xml 10.0.13 (#380)

- [x] 1. Package updates: `dotnet outdated` over the solution (2026-10-25). Nothing outdated.
- [x] 2. Reproduce: a plain-SDK project in the scratchpad referencing the published `Tharga.Team.Blazor` 4.3.0 restores with three NU1903 for `System.Security.Cryptography.Xml` 10.0.10. Inside this repo nothing resolves it (`dotnet list package --vulnerable --include-transitive` is clean), since every project gets the assembly from the shared framework.
- [x] 3. The pin in `Tharga.Team.Blazor.csproj`, beside the `Pkcs` one, with a comment saying why and when to drop it.
  - Found: the pin alone does nothing for the consumer. The shared framework ships the assembly, so the SDK prunes the package (NU1510) and `pack` leaves it out of the nuspec. A static `PrunePackageReference Remove` comes too early; the SDK adds the list in `AddPrunePackageReferences`, so a target after it (`KeepCryptographyXmlPin`) takes this one out. Then the nuspec carries `System.Security.Cryptography.Xml` 10.0.13 and NU1510 no longer fires, so no suppression.
- [x] 4. Check: pack `Tharga.Team`, `Tharga.Team.Service` and `Tharga.Team.Blazor` to a local feed and restore the same plain-SDK project against it: no NU1903, and 10.0.13 resolved.
  - Done: the plain-SDK project resolves 10.0.13 (10.0.10 before) with no warning; a Web SDK project restores with no warning and the package pruned, as before.
- [x] 5. Full build (baseline 14) and the full suite.
  - Build at 14 warnings; full suite green: Email 985, MongoDB 468, Blazor 2236, Service 1470, Support 814, Support.Anthropic 12, Mcp 121, Entra 58, Images 11.
