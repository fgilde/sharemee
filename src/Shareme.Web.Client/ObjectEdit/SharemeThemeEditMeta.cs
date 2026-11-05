using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using MudBlazor.Extensions.Components.ObjectEdit.Options;
using Shareme.Web.Client.Theming;

namespace Shareme.Web.Client.ObjectEdit;

/// <summary>
/// How the theme editor shows the properties of <see cref="SharemeTheme"/>; the base configures the library's (logo, custom CSS).
/// This folder holds the forms' configurations: a class implementing IObjectMetaConfiguration&lt;T&gt; applies to every object form of T.
/// </summary>
public class SharemeThemeEditMeta(CoworkeeLocalizer l) : CoworkeeThemeEditMeta<SharemeTheme>(l)
{
    public override async Task ConfigureAsync(ObjectEditMeta<SharemeTheme> meta)
    {
        await base.ConfigureAsync(meta);
        // e.g. meta.Property(t => t.MapStyle).WithLabel(L["Map style"]);
    }
}
