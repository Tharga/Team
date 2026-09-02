using System.Runtime.CompilerServices;

// ISupportCaseService moved to Tharga.Team, where its own models already live -- SupportCase,
// SupportCasePage, ISupportCaseStore and ISupportCaseNotifier were all there already, and the service
// contract was the odd one out. Moving it lets a Blazor component depend on contracts alone rather than on
// this package, which now carries MailKit and would otherwise reach every consumer of Tharga.Team.Blazor.
//
// The namespace is unchanged, so no consumer's `using` breaks. This forward keeps assemblies built against
// the old location loading, so an upgrade needs no recompilation either.
[assembly: TypeForwardedTo(typeof(Tharga.Team.Support.Cases.ISupportCaseService))]
