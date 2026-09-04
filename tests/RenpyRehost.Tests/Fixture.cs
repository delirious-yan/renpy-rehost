namespace RenpyRehost.Tests;

/// <summary>Builds a throwaway on-disk Ren'Py game tree for a test, cleaned up on dispose.</summary>
public sealed class GameFixture : IDisposable
{
    public string Root { get; }

    private GameFixture(string root) => Root = root;

    public static GameFixture Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "rehost-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new GameFixture(root);
    }

    public GameFixture Dir(string relative)
    {
        Directory.CreateDirectory(Path.Combine(Root, relative));
        return this;
    }

    public GameFixture File(string relative, string content)
    {
        string p = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return this;
    }

    public GameFixture Bytes(string relative, int size)
    {
        string p = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllBytes(p, new byte[size]);
        return this;
    }

    public string At(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
