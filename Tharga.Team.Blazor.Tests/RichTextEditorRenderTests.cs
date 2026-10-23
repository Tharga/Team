using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The formatted-text editor: a small toolbar in the reader's language, what is typed reported as it is typed, an
/// editor holding only empty paragraphs counted as empty, and anything pasted sanitized as it arrives.
/// </summary>
public class RichTextEditorRenderTests : BunitContext
{
    private const string CaseId = "case-1";

    private readonly Mock<ISupportCaseService> _cases = new();

    [Fact]
    public void TheToolbar_OffersTheSmallSetOfTools_AndNoImages()
    {
        var view = RenderEditor();

        Assert.Single(view.FindComponents<RadzenHtmlEditorBold>());
        Assert.Single(view.FindComponents<RadzenHtmlEditorItalic>());
        Assert.Single(view.FindComponents<RadzenHtmlEditorUnderline>());
        Assert.Single(view.FindComponents<RadzenHtmlEditorUnorderedList>());
        Assert.Single(view.FindComponents<RadzenHtmlEditorOrderedList>());
        Assert.Single(view.FindComponents<RadzenHtmlEditorLink>());
        Assert.Single(view.FindComponents<RadzenHtmlEditorRemoveFormat>());
        Assert.Single(view.FindComponents<RadzenHtmlEditorUndo>());
        Assert.Single(view.FindComponents<RadzenHtmlEditorRedo>());
        Assert.Empty(view.FindComponents<RadzenHtmlEditorImage>());
        Assert.Empty(view.FindComponents<RadzenHtmlEditorSource>());
    }

    [Fact]
    public void TheToolbar_OffersParagraphHeadingAndSubheading_AndNothingTheReadingSideWouldDrop()
    {
        var view = RenderEditor(headings: true);

        var picker = view.FindComponent<RadzenDropDown<string>>();

        Assert.Equal(["Paragraph", "Heading", "Subheading"], picker.FindAll(".rz-dropdown-item").Select(x => x.TextContent.Trim()));
        Assert.Equal([RichTextBlocks.Paragraph, RichTextBlocks.Heading, RichTextBlocks.Subheading], RichTextBlocks.Offered);
    }

    [Fact]
    public void TheStylePickers_Labels_ComeFromTheTextCatalogue()
    {
        var provider = new Mock<IThargaTextProvider>();
        provider.Setup(x => x.GetAsync(It.IsAny<TextKey>())).ReturnsAsync((TextKey key) => $"[{key.Key}]");

        var view = RenderEditor(provider: provider.Object, headings: true);

        var picker = view.FindComponent<RadzenDropDown<string>>();
        Assert.Equal([$"[{RichTextEditorText.Paragraph.Key}]", $"[{RichTextEditorText.Heading.Key}]", $"[{RichTextEditorText.Subheading.Key}]"],
            picker.FindAll(".rz-dropdown-item").Select(x => x.TextContent.Trim()));
        Assert.Equal($"[{RichTextEditorText.TextStyle.Key}]", picker.Instance.Placeholder);
    }

    [Fact]
    public void ByDefault_NoStylePickerIsOffered_SoTheSmallComposersKeepTheirToolbar()
    {
        var view = RenderEditor();

        Assert.Empty(view.FindComponents<RadzenDropDown<string>>());
    }

    [Theory]
    [InlineData(RichTextBlocks.Heading, "<h2>")]
    [InlineData(RichTextBlocks.Subheading, "<h3>")]
    [InlineData(RichTextBlocks.Paragraph, "<p>")]
    public async Task ChoosingAStyle_FormatsTheBlockAtTheCaret(string block, string command)
    {
        var view = RenderEditor(headings: true);
        JSInterop.Setup<RadzenHtmlEditorCommandState>("Radzen.execCommand", _ => true).SetResult(new RadzenHtmlEditorCommandState { Success = true, Html = $"<{block}>x</{block}>" });
        var picker = view.FindComponent<RadzenDropDown<string>>();

        await view.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(block));

        var call = Assert.Single(JSInterop.Invocations, x => x.Identifier == "Radzen.execCommand");
        Assert.Equal(["formatBlock", command], call.Arguments.Skip(1).Select(x => x?.ToString()));
    }

    [Fact]
    public void EveryToolsTitle_ComesFromTheTextCatalogue()
    {
        var provider = new Mock<IThargaTextProvider>();
        provider.Setup(x => x.GetAsync(It.IsAny<TextKey>())).ReturnsAsync((TextKey key) => $"[{key.Key}]");

        var view = RenderEditor(provider: provider.Object);

        Assert.Equal($"[{RichTextEditorText.Bold.Key}]", view.FindComponent<RadzenHtmlEditorBold>().Instance.Title);
        Assert.Equal($"[{RichTextEditorText.NumberedList.Key}]", view.FindComponent<RadzenHtmlEditorOrderedList>().Instance.Title);
        Assert.Equal($"[{RichTextEditorText.LinkAddress.Key}]", view.FindComponent<RadzenHtmlEditorLink>().Instance.UrlText);
        Assert.Equal($"[{RichTextEditorText.RemoveFormatting.Key}]", view.FindComponent<RadzenHtmlEditorRemoveFormat>().Instance.Title);
    }

    [Fact]
    public void WhatIsTyped_IsReportedAsItIsTyped()
    {
        string value = null;
        var view = RenderEditor(changed: x => value = x);

        view.TypeRichText("<p>Hello <b>there</b></p>");

        Assert.Equal("<p>Hello <b>there</b></p>", value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<br>")]
    [InlineData("<p><br></p>")]
    [InlineData("<div>&nbsp;</div><p> </p>")]
    public void AnEditorHoldingOnlyEmptyParagraphs_IsEmpty(string html)
    {
        Assert.True(RichTextEditor.IsEmpty(html));
    }

    [Fact]
    public void AnEditorHoldingText_IsNotEmpty()
    {
        Assert.False(RichTextEditor.IsEmpty("<p>x</p>"));
    }

    [Fact]
    public async Task PastedContent_IsSanitizedAsItArrives_ImagesIncluded()
    {
        var view = RenderEditor();
        var editor = view.FindComponent<RadzenHtmlEditor>();

        var pasted = await view.InvokeAsync(() => editor.Instance.OnPaste("<p onclick=\"x()\"><b>Hi</b><script>x()</script><img src=\"data:image/png;base64,AAAA\" alt=\"pic\"></p>"));

        Assert.Equal("<p><b>Hi</b>pic</p>", pasted);
    }

    [Fact]
    public void AnAnswerBoxHoldingOnlyEmptyParagraphs_CannotSend()
    {
        var view = RenderConversation();

        view.TypeRichText("<p><br></p>");

        Assert.True(view.Find($"[data-answer='{CaseId}']").HasAttribute("disabled"));
    }

    [Fact]
    public void WhatWasWritten_IsSentAsHtml_ForTheServiceToSanitize()
    {
        var view = RenderConversation();

        view.TypeRichText("<p>Try <b>this</b>.</p>");
        view.Find($"[data-answer='{CaseId}']").Click();

        _cases.Verify(x => x.AnswerCaseAsync(null, CaseId, string.Empty, "<p>Try <b>this</b>.</p>", It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AfterSending_TheEditorIsEmptyAgain()
    {
        var view = RenderConversation();

        view.TypeRichText("<p>Done</p>");
        view.Find($"[data-answer='{CaseId}']").Click();

        Assert.True(string.IsNullOrEmpty(view.FindComponent<RichTextEditor>().Instance.Value));
        Assert.True(view.Find($"[data-answer='{CaseId}']").HasAttribute("disabled"));
    }

    private void Setup(IThargaTextProvider provider = null)
    {
        if (provider == null) Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        else Services.AddSingleton(provider);

        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        Services.AddSingleton(_cases.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<RichTextEditor> RenderEditor(Action<string> changed = null, IThargaTextProvider provider = null, bool headings = false)
    {
        Setup(provider);

        return Render<RichTextEditor>(p => p.Add(x => x.ValueChanged, changed ?? (_ => { })).Add(x => x.ShowHeadings, headings));
    }

    private IRenderedComponent<SupportUnassignedConversation> RenderConversation()
    {
        _cases.Setup(x => x.GetMessagesAsync(null, CaseId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportMessagePage { Items = [] });
        _cases.Setup(x => x.GetAttachmentLimitsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(SupportAttachmentLimits.Default);
        Setup();

        return Render<SupportUnassignedConversation>(p => p.Add(x => x.Case, new SupportCase
        {
            Id = CaseId,
            AuthorIdentity = null,
            AuthorName = "stranger@example.com",
            Subject = "Help",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1
        }));
    }
}
