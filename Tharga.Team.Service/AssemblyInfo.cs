using System.Runtime.CompilerServices;

// ITeamPrincipalAccessor moved to Tharga.Team, because TeamManagementService gates team reads from there
// and needs the caller's claims to do it. Reads previously recomputed the caller's scopes from their member
// row instead, which is what made consent-based access invisible to them (Tharga/Team#248).
//
// The namespace is unchanged, so no consumer's `using` breaks and a host's own ITeamPrincipalAccessor
// implementation still compiles. This forward keeps assemblies built against the old location loading, so
// an upgrade needs no recompilation either.
[assembly: TypeForwardedTo(typeof(Tharga.Team.Service.ITeamPrincipalAccessor))]
