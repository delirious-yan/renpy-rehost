using System.Runtime.InteropServices;

namespace RenpyRehost.Core;

/// <summary>Locates the pieces of an unpacked Ren'Py SDK we need to drive a headless build.</summary>
public sealed class SdkLayout
{
    public string Root { get; }

    public SdkLayout(string root) => Root = root;

    /// <summary>The SDK is unpacked if its bootstrap script is present.</summary>
    public bool IsUnpacked => File.Exists(RenpyPy);

    public string RenpyPy => Path.Combine(Root, "renpy.py");
    public string LauncherDir => Path.Combine(Root, "launcher");
    public string LauncherGameDir => Path.Combine(LauncherDir, "game");

    /// <summary>Web-support files (<c>web/index.html</c>, <c>web/hash.txt</c>, the wasm runtime).</summary>
    public string WebDir => Path.Combine(Root, "web");

    /// <summary>web_build needs <c>web/</c> present with a hash manifest it can validate.</summary>
    public bool HasWebSupport => File.Exists(Path.Combine(WebDir, "hash.txt"))
                              && File.Exists(Path.Combine(WebDir, "index.html"));

    /// <summary>
    /// The command that runs a Ren'Py project or engine command headlessly:
    /// <c>renpy.exe</c> on Windows, <c>renpy.sh</c> elsewhere. It bundles its own
    /// interpreter (Python 2 for the 7.4.x line, Python 3 for 8.x).
    /// </summary>
    public string RenpyRunner
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string exe = Path.Combine(Root, "renpy.exe");
                if (File.Exists(exe)) return exe;
            }
            string sh = Path.Combine(Root, "renpy.sh");
            if (File.Exists(sh)) return sh;
            throw new RehostException($"No renpy launcher (renpy.exe / renpy.sh) in the SDK at {Root}.");
        }
    }

    /// <summary>
    /// Hard-link <paramref name="source"/> to <paramref name="dest"/> when possible
    /// (same NTFS volume), else copy. Returns true if a link was made. Used to
    /// clone a multi-GB game into a build project without duplicating the bytes —
    /// safe only for files the build won't write to (media, archives).
    /// </summary>
    public static bool LinkOrCopy(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (File.Exists(dest)) File.Delete(dest);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && CreateHardLinkW(dest, source, IntPtr.Zero))
            return true;

        File.Copy(source, dest, overwrite: true);
        return false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
