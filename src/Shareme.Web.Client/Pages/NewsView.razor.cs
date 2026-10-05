using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Knowledge;
using Shareme.Web.Client.Knowledge;

namespace Shareme.Web.Client.Pages;

public partial class NewsView
{
    [Inject] private IIntranetApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private NewsDto? _news;
    private bool _editing, _pinned;
    private string? _title, _markdown;
    private DateTime? _publish, _expires;

    [Parameter] public Guid Id { get; set; }

    [SupplyParameterFromQuery(Name = "edit")] public bool OpenEditor { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _editing = false;
        try
        {
            _news = await Api.GetNewsAsync(Id);
            if (OpenEditor && _news.CanEdit)
            {
                Edit();
            }
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private void Edit() =>
        (_title, _markdown, _publish, _expires, _pinned, _editing) = (_news!.Title, _news.Markdown, _news.PublishAt.LocalDateTime, _news.ExpiresAt?.LocalDateTime, _news.Pinned, true);

    /// <summary>A local date and time of the browser as UTC.</summary>
    private static DateTimeOffset Utc(DateTime local) => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();

    private async Task SaveAsync()
    {
        try
        {
            _news = await Api.UpdateNewsAsync(Id, new NewsUpdateRequest(_title ?? string.Empty, _markdown ?? string.Empty,
                _publish is { } publish ? Utc(publish) : DateTimeOffset.UtcNow, _expires is { } expires ? Utc(expires) : null, _pinned));
            _editing = false;
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }
}
