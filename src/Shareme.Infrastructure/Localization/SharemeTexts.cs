using Coworkee.Localization.Resources;

namespace Shareme.Infrastructure.Localization;

internal sealed class SharemeTexts : ILocalizationResourceContributor
{
    public void Define(LocalizationResourceContext context) => context.AddEmbeddedJson(typeof(SharemeTexts).Assembly);
}
