using System.Text.RegularExpressions;

namespace RenpyRehost.Core;

/// <summary>
/// A Ren'Py engine version (major.minor.patch). The patch component is optional
/// in some sources (log.txt sometimes prints only "8.2"); it defaults to 0.
/// </summary>
public sealed partial record RenpyVersion(int Major, int Minor, int Patch) : IComparable<RenpyVersion>
{
    [GeneratedRegex(@"(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex VersionPattern();

    /// <summary>Parse the first "x.y" or "x.y.z" found in <paramref name="text"/>, or null.</summary>
    public static RenpyVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = VersionPattern().Match(text);
        if (!m.Success) return null;
        int patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        return new RenpyVersion(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), patch);
    }

    /// <summary>
    /// True when this SDK version ships a native Web build target. Web export
    /// landed experimentally in 7.4 and is first-class on 8.x. Anything older
    /// needs the decompile-and-port-forward path (roadmap P4).
    /// </summary>
    public bool SupportsWebTarget => Major >= 8 || (Major == 7 && Minor >= 4);

    /// <summary>The token renpy.org uses in download paths: https://www.renpy.org/dl/{key}/ .</summary>
    public string DownloadKey => $"{Major}.{Minor}.{Patch}";

    /// <summary>8.2 added a real <c>web_build</c> CLI command; earlier lines only have the launcher's internal build.</summary>
    public bool HasWebBuildCommand => Major > 8 || (Major == 8 && Minor >= 2);

    public int CompareTo(RenpyVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
