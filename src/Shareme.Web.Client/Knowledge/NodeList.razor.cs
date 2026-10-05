using Microsoft.AspNetCore.Components;
using Shareme.Contracts.Graph;

namespace Shareme.Web.Client.Knowledge;

public partial class NodeList
{
    [Parameter, EditorRequired] public IReadOnlyList<NodeDto> Nodes { get; set; } = [];

    [Parameter] public string? Empty { get; set; }
}
