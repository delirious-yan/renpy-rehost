namespace RenpyRehost.Core;

public static class Humanize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    /// <summary>Byte count as a short human string, e.g. 1536 -> "1.5 KB", 0 -> "0 B".</summary>
    public static string Bytes(long count)
    {
        if (count <= 0) return "0 B";
        double n = count;
        int u = 0;
        while (n >= 1024 && u < Units.Length - 1) { n /= 1024; u++; }
        return u == 0 ? $"{count} B" : $"{n:0.##} {Units[u]}";
    }
}
