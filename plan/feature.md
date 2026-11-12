# Feature: user-identity-link (#475)

A person who signs in through a new identity provider is given a second, empty user with the same email. Root cause confirmed in code: users are found by Identity alone.

- Users grid marks users sharing an email.
- Administrator action and `IUserManagementService.LinkIdentityAsync`: move the duplicate identity onto the user who keeps the teams, remove the duplicate; refused when the duplicate belongs to a team.
- Opt-in `ShouldLinkToExistingUserByEmail` hook for hosts whose provider verifies email.
