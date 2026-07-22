using System.Text.Json;
using System.Text.Json.Serialization;

namespace E2ETest.Core.Model;

public sealed class SamplePointer
{
    public int SchemaVersion { get; set; } = 1;
    public string VersionId { get; set; } = "";
    public SampleMetadata Metadata { get; set; } = new();
}

public sealed class SampleMetadata
{
    public int SchemaVersion { get; set; } = 1;
    public string SampleId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ReplaySettings Replay { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SampleSnapshot : IDisposable
{
    public required string VersionId { get; init; }
    public required string VersionDirectory { get; init; }
    public required SampleManifest Manifest { get; init; }
    internal IDisposable? ReadLease { get; init; }
    public void Dispose() => ReadLease?.Dispose();
}
