using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Filtering the users list by how users sign in: those who do, those who read and do not, and those with no directory
/// link. "Has" and "lacks" say something only about users whose methods were read.
/// </summary>
public class SignInFilterTests
{
    private static readonly SignInMethod EId = new("e-ID");
    private static readonly SignInMethod Email = new("Email");

    private static UserViewModel User(string key, string directoryId, SignInMethod[] methods, string error = null)
        => new() { Key = key, Name = key, DirectoryId = directoryId, SignInMethods = methods, SignInError = error };

    private static readonly UserViewModel Both = User("both", "d1", [EId, Email]);
    private static readonly UserViewModel OnlyEmail = User("email", "d2", [Email]);
    private static readonly UserViewModel None = User("none", "d3", []);
    private static readonly UserViewModel NotLinked = User("unlinked", null, null);
    private static readonly UserViewModel Unread = User("unread", "d4", null);
    private static readonly UserViewModel Failed = User("failed", "d5", null, "The directory is down.");

    private static readonly UserViewModel[] Everyone = [Both, OnlyEmail, None, NotLinked, Unread, Failed];

    private static string[] Matching(SignInFilter filter) => [.. Everyone.Where(filter.Matches).Select(x => x.Key)];

    [Fact]
    public void Has_SelectsTheUsersWhoSignInWithTheMethod()
    {
        Assert.Equal(["both"], Matching(SignInFilter.Has("e-ID")));
        Assert.Equal(["both", "email"], Matching(SignInFilter.Has("Email")));
    }

    [Fact]
    public void Lacks_SelectsTheUsersWhoWereReadAndDoNotSignInWithIt()
    {
        Assert.Equal(["email", "none"], Matching(SignInFilter.Lacks("e-ID")));
        Assert.Equal(["none"], Matching(SignInFilter.Lacks("Email")));
    }

    [Fact]
    public void NotLinked_SelectsTheUsersWithNoDirectoryLink_WhateverElseIsKnown()
    {
        Assert.Equal(["unlinked"], Matching(SignInFilter.NotLinked));
    }

    [Fact]
    public void AUserNotLinked_OrNotRead_OrWhoseReadFailed_IsNeverSaidToLackAMethod()
    {
        var lacks = SignInFilter.Lacks("e-ID");

        Assert.False(lacks.Matches(NotLinked));
        Assert.False(lacks.Matches(Unread));
        Assert.False(lacks.Matches(Failed));
    }

    [Fact]
    public void AUserNotRead_OrWhoseReadFailed_IsNeverSaidToHaveAMethod()
    {
        var has = SignInFilter.Has("e-ID");

        Assert.False(has.Matches(Unread));
        Assert.False(has.Matches(Failed));
    }

    [Fact]
    public void TheMethod_IsComparedIgnoringCase()
    {
        Assert.Equal(["both"], Matching(SignInFilter.Has("E-ID")));
        Assert.Equal(["email", "none"], Matching(SignInFilter.Lacks("E-id")));
    }

    [Fact]
    public void ANullUser_MatchesNothing()
    {
        Assert.False(SignInFilter.Has("e-ID").Matches(null));
        Assert.False(SignInFilter.Lacks("e-ID").Matches(null));
        Assert.False(SignInFilter.NotLinked.Matches(null));
    }

    [Fact]
    public void TheMethodsToOffer_AreThoseOfTheUsersWhoWereRead_EachOnce_InOrder()
    {
        var methods = SignInFilter.MethodsOf([.. Everyone, User("more", "d6", [new SignInMethod("EMAIL"), new SignInMethod("Broker")])]);

        Assert.Equal(["Broker", "e-ID", "Email"], methods);
    }

    [Fact]
    public void WithNoUsers_ThereIsNoMethodToOffer()
    {
        Assert.Empty(SignInFilter.MethodsOf(null));
        Assert.Empty(SignInFilter.MethodsOf([]));
    }

    [Fact]
    public void TheSameChoice_IsEqual_SoADropdownCanFindItsSelection()
    {
        Assert.Equal(SignInFilter.Has("e-ID"), SignInFilter.Has("e-ID"));
        Assert.NotEqual(SignInFilter.Has("e-ID"), SignInFilter.Lacks("e-ID"));
    }
}
