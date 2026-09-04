using System.Text.Json;

namespace RenpyRehost.Core.Stages;

/// <summary>
/// Which asset passes have already run against a reconstructed project, so a
/// <c>--reuse-project</c> re-run can skip straight to Build. Stored as
/// <c>.rehost-pipeline.json</c> at the project root; Reconstruct doesn't create
/// it (a fresh clone has run nothing), so a full rebuild always starts clean.
/// </summary>
public sealed class PipelineState
{
    public bool WebpTranscoded { get; set; }
    public bool ImagesDownscaled { get; set; }
    public bool VideoConverted { get; set; }
    public bool AudioRecompressed { get; set; }

    private static string PathFor(string projDir) => System.IO.Path.Combine(projDir, ".rehost-pipeline.json");

    public static PipelineState Load(string projDir)
    {
        try
        {
            string p = PathFor(projDir);
            return File.Exists(p)
                ? JsonSerializer.Deserialize<PipelineState>(File.ReadAllText(p)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save(string projDir)
    {
        try { File.WriteAllText(PathFor(projDir), JsonSerializer.Serialize(this)); }
        catch { /* best effort — losing the cache just means a slower re-run */ }
    }
}
