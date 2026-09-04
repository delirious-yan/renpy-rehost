using System.Reflection;
using System.Text.Json;

namespace RenpyRehost.Core;

public sealed record SdkArtifact(long Size, string? Sha256);
public sealed record SdkRelease(SdkArtifact? Sdk, SdkArtifact? Web);

/// <summary>
/// Pinned download sizes/hashes for known Ren'Py releases, from the embedded
/// <c>Resources/sdk-manifest.json</c>. A version that isn't listed still
/// downloads — just without verification, with a warning.
/// </summary>
public sealed class SdkManifest
{
    private readonly Dictionary<string, SdkRelease> _byVersion;

    private SdkManifest(Dictionary<string, SdkRelease> byVersion) => _byVersion = byVersion;

    public SdkRelease? Get(RenpyVersion version)
        => _byVersion.TryGetValue(version.DownloadKey, out var r) ? r : null;

    public static SdkManifest Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        string name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("sdk-manifest.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("sdk-manifest.json is not embedded.");

        using var stream = asm.GetManifestResourceStream(name)!;
        var data = JsonSerializer.Deserialize<Dictionary<string, SdkRelease>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? new();
        return new SdkManifest(data);
    }
}
