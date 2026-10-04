using Bunit;
using Coworkee.Contracts.Ai;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Web.Client.Assistant;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class AssistantTests : BunitContext
{
    private readonly IAssistantApi _api = Substitute.For<IAssistantApi>();

    public AssistantTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public async Task Sends_the_conversation_and_shows_answer_and_tool_calls_as_plain_text()
    {
        _api.ChatAsync(default!, default).ReturnsForAnyArgs(new ChatResponseDto("<b>Created</b> the folder.", "end_turn",
            [new AiToolCallDto(Guid.CreateVersion7(), "create_folder", AiChannels.Chat, "{}", true, null, 12, DateTimeOffset.UtcNow, null)]));
        var page = Render<Shareme.Web.Client.Pages.Assistant>();

        page.Find("textarea").Change("Create Summer");
        await page.Find("[data-testid='send']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-role='assistant']").TextContent.ShouldContain("<b>Created</b> the folder."));
        page.FindAll("[data-role='assistant'] b").ShouldBeEmpty();
        page.Find("[data-tool='create_folder']").ShouldNotBeNull();
        await _api.Received(1).ChatAsync(Arg.Is<IReadOnlyList<ChatMessageDto>>(m => m.Count == 1 && m[0].Role == "user" && m[0].Text == "Create Summer"), Arg.Any<CancellationToken>());
    }
}
