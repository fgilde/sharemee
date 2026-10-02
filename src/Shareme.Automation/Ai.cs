using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Assets;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Automation;

/// <summary>Which provider, prompt and rendition fill which property, and whether results need a review.</summary>
public sealed class ExtractionDefinition : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public required string Provider { get; set; }

    public string? Endpoint { get; set; }

    public string? Model { get; set; }

    /// <summary>Encrypted with data protection; never leaves the server.</summary>
    public string? ProtectedApiKey { get; set; }

    public string Mode { get; set; } = AiModes.Tags;

    public string? Prompt { get; set; }

    public string InputKind { get; set; } = DerivationKinds.Preview;

    public List<string> AssetTypeKeys { get; set; } = [];

    public required string TargetKey { get; set; }

    public List<AiMapping> Mappings { get; set; } = [];

    public decimal MinConfidence { get; set; } = 0.5m;

    public string? Language { get; set; }

    public bool AutoApply { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>Values an extraction found for a node, waiting for someone to apply or reject them.</summary>
[NotAudited]
public sealed class ExtractionSuggestion : Entity, IMultiTenant
{
    public Guid DefinitionId { get; set; }

    public Guid NodeId { get; set; }

    public required string TargetKey { get; set; }

    public List<AiFindingDto> Findings { get; set; } = [];

    public List<MetadataValue> Values { get; set; } = [];

    public string State { get; set; } = SuggestionStates.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid TenantId { get; set; }
}

internal static class AiEndpoints
{
    public const string HttpClientName = "Shareme.Ai";

    public static void Map(WebApplication app)
    {
        var definitions = app.MapGroup("/api/v1/extractions").WithTags("AI").RequireAuthorization();
        definitions.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetExtractionDefinitions(), ct).ToHttpResult());
        definitions.MapPost("/", (ExtractionDefinitionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveExtractionDefinition(null, body), ct).ToHttpResult());
        definitions.MapPut("/{id:guid}", (Guid id, ExtractionDefinitionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveExtractionDefinition(id, body), ct).ToHttpResult());
        definitions.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteExtractionDefinition(id), ct).ToHttpResult());
        app.MapPost("/api/v1/extraction-runs", (ExtractionRunRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new StartExtraction(body), ct).ToHttpResult())
            .WithTags("AI").RequireAuthorization();

        var suggestions = app.MapGroup("/api/v1/extraction-suggestions").WithTags("AI").RequireAuthorization();
        suggestions.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetSuggestions(), ct).ToHttpResult());
        suggestions.MapPost("/{id:guid}/apply", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DecideSuggestion(id, true), ct).ToHttpResult());
        suggestions.MapPost("/{id:guid}/reject", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DecideSuggestion(id, false), ct).ToHttpResult());
    }
}

[RequiresPermission(AiPermissions.Run)]
public sealed record GetExtractionDefinitions : IQuery<Result<IReadOnlyList<ExtractionDefinitionDto>>>;

[RequiresPermission(AiPermissions.Manage)]
public sealed record SaveExtractionDefinition(Guid? Id, ExtractionDefinitionRequest Request) : ICommand<Result<ExtractionDefinitionDto>>;

[RequiresPermission(AiPermissions.Manage)]
public sealed record DeleteExtractionDefinition(Guid Id) : ICommand<Result>;

[RequiresPermission(AiPermissions.Run)]
public sealed record StartExtraction(ExtractionRunRequest Request) : ICommand<Result<DataJobDto>>;

/// <summary>Pending suggestions for nodes the user can read.</summary>
[RequiresPermission(AiPermissions.Run)]
public sealed record GetSuggestions : IQuery<Result<IReadOnlyList<ExtractionSuggestionDto>>>;

[RequiresPermission(AiPermissions.Run)]
public sealed record DecideSuggestion(Guid Id, bool Apply) : ICommand<Result>;

internal sealed class AiHandlers(
    CoworkeeDbContext db, MetadataCatalog catalog, NodeAccess access, ICurrentUser currentUser, IDataProtectionProvider protection, IServiceScopeFactory scopes,
    TimeProvider clock)
    : IHandler<GetExtractionDefinitions, Result<IReadOnlyList<ExtractionDefinitionDto>>>,
      IHandler<SaveExtractionDefinition, Result<ExtractionDefinitionDto>>,
      IHandler<DeleteExtractionDefinition, Result>,
      IHandler<StartExtraction, Result<DataJobDto>>,
      IHandler<GetSuggestions, Result<IReadOnlyList<ExtractionSuggestionDto>>>,
      IHandler<DecideSuggestion, Result>
{
    public const int MaxNodes = DataLimits.DefaultNodes;
    private static readonly Error NotFound = Error.NotFound("extractions.not_found", "The extraction does not exist.");

    public async Task<Result<IReadOnlyList<ExtractionDefinitionDto>>> HandleAsync(GetExtractionDefinitions query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ExtractionDefinitionDto> list = (await db.Set<ExtractionDefinition>().AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<ExtractionDefinitionDto>>.Success(list);
    }

    public async Task<Result<ExtractionDefinitionDto>> HandleAsync(SaveExtractionDefinition command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || !AiProviders.All.Contains(request.Provider) || request.Mode is not (AiModes.Tags or AiModes.Description))
        {
            return Error.Validation("Provider", "A name, a known provider and the mode tags or description are required.");
        }

        // the server calls this address with the key, so only https and only where a provider lives
        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(request.Endpoint) && (!Uri.TryCreate(request.Endpoint.Trim(), UriKind.Absolute, out endpoint) || endpoint.Scheme != Uri.UriSchemeHttps))
        {
            return Error.Validation("Endpoint", "The endpoint must be an https address.");
        }

        if ((request.Provider == AiProviders.AzureVision && endpoint is null) || (request.Provider != AiProviders.AzureVision && request.Provider == AiProviders.OpenAi && string.IsNullOrWhiteSpace(request.Model)))
        {
            return Error.Validation("Endpoint", "Azure AI Vision needs its endpoint, OpenAI compatible providers a model.");
        }

        if (request.MinConfidence is < 0 or > 1 || string.IsNullOrWhiteSpace(request.InputKind) || request.Mappings is { Count: > 500 } || request.Prompt is { Length: > 4000 } || request.ApiKey is { Length: > 500 })
        {
            return Error.Validation("MinConfidence", "The threshold lies between 0 and 1; prompts take up to 4000 characters, up to 500 mappings.");
        }

        var inputKind = request.InputKind.Trim();
        if (inputKind is not (DerivationKinds.Thumbnail or DerivationKinds.Preview or DerivationKinds.Still) && !await db.Set<DerivationType>().AnyAsync(t => t.Key == inputKind, cancellationToken))
        {
            return Error.Validation("InputKind", $"There is no image rendition '{inputKind}'.");
        }

        var target = (await catalog.DefinitionsAsync(cancellationToken)).FirstOrDefault(d => d.Key == request.TargetKey);
        if (target is null || !target.Editable || target.IsSystem || target.HasContext)
        {
            return Error.Validation("TargetKey", $"'{request.TargetKey}' is no editable property without contexts.");
        }

        if (request.Mode == AiModes.Description && (target.ListMode != ListModes.None || target.ValueType != PropertyValueTypes.String))
        {
            return Error.Validation("TargetKey", "Descriptions go into text properties.");
        }

        // mapped values become list keys; a mapping to a value the list does not have would fail every write
        var mappings = new List<AiMapping>();
        foreach (var mapping in request.Mappings ?? [])
        {
            if (string.IsNullOrWhiteSpace(mapping?.FromValue) || string.IsNullOrWhiteSpace(mapping.ToValue))
            {
                continue;
            }

            var to = mapping.ToValue.Trim();
            if (target.ListMode != ListModes.None)
            {
                var listValue = target.ListValues.FirstOrDefault(v => v.IsActive && (string.Equals(v.Key, to, StringComparison.OrdinalIgnoreCase) || string.Equals(v.Name, to, StringComparison.OrdinalIgnoreCase)));
                if (listValue is null && !target.AllowCustomValues)
                {
                    return Error.Validation("Mappings", $"'{to}' is no value of the list of '{target.Key}'.");
                }

                to = listValue?.Key ?? to;
            }

            mappings.Add(new AiMapping(mapping.FromValue.Trim(), to));
        }

        if (request.Language is { } language && !(await catalog.LanguagesAsync(cancellationToken)).Any(l => l.Code == language))
        {
            return Error.Validation("Language", $"There is no data language '{language}'.");
        }

        ExtractionDefinition definition;
        if (command.Id is { } id)
        {
            if (await db.Set<ExtractionDefinition>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not { } existing)
            {
                return NotFound;
            }

            definition = existing;
        }
        else
        {
            definition = new ExtractionDefinition { Name = string.Empty, Provider = request.Provider, TargetKey = request.TargetKey };
            db.Add(definition);
        }

        // a stored key only ever goes where it was entered for: a new address or provider needs the key again
        var address = endpoint?.ToString().TrimEnd('/');
        if (request.ApiKey is null && (definition.Endpoint != address || definition.Provider != request.Provider))
        {
            definition.ProtectedApiKey = null;
        }

        definition.Name = request.Name.Trim();
        definition.Provider = request.Provider;
        definition.Endpoint = address;
        definition.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        if (request.ApiKey is { } key)
        {
            definition.ProtectedApiKey = key.Trim().Length == 0 ? null : Protector(protection).Protect(key.Trim());
        }

        definition.Mode = request.Mode;
        definition.Prompt = string.IsNullOrWhiteSpace(request.Prompt) ? null : request.Prompt.Trim();
        definition.InputKind = inputKind;
        definition.AssetTypeKeys = request.AssetTypeKeys?.Distinct().ToList() ?? [];
        definition.TargetKey = request.TargetKey;
        definition.Mappings = mappings;
        definition.MinConfidence = request.MinConfidence;
        definition.Language = request.Language;
        definition.AutoApply = request.AutoApply;
        return Map(definition);
    }

    public async Task<Result> HandleAsync(DeleteExtractionDefinition command, CancellationToken cancellationToken)
    {
        if (await db.Set<ExtractionDefinition>().SingleOrDefaultAsync(d => d.Id == command.Id, cancellationToken) is not { } definition)
        {
            return NotFound;
        }

        // rules that run it would fail on every node
        if ((await db.Set<Rule>().AsNoTracking().Select(r => new { r.Name, r.Actions }).ToListAsync(cancellationToken)).FirstOrDefault(r => r.Actions.Any(a => a.ExtractionId == command.Id)) is { } rule)
        {
            return Error.Conflict("extractions.in_use", $"The rule '{rule.Name}' runs this extraction.");
        }

        db.Remove(definition);
        await db.Set<ExtractionSuggestion>().Where(s => s.DefinitionId == command.Id && s.State == SuggestionStates.Pending).ExecuteDeleteAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<DataJobDto>> HandleAsync(StartExtraction command, CancellationToken cancellationToken)
    {
        if (command.Request.NodeIds is not { Count: > 0 and <= MaxNodes } ids)
        {
            return Error.Validation("NodeIds", $"Choose 1 to {MaxNodes} assets.");
        }

        if (!await db.Set<ExtractionDefinition>().AnyAsync(d => d.Id == command.Request.DefinitionId, cancellationToken))
        {
            return NotFound;
        }

        return DataHandlers.Map(await AiRuns.EnqueueAsync(scopes, command.Request.DefinitionId, ids.Distinct().ToList(), currentUser.UserId ?? Guid.Empty, clock, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<ExtractionSuggestionDto>>> HandleAsync(GetSuggestions query, CancellationToken cancellationToken)
    {
        // pages through until enough are visible, so hidden ones do not crowd out older visible ones; suggestions for hidden properties are not shown
        var hidden = (await access.RestrictionsAsync(cancellationToken)).HiddenProperties;
        var visible = new List<ExtractionSuggestionDto>();
        for (var skip = 0; visible.Count < 200 && skip < 5000; skip += 200)
        {
            var page = await (from suggestion in db.Set<ExtractionSuggestion>().AsNoTracking()
                              join definition in db.Set<ExtractionDefinition>() on suggestion.DefinitionId equals definition.Id
                              join node in db.Set<Node>() on suggestion.NodeId equals node.Id
                              where suggestion.State == SuggestionStates.Pending
                              orderby suggestion.CreatedAt descending, suggestion.Id
                              select new { suggestion, definition.Name, NodeName = node.Name }).Skip(skip).Take(200).ToListAsync(cancellationToken);
            foreach (var item in page)
            {
                if (visible.Count < 200 && !hidden.Contains(item.suggestion.TargetKey) && await access.CanAsync(NodePermissions.Read, item.suggestion.NodeId, cancellationToken))
                {
                    var s = item.suggestion;
                    visible.Add(new ExtractionSuggestionDto(s.Id, s.DefinitionId, item.Name, s.NodeId, item.NodeName, s.TargetKey, s.Findings, s.Values, s.State, s.CreatedAt));
                }
            }

            if (page.Count < 200)
            {
                break;
            }
        }

        return visible;
    }

    public async Task<Result> HandleAsync(DecideSuggestion command, CancellationToken cancellationToken)
    {
        if (await db.Set<ExtractionSuggestion>().SingleOrDefaultAsync(s => s.Id == command.Id && s.State == SuggestionStates.Pending, cancellationToken) is not { } suggestion
            || !await access.CanAsync(NodePermissions.Read, suggestion.NodeId, cancellationToken))
        {
            return Error.NotFound("extractions.suggestion_not_found", "The suggestion does not exist (any more).");
        }

        // deciding changes what the node gets, also when rejecting
        if (!await access.CanAsync(NodePermissions.Update, suggestion.NodeId, cancellationToken))
        {
            return Error.Forbidden("extractions.forbidden", "You may not change this node.");
        }

        if (command.Apply)
        {
            // written as the deciding user, so the regular checks and permissions apply
            if (await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == suggestion.NodeId, cancellationToken) is not { } node
                || (await catalog.DefinitionsAsync(cancellationToken)).FirstOrDefault(d => d.Key == suggestion.TargetKey) is not { } target)
            {
                return Error.NotFound("extractions.suggestion_not_found", "The node or the property is gone.");
            }

            var values = DataJobRunner.Merge(target, node.Metadata.GetValueOrDefault(target.Key) ?? [], suggestion.Values);
            if (await MetadataWrites.WriteAsync(scopes, new SetNodeMetadata(node.Id, new Dictionary<string, List<MetadataValue>?> { [target.Key] = values }, Replace: false, MetadataSources.User), cancellationToken) is { } error)
            {
                return error;
            }
        }

        suggestion.State = command.Apply ? SuggestionStates.Applied : SuggestionStates.Rejected;
        return Result.Success();
    }

    internal static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("Shareme.Ai.ApiKeys");

    private static ExtractionDefinitionDto Map(ExtractionDefinition d) => new(
        d.Id, d.Name, d.Provider, d.Endpoint, d.Model, d.ProtectedApiKey is not null, d.Mode, d.Prompt, d.InputKind, d.AssetTypeKeys, d.TargetKey, d.Mappings, d.MinConfidence, d.Language, d.AutoApply);
}

internal static class AiRuns
{
    /// <summary>Queues a run in a scope of its own, so callers inside other units of work (rules) are not affected.</summary>
    public static async Task<DataJob> EnqueueAsync(IServiceScopeFactory scopes, Guid definitionId, List<Guid> nodeIds, Guid userId, TimeProvider clock, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        var job = new DataJob { Kind = DataJobKinds.Extraction, ExtractionId = definitionId, NodeIds = nodeIds, StartedBy = userId, CreatedAt = clock.GetUtcNow() };
        db.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().Enqueue<DataJobRunner, Guid>(job.Id);
        return job;
    }
}

/// <summary>The connection to providers: no redirects (they would carry the key elsewhere) and no addresses inside the network unless allowed.</summary>
internal static class AiNetwork
{
    public static SocketsHttpHandler Handler(bool allowPrivateNetworks) => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = (await System.Net.Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken)).Where(a => allowPrivateNetworks || !IsInternal(a)).ToArray();
            if (addresses.Length == 0)
            {
                throw new HttpRequestException($"'{context.DnsEndPoint.Host}' is no public address; set Shareme:Ai:AllowPrivateNetworks for providers inside the network.");
            }

            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsInternal(System.Net.IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (System.Net.IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? bytes[0] is 0 or 10 or 127 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254)
              || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) || bytes[0] >= 224
            : (bytes[0] & 0xFE) == 0xFC || address.Equals(System.Net.IPAddress.IPv6Any);
    }
}

internal sealed record AiOutcome(List<AiFindingDto> Findings, List<MetadataValue> Values, string? Error = null);

/// <summary>Asks the provider about one image and turns the answer into values of the target property.</summary>
internal sealed class AiExtractor(IHttpClientFactory clients, IDataProtectionProvider protection)
{
    public const string DefaultAnthropicModel = "claude-opus-5-5";

    public async Task<AiOutcome> ExtractAsync(ExtractionDefinition definition, PropertyDefinition target, string? language, byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        string? key;
        try
        {
            key = definition.ProtectedApiKey is { } protectedKey ? AiHandlers.Protector(protection).Unprotect(protectedKey) : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new AiOutcome([], [], "The stored API key can no longer be read; enter it again.");
        }

        var client = clients.CreateClient(AiEndpoints.HttpClientName);
        Raw raw;
        try
        {
            raw = definition.Provider switch
            {
                AiProviders.AzureVision => await AzureAsync(client, definition, key, language, image, mimeType, cancellationToken),
                AiProviders.Anthropic => Parse(await AnthropicAsync(client, definition, key, Instruction(definition, target, language), image, mimeType, cancellationToken)),
                _ => Parse(await OpenAiAsync(client, definition, key, Instruction(definition, target, language), image, mimeType, cancellationToken)),
            };
        }
        catch (ProviderException exception)
        {
            return new AiOutcome([], [], exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException
                                              or NullReferenceException or OverflowException or InvalidCastException or Polly.Timeout.TimeoutRejectedException
                                              or Polly.CircuitBreaker.BrokenCircuitException)
        {
            return new AiOutcome([], [], cancellationToken.IsCancellationRequested ? throw new OperationCanceledException(cancellationToken) : $"The provider could not be asked or answered unexpectedly ({exception.GetType().Name}).");
        }

        return Convert(definition, target, language, raw);
    }

    private const int MaxTags = 50;

    private sealed record Raw(List<AiFindingDto> Tags, AiFindingDto? Description);

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;

    /// <summary>The largest image each provider takes.</summary>
    public static long MaxImageBytes(string provider) => provider == AiProviders.Anthropic ? 5L * 1024 * 1024 : 10L * 1024 * 1024;

    private sealed class ProviderException(string message) : Exception(message);

    /// <summary>Tags above the threshold, mapped to list values where the property has them; or the description.</summary>
    private static AiOutcome Convert(ExtractionDefinition definition, PropertyDefinition target, string? language, Raw raw)
    {
        // what the image says can steer the model, so how much it writes stays bounded
        raw = raw with
        {
            Tags = raw.Tags.Take(MaxTags).Select(t => t with { Value = Cut(t.Value, 100) }).ToList(),
            Description = raw.Description is { } d ? d with { Value = Cut(d.Value, 2000) } : null,
        };
        var lang = target.Localizable ? language : null;
        if (definition.Mode == AiModes.Description)
        {
            return raw.Description is { Value.Length: > 0 } description && description.Confidence >= definition.MinConfidence
                ? new AiOutcome([description], [MetadataText.Parse(target, description.Value.Trim(), lang)])
                : new AiOutcome(raw.Description is null ? [] : [raw.Description], []);
        }

        var findings = raw.Tags.Where(t => t.Confidence >= definition.MinConfidence && t.Value.Trim().Length > 0).OrderByDescending(t => t.Confidence).ToList();
        var texts = new List<string>();
        foreach (var finding in findings)
        {
            var value = finding.Value.Trim();
            if (definition.Mappings.FirstOrDefault(m => string.Equals(m.FromValue.Trim(), value, StringComparison.OrdinalIgnoreCase)) is { } mapping)
            {
                value = mapping.ToValue;
            }
            else if (target.ListMode != ListModes.None && target.ListValues.FirstOrDefault(v => v.IsActive && (string.Equals(v.Key, value, StringComparison.OrdinalIgnoreCase) || string.Equals(v.Name, value, StringComparison.OrdinalIgnoreCase))) is { } listValue)
            {
                value = listValue.Key;
            }
            else if (target.ListMode != ListModes.None && !target.AllowCustomValues)
            {
                continue;
            }

            if (!texts.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                texts.Add(value);
            }
        }

        if (texts.Count == 0)
        {
            return new AiOutcome(findings, []);
        }

        var values = MetadataText.IsMulti(target)
            ? texts.Select(t => MetadataText.Parse(target, t, lang)).ToList()
            : [MetadataText.Parse(target, target.ListMode != ListModes.None ? texts[0] : string.Join(", ", texts), lang)];
        return new AiOutcome(findings, values);
    }

    private static string Instruction(ExtractionDefinition definition, PropertyDefinition target, string? language)
    {
        var text = new StringBuilder();
        text.Append(definition.Mode == AiModes.Description
            ? "Describe the image in one or two sentences for a media library."
            : "List the keywords that describe what the image shows, for a media library.");
        text.Append($" Write in the language with the code '{language ?? "en"}'.");
        if (definition.Mode == AiModes.Tags && target.ListMode != ListModes.None && !target.AllowCustomValues)
        {
            text.Append(" Choose keywords only from this list: ").AppendJoin(", ", target.ListValues.Where(v => v.IsActive).OrderBy(v => v.SortIndex).Select(v => v.Key)).Append('.');
        }

        if (definition.Prompt is { } prompt)
        {
            text.Append(' ').Append(prompt);
        }

        text.Append(" Answer with JSON only, in this form: {\"tags\":[{\"name\":\"keyword\",\"confidence\":0.9}],\"description\":\"text\",\"descriptionConfidence\":0.9}. Confidence is a number between 0 and 1.");
        return text.ToString();
    }

    private static async Task<string> OpenAiAsync(HttpClient client, ExtractionDefinition definition, string? key, string instruction, byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{definition.Endpoint ?? "https://api.openai.com/v1"}/chat/completions");
        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        request.Content = JsonContent.Create(new
        {
            model = definition.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = instruction },
                new { role = "user", content = new object[] { new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{System.Convert.ToBase64String(image)}" } } } },
            },
        });
        var answer = await SendAsync(client, request, cancellationToken);
        return answer["choices"]![0]!["message"]!["content"]!.GetValue<string>();
    }

    private static async Task<string> AnthropicAsync(HttpClient client, ExtractionDefinition definition, string? key, string instruction, byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{definition.Endpoint ?? "https://api.anthropic.com"}/v1/messages");
        request.Headers.Add("x-api-key", key ?? string.Empty);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model = definition.Model ?? DefaultAnthropicModel,
            max_tokens = 1024,
            system = instruction,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = mimeType, data = System.Convert.ToBase64String(image) } },
                        new { type = "text", text = "Analyse this image as instructed." },
                    },
                },
            },
        });
        var answer = await SendAsync(client, request, cancellationToken);
        if (answer["stop_reason"]?.GetValue<string>() == "max_tokens")
        {
            throw new ProviderException("The answer was cut off at the token limit.");
        }

        return string.Concat(answer["content"]!.AsArray().Where(c => c?["type"]?.GetValue<string>() == "text").Select(c => c!["text"]!.GetValue<string>()));
    }

    private static async Task<Raw> AzureAsync(HttpClient client, ExtractionDefinition definition, string? key, string? language, byte[] image, string mimeType, CancellationToken cancellationToken)
    {
        // captions exist in English only; tags take the base language code
        var features = definition.Mode == AiModes.Description ? "caption" : "tags";
        var code = definition.Mode == AiModes.Description ? "en" : (language ?? "en").Split('-')[0].ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{definition.Endpoint}/computervision/imageanalysis:analyze?api-version=2024-02-01&features={features}&language={Uri.EscapeDataString(code)}");
        request.Headers.Add("Ocp-Apim-Subscription-Key", key ?? string.Empty);
        request.Content = new ByteArrayContent(image);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var answer = await SendAsync(client, request, cancellationToken);
        var tags = answer["tagsResult"]?["values"]?.AsArray().Select(t => new AiFindingDto(t!["name"]!.GetValue<string>(), Confidence(t["confidence"]))).ToList() ?? [];
        var caption = answer["captionResult"] is { } c ? new AiFindingDto(c["text"]!.GetValue<string>(), Confidence(c["confidence"])) : null;
        return new Raw(tags, caption);
    }

    private static async Task<JsonNode> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // the providers' error messages name the problem (model, quota, format) and never the key
            var detail = string.Empty;
            try
            {
                var error = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["error"];
                detail = (error is JsonValue ? error.GetValue<string>() : error?["message"]?.GetValue<string>()) is { Length: > 0 } message ? $": {Cut(message, 200)}" : string.Empty;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
            }

            throw new ProviderException($"The provider answered {(int)response.StatusCode} {response.ReasonPhrase}{detail}.");
        }

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) ?? throw new JsonException("empty answer");
    }

    /// <summary>The JSON the prompt asks for, also when the model wraps it in a code block.</summary>
    private static Raw Parse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new ProviderException("The provider did not answer with JSON.");
        }

        var json = JsonNode.Parse(text[start..(end + 1)])!;
        var tags = json["tags"]?.AsArray().Select(t => t is JsonValue value
                ? new AiFindingDto(value.GetValue<string>(), 1m)
                : new AiFindingDto(t!["name"]?.GetValue<string>() ?? string.Empty, Confidence(t["confidence"])))
            .ToList() ?? [];
        var description = json["description"]?.GetValue<string>() is { } d ? new AiFindingDto(d, json["descriptionConfidence"] is { } dc ? Confidence(dc) : 1m) : null;
        return new Raw(tags, description);
    }

    /// <summary>0..1; numbers in text and percentages are understood, a missing value counts as certain.</summary>
    private static decimal Confidence(JsonNode? node)
    {
        if (node is null)
        {
            return 1m;
        }

        var value = node.GetValueKind() == JsonValueKind.String
            ? double.TryParse(node.GetValue<string>().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0
            : node.GetValue<double>();
        value = value > 1 ? value / 100 : value;
        return decimal.Round((decimal)Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1), 4);
    }
}
