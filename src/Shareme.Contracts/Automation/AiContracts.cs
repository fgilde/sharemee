using Shareme.Contracts.Metadata;

namespace Shareme.Contracts.Automation;

public static class AiPermissions
{
    /// <summary>Configure providers, prompts and mappings.</summary>
    public const string Manage = "Automation.Ai";

    /// <summary>Run extractions on assets and review their suggestions.</summary>
    public const string Run = "Automation.Ai.Run";
}

public static class AiProviders
{
    /// <summary>Chat completions with images (OpenAI and compatible servers).</summary>
    public const string OpenAi = "openai";

    public const string Anthropic = "anthropic";

    /// <summary>Azure AI Vision image analysis (tags and caption).</summary>
    public const string AzureVision = "azureVision";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { OpenAi, Anthropic, AzureVision };
}

public static class AiModes
{
    public const string Tags = "tags";
    public const string Description = "description";
}

/// <summary>A tag the provider reports, written as the given list value.</summary>
public sealed record AiMapping(string FromValue, string ToValue);

/// <summary>The key is never sent back; <see cref="HasApiKey"/> tells whether one is stored.</summary>
public sealed record ExtractionDefinitionDto(
    Guid Id, string Name, string Provider, string? Endpoint, string? Model, bool HasApiKey, string Mode, string? Prompt, string InputKind,
    IReadOnlyList<string> AssetTypeKeys, string TargetKey, IReadOnlyList<AiMapping> Mappings, decimal MinConfidence, string? Language, bool AutoApply);

/// <summary>A null <see cref="ApiKey"/> keeps the stored key.</summary>
public sealed record ExtractionDefinitionRequest(
    string Name, string Provider, string TargetKey, string Mode = AiModes.Tags, string? Endpoint = null, string? Model = null, string? ApiKey = null, string? Prompt = null,
    string InputKind = "preview", IReadOnlyList<string>? AssetTypeKeys = null, IReadOnlyList<AiMapping>? Mappings = null, decimal MinConfidence = 0.5m, string? Language = null,
    bool AutoApply = false);

public sealed record ExtractionRunRequest(Guid DefinitionId, IReadOnlyList<Guid> NodeIds);

public static class SuggestionStates
{
    public const string Pending = "pending";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
}

/// <summary>What the provider found, with confidence, and the values it would write.</summary>
public sealed record AiFindingDto(string Value, decimal Confidence);

public sealed record ExtractionSuggestionDto(
    Guid Id, Guid DefinitionId, string DefinitionName, Guid NodeId, string NodeName, string TargetKey, IReadOnlyList<AiFindingDto> Findings, IReadOnlyList<MetadataValue> Values,
    string State, DateTimeOffset CreatedAt);
