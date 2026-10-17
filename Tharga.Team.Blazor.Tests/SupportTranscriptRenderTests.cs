using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A case reads as a chat: who raised it on one side, everybody answering on the other, each with their own mark.
/// </summary>
public class SupportTranscriptRenderTests : BunitContext
{
    private const string Author = "alice";

    private static SupportMessage Message(int sequence, SupportMessageKind kind, string identity, string name, string body) => new()
    {
        Sequence = sequence,
        Kind = kind,
        AuthorIdentity = identity,
        AuthorName = name,
        Body = body,
        SentAt = new DateTime(2026, 9, 3, 8, sequence, 0, DateTimeKind.Utc)
    };

    private static readonly SupportMessage[] Conversation =
    [
        Message(1, SupportMessageKind.User, Author, "Alice", "The export is empty."),
        Message(2, SupportMessageKind.Assistant, null, "Assistant", "Try the export button."),
        Message(3, SupportMessageKind.User, "sam", "Sam Support", "Looking into it."),
        Message(4, SupportMessageKind.System, "sam", "Sam Support", "Case closed by Sam Support.")
    ];

    [Fact]
    public void TheReader_SeesTheirOwnMessagesOnTheRight_AndTheAnswersOnTheLeft()
    {
        var view = Render(authorOnRight: true);

        var sides = view.FindAll("[data-side]").Select(x => x.GetAttribute("data-side")).ToArray();

        Assert.Equal(["right", "left", "left"], sides);
    }

    [Fact]
    public void SupportStaff_SeeTheCustomerOnTheLeft_AndTheAnswersOnTheRight()
    {
        var view = Render(authorOnRight: false);

        var sides = view.FindAll("[data-side]").Select(x => x.GetAttribute("data-side")).ToArray();

        Assert.Equal(["left", "right", "right"], sides);
    }

    [Fact]
    public void EachSenderCarriesTheirOwnMark()
    {
        var view = Render(authorOnRight: true);

        var senders = view.FindAll("[data-sender]").Select(x => x.GetAttribute("data-sender")).ToArray();

        Assert.Equal(["author", "assistant", "support", "system"], senders);
    }

    [Fact]
    public void TheAssistantIsAnIcon_NotAWordedLabel()
    {
        var view = Render(authorOnRight: true);

        Assert.Contains("smart_toy", view.Markup);
        Assert.DoesNotContain(">Assistant<", view.Markup);
    }

    [Fact]
    public void ASupportAnswer_NamesThePersonWhoWroteIt()
    {
        var view = Render(authorOnRight: true);

        Assert.Contains("support_agent", view.Markup);
        Assert.Contains("Sam Support", view.Markup);
    }

    [Fact]
    public void AnAnswerWrittenFromTheSupportSide_SitsWithTheAnswers_WhoeverWroteIt()
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<SupportTranscript>(parameters => parameters
            .Add(x => x.Messages, [Message(1, SupportMessageKind.User, Author, "Alice", "Help."), Message(2, SupportMessageKind.User, Author, "Alice", "Answered as support.") with { AsSupport = true }])
            .Add(x => x.CaseAuthorIdentity, Author)
            .Add(x => x.AuthorOnRight, true));

        Assert.Equal(["right", "left"], view.FindAll("[data-side]").Select(x => x.GetAttribute("data-side")).ToArray());
        Assert.Equal(["author", "support"], view.FindAll("[data-sender]").Select(x => x.GetAttribute("data-sender")).ToArray());
    }

    private static SupportMessage Answer(SupportMessageDelivery delivery, string error = null)
        => Message(2, SupportMessageKind.User, "sam", "Sam Support", "Looking into it.") with { AsSupport = true, Delivery = delivery, DeliveryError = error };

    [Fact]
    public void AnAnswerThatWasSent_SaysNothingAboutIt()
    {
        var view = RenderDelivery([Answer(SupportMessageDelivery.Sent)]);

        Assert.Empty(view.FindAll("[data-delivery]"));
    }

    [Fact]
    public void AnAnswerNotYetSent_SaysSo()
    {
        var view = RenderDelivery([Answer(SupportMessageDelivery.Pending)]);

        Assert.Equal("pending", view.Find("[data-delivery]").GetAttribute("data-delivery"));
        Assert.Contains(SupportTranscriptText.NotSentYet.Default, view.Markup);
    }

    /// <summary>With no channel there is nowhere to send it, so Pending is not news.</summary>
    [Fact]
    public void AnAnswerOnACaseWithNoChannel_SaysNothingAboutPending()
    {
        var view = RenderDelivery([Answer(SupportMessageDelivery.Pending)], hasChannels: false);

        Assert.Empty(view.FindAll("[data-delivery]"));
    }

    [Fact]
    public void AnAnswerNotDelivered_SaysWhy_AndOffersToSendItAgain()
    {
        var view = RenderDelivery([Answer(SupportMessageDelivery.Failed, "554 5.7.23 SPF validation failed")], resend: _ => { });

        Assert.Equal("failed", view.Find("[data-delivery]").GetAttribute("data-delivery"));
        Assert.Contains(SupportTranscriptText.NotDelivered.Default, view.Markup);
        Assert.Contains("554 5.7.23 SPF validation failed", view.Find("[data-delivery-error]").TextContent);
        Assert.Contains(SupportTranscriptText.SendAgain.Default, view.Find("[data-resend]").TextContent);
    }

    [Fact]
    public void SendingAgain_HandsTheAnswerToTheHost()
    {
        SupportMessage resent = null;
        var view = RenderDelivery([Answer(SupportMessageDelivery.Failed, "Mailbox full")], resend: x => resent = x);

        view.Find("[data-resend]").Click();

        Assert.Equal(2, resent?.Sequence);
    }

    [Fact]
    public void AReaderWhoMayNotAnswer_IsNotOfferedToSendAgain()
    {
        var view = RenderDelivery([Answer(SupportMessageDelivery.Failed, "Mailbox full")]);

        Assert.Equal("failed", view.Find("[data-delivery]").GetAttribute("data-delivery"));
        Assert.Empty(view.FindAll("[data-resend]"));
    }

    /// <summary>The customer is reading the answer on the site; whether it also reached a channel is support's concern.</summary>
    [Fact]
    public void TheCustomer_IsNotShownDelivery()
    {
        var view = RenderDelivery([Answer(SupportMessageDelivery.Failed, "Mailbox full")], showDelivery: false);

        Assert.Empty(view.FindAll("[data-delivery]"));
    }

    [Fact]
    public void WhatTheCustomerWrote_NeverCarriesADeliveryStatus()
    {
        var view = RenderDelivery([Message(1, SupportMessageKind.User, Author, "Alice", "Help.") with { Delivery = SupportMessageDelivery.Failed, DeliveryError = "x" }]);

        Assert.Empty(view.FindAll("[data-delivery]"));
    }

    private IRenderedComponent<SupportTranscript> RenderDelivery(SupportMessage[] messages, bool hasChannels = true, bool showDelivery = true, Action<SupportMessage> resend = null)
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportTranscript>(parameters =>
        {
            parameters
                .Add(x => x.Messages, messages)
                .Add(x => x.CaseAuthorIdentity, Author)
                .Add(x => x.ShowDelivery, showDelivery)
                .Add(x => x.HasChannels, hasChannels);

            if (resend != null) parameters.Add(x => x.Resend, resend);
        });
    }

    private IRenderedComponent<SupportTranscript> Render(bool authorOnRight)
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportTranscript>(parameters => parameters
            .Add(x => x.Messages, Conversation)
            .Add(x => x.CaseAuthorIdentity, Author)
            .Add(x => x.AuthorOnRight, authorOnRight));
    }
}
