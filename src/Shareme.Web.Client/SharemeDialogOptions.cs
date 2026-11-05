using Coworkee.Client.Blazor.Dialogs;

namespace Shareme.Web.Client;

/// <summary>
/// The options of the library's dialogs (side sheets, confirmations, progress, ...). Override a part to change it, for example:
/// <code>
/// public override MudBlazor.Extensions.Options.DialogOptionsEx Small()
/// {
///     var options = base.Small();
///     options.Animations = [MudBlazor.Extensions.Options.AnimationType.Perspective3d];
///     return options;
/// }
///
/// // administrators may then change every part under Administration, User interface, Dialog options
/// public override bool AdminEditable => true;
/// </code>
/// </summary>
internal sealed class SharemeDialogOptions : DialogOptionsContributor
{
}
