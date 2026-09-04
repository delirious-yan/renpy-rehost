namespace RenpyRehost.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.FirstOrDefault(a => !a.StartsWith('-'))));
    }
}
