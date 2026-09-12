using RenpyRehost.Core;

namespace RenpyRehost.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Anything that slips past a local try/catch would otherwise just crash
        // the app with no trail — log it and tell the user, instead.
        Application.ThreadException += (_, e) => ReportCrash("UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportCrash("background thread", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown error"));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.FirstOrDefault(a => !a.StartsWith('-'))));
    }

    private static void ReportCrash(string where, Exception ex)
    {
        using var log = Log.Start("crash");
        log.Exception($"unhandled on the {where}", ex);
        MessageBox.Show(
            $"Something went wrong: {ex.Message}\n\nFull detail logged to:\n{log.Path}",
            "Ren'Py Rehost", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
