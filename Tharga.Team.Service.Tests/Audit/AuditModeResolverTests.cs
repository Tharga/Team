using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// The rule both enforcement proxies share: what a method's declared mode and the host's default add up to
/// (Tharga/Team#262).
/// </summary>
public class AuditModeResolverTests
{
    [Theory]
    [InlineData(AuditMode.None)]
    [InlineData(AuditMode.Access)]
    [InlineData(AuditMode.Change)]
    public void AMethodThatDeclaresAMode_KeepsIt(AuditMode declared)
    {
        Assert.Equal(declared, AuditModeResolver.Resolve(declared, AuditMode.Access));
        Assert.Equal(declared, AuditModeResolver.Resolve(declared, AuditMode.None));
    }

    /// <summary>Saying nothing defers to the host — the whole point of the zero value.</summary>
    [Theory]
    [InlineData(AuditMode.None)]
    [InlineData(AuditMode.Access)]
    [InlineData(AuditMode.Change)]
    public void AMethodThatSaysNothing_TakesTheHostDefault(AuditMode hostDefault)
    {
        Assert.Equal(hostDefault, AuditModeResolver.Resolve(AuditMode.Default, hostDefault));
    }

    /// <summary>
    /// A host default cannot defer to itself. It resolves to the behaviour every consumer already has,
    /// which is the safe answer to a question nobody answered.
    /// </summary>
    [Fact]
    public void NobodyHavingAnswered_MeansAudited()
    {
        Assert.Equal(AuditMode.Access, AuditModeResolver.Resolve(AuditMode.Default, AuditMode.Default));
    }

    [Fact]
    public void NoneWritesNothing_ForACallThatHappened()
    {
        Assert.False(AuditModeResolver.ShouldWrite(AuditMode.None, denied: false));
    }

    /// <summary>
    /// The line that keeps this feature from deleting evidence. Silence is about not recording who read
    /// something; it is not about concealing who was refused.
    /// </summary>
    [Fact]
    public void NoneStillWrites_ARefusal()
    {
        Assert.True(AuditModeResolver.ShouldWrite(AuditMode.None, denied: true));
    }

    [Theory]
    [InlineData(AuditMode.Access)]
    [InlineData(AuditMode.Change)]
    public void AnAuditedMode_Writes(AuditMode mode)
    {
        Assert.True(AuditModeResolver.ShouldWrite(mode, denied: false));
    }

    [Fact]
    public void AccessIsAServiceCall_AndChangeIsADataChange()
    {
        Assert.Equal(AuditEventType.ServiceCall,
            AuditModeResolver.EventTypeFor(AuditMode.Access, denied: false, AuditEventType.ScopeDenial));
        Assert.Equal(AuditEventType.DataChange,
            AuditModeResolver.EventTypeFor(AuditMode.Change, denied: false, AuditEventType.ScopeDenial));
    }

    /// <summary>
    /// A refused call did not happen, so the mode's classification of a call does not apply to it. Each
    /// proxy supplies its own denial type, which is why they can share this.
    /// </summary>
    [Theory]
    [InlineData(AuditMode.None)]
    [InlineData(AuditMode.Access)]
    [InlineData(AuditMode.Change)]
    public void ADenialIsADenial_WhateverTheModeAskedFor(AuditMode mode)
    {
        Assert.Equal(AuditEventType.ScopeDenial,
            AuditModeResolver.EventTypeFor(mode, denied: true, AuditEventType.ScopeDenial));
        Assert.Equal(AuditEventType.AccessLevelDenial,
            AuditModeResolver.EventTypeFor(mode, denied: true, AuditEventType.AccessLevelDenial));
    }

    /// <summary>
    /// The safety property this whole design rests on: an existing host that configures nothing keeps the
    /// full access trace it has today. If this ever changes, every consumer's audit trail changes with it.
    /// </summary>
    [Fact]
    public void TheShippedDefault_IsAudited()
    {
        Assert.Equal(AuditMode.Access, new AuditOptions().DefaultAuditMode);
    }
}
