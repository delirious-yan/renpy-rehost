using System.Diagnostics;
using RenpyRehost.Core;

namespace RenpyRehost.App;

public sealed class MainForm : Form
{
    private readonly Panel _dropZone;
    private readonly Label _dropLabel;
    private readonly TextBox _versionBox;
    private readonly ComboBox _assetsBox;
    private readonly CheckBox _serveCheck;
    private readonly ListView _stages;
    private readonly TextBox _log;
    private readonly Button _goButton;
    private readonly Panel _resultBar;
    private readonly LinkLabel _resultLink;

    private readonly TabControl _tabs;
    private readonly ListView _libList;
    private readonly Label _libStatus;
    private readonly ContextMenuStrip _moveMenu;
    private readonly ContextMenuStrip _browserMenu;
    private readonly FlowLayoutPanel _libButtons;
    private readonly ProgressBar _libProgress;
    private readonly Button _cleanBtn;

    private string? _sourcePath;
    private CancellationTokenSource? _cts;
    private IAsyncDisposable? _server;
    private string? _lastOutputDir;
    private string? _lastUrl;

    private static readonly string[] StageNames =
        { "Ingest", "Detect", "Preflight", "AcquireSdk", "Reconstruct", "AssetPipeline", "Build", "Assemble", "Serve" };

    public MainForm(string? initialPath)
    {
        Text = "Ren'Py Rehost";
        MinimumSize = new Size(640, 580);
        Size = new Size(700, 640);
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;

        _tabs = new TabControl { Dock = DockStyle.Fill };
        Controls.Add(_tabs);

        var convertTab = new TabPage("Convert");
        var libraryTab = new TabPage("Library");
        _tabs.TabPages.Add(convertTab);
        _tabs.TabPages.Add(libraryTab);

        // ================= Convert tab =================
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        convertTab.Controls.Add(root);

        // --- drop zone ---
        _dropZone = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.ControlLightLight, AllowDrop = true, Margin = new Padding(0, 0, 0, 8) };
        _dropZone.Paint += (_, e) =>
        {
            using var pen = new Pen(SystemColors.ControlDark) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, 1, 1, _dropZone.Width - 3, _dropZone.Height - 3);
        };
        _dropLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Drop a Ren'Py game folder or .exe here    —    or click to browse" };
        _dropZone.Controls.Add(_dropLabel);
        _dropZone.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _dropZone.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) SetSource(files[0]);
        };
        _dropLabel.Click += (_, _) => Browse();
        _dropZone.Click += (_, _) => Browse();
        root.Controls.Add(_dropZone, 0, 0);

        // --- options ---
        var opts = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        _serveCheck = new CheckBox { Text = "Play when done", Checked = true, AutoSize = true, Margin = new Padding(0, 6, 16, 0) };
        opts.Controls.Add(_serveCheck);
        opts.Controls.Add(new Label { Text = "Ren'Py version:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _versionBox = new TextBox { Width = 70, PlaceholderText = "auto", Margin = new Padding(0, 4, 16, 0) };
        opts.Controls.Add(_versionBox);
        opts.Controls.Add(new Label { Text = "Assets:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _assetsBox = new ComboBox { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 0, 0) };
        _assetsBox.Items.AddRange(new object[] { "auto", "off", "force" });
        _assetsBox.SelectedIndex = 0;
        opts.Controls.Add(_assetsBox);
        root.Controls.Add(opts, 0, 1);

        // --- stages ---
        _stages = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Margin = new Padding(0, 0, 0, 8) };
        _stages.Columns.Add("Stage", 130);
        _stages.Columns.Add("Status", 480);
        foreach (var s in StageNames) _stages.Items.Add(new ListViewItem(new[] { s, "" }));
        root.Controls.Add(_stages, 0, 2);

        // --- log ---
        _log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5f), BackColor = Color.FromArgb(24, 24, 24), ForeColor = Color.Gainsboro, Margin = new Padding(0, 0, 0, 8) };
        root.Controls.Add(_log, 0, 3);

        // --- bottom: go button + result bar ---
        var bottom = new Panel { Dock = DockStyle.Fill };
        _goButton = new Button { Text = "Convert", Dock = DockStyle.Right, Width = 120, Enabled = false };
        _goButton.Click += async (_, _) => await OnGoAsync();
        bottom.Controls.Add(_goButton);

        _resultBar = new Panel { Dock = DockStyle.Fill, Visible = false };
        _resultLink = new LinkLabel { Dock = DockStyle.Left, AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        _resultLink.LinkClicked += (_, _) => OpenResult();
        var showFolder = new Button { Text = "Show folder", Dock = DockStyle.Right, Width = 100 };
        showFolder.Click += (_, _) => { if (_lastOutputDir is { } d) OpenFolder(d); };
        _resultBar.Controls.Add(_resultLink);
        _resultBar.Controls.Add(showFolder);
        bottom.Controls.Add(_resultBar);
        root.Controls.Add(bottom, 0, 4);

        // ================= Library tab =================
        var lib = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        lib.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lib.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        lib.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        lib.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        libraryTab.Controls.Add(lib);

        _libList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Margin = new Padding(0, 0, 0, 8) };
        _libList.Columns.Add("Title", 220);
        _libList.Columns.Add("Ren'Py", 70);
        _libList.Columns.Add("Size", 90);
        _libList.Columns.Add("Last played", 110);
        _libList.Columns.Add("Folder", 400);
        _libList.DoubleClick += (_, _) => PlaySelected(browserExe: null);
        lib.Controls.Add(_libList, 0, 0);

        _libButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var libButtons = _libButtons;
        var playBtn = new Button { Text = "▶  Play", Width = 84, Height = 30 };
        playBtn.Click += (_, _) => PlaySelected(browserExe: null);
        var chooseBrowserBtn = new Button { Text = "Choose browser ▾", Width = 118, Height = 30 };
        _browserMenu = new ContextMenuStrip();
        chooseBrowserBtn.Click += (_, _) => ShowBrowserMenu(chooseBrowserBtn);
        var showBtn = new Button { Text = "Folder", Width = 62, Height = 30 };
        showBtn.Click += (_, _) => { if (SelectedEntry() is { } e) OpenFolder(e.Path); };
        var moveBtn = new Button { Text = "Move  ▾", Width = 78, Height = 30 };
        _moveMenu = new ContextMenuStrip();
        _moveMenu.Items.Add("To app data", null, (_, _) => MoveSelected(Library.AppOutDir));
        _moveMenu.Items.Add("Next to the original game…", null, (_, _) => MoveSelected(null, toGame: true));
        _moveMenu.Items.Add("Choose a folder…", null, (_, _) => MoveSelected(null));
        moveBtn.Click += (_, _) => _moveMenu.Show(moveBtn, new Point(0, moveBtn.Height));
        var addBtn = new Button { Text = "Add…", Width = 58, Height = 30 };
        addBtn.Click += (_, _) => AddExisting();
        var removeBtn = new Button { Text = "Remove", Width = 70, Height = 30 };
        removeBtn.Click += (_, _) => RemoveSelected();
        var refreshBtn = new Button { Text = "Refresh", Width = 66, Height = 30 };
        refreshBtn.Click += (_, _) => RefreshLibrary();
        _cleanBtn = new Button { Text = "Clean up…", Width = 84, Height = 30, Enabled = false };
        _cleanBtn.Click += (_, _) => CleanUpScratch();
        foreach (var b in new[] { playBtn, chooseBrowserBtn, showBtn, moveBtn, addBtn, removeBtn, refreshBtn })
        { b.Margin = new Padding(0, 4, 6, 0); libButtons.Controls.Add(b); }
        _cleanBtn.Margin = new Padding(18, 4, 6, 0);
        libButtons.Controls.Add(_cleanBtn);
        lib.Controls.Add(libButtons, 0, 1);

        _libProgress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, Visible = false, Margin = new Padding(0, 1, 0, 3) };
        lib.Controls.Add(_libProgress, 0, 2);

        _libStatus = new Label { Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, AutoEllipsis = true };
        lib.Controls.Add(_libStatus, 0, 3);

        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedTab == libraryTab) RefreshLibrary(); };

        FormClosing += async (_, _) => { _cts?.Cancel(); if (_server is { } s) await s.DisposeAsync(); };

        RefreshLibrary();
        if (!string.IsNullOrWhiteSpace(initialPath)) SetSource(initialPath!);
    }

    // ---------------- Convert ----------------

    private void Browse()
    {
        using var dlg = new OpenFileDialog { Title = "Pick the game .exe (or any file in the game folder)", CheckFileExists = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetSource(Path.GetDirectoryName(dlg.FileName) ?? dlg.FileName);
    }

    private void SetSource(string path)
    {
        _sourcePath = path;
        _dropLabel.Text = path;
        _goButton.Enabled = true;
        _resultBar.Visible = false;
        _tabs.SelectedIndex = 0;
    }

    private async Task OnGoAsync()
    {
        if (_sourcePath is null) return;

        if (_cts is not null) { _cts.Cancel(); return; }
        _cts = new CancellationTokenSource();
        _goButton.Text = "Cancel";
        _resultBar.Visible = false;
        _log.Clear();
        foreach (ListViewItem i in _stages.Items) i.SubItems[1].Text = "";

        string appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenpyRehost");
        var sink = new UiProgressSink();
        WireSink(sink);

        RenpyVersion? version = null;
        if (_versionBox.Text.Trim() is { Length: > 0 } vt)
        {
            version = RenpyVersion.Parse(vt);
            if (version is null) { MessageBox.Show(this, "Version must look like 8.3.7", "Ren'Py Rehost"); ResetGo(); return; }
        }

        string name = Path.GetFileNameWithoutExtension(_sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        var options = new ConversionOptions
        {
            SdkCacheDir = Path.Combine(appRoot, "sdk"),
            ToolsCacheDir = Path.Combine(appRoot, "tools"),
            WorkDir = Path.Combine(appRoot, "work"),
            OutputDir = Path.Combine(appRoot, "out", name),
            Serve = _serveCheck.Checked,
            EmitOnly = !_serveCheck.Checked,
            VersionOverride = version,
            AssetPipeline = Enum.Parse<AssetPipelineMode>(_assetsBox.Text, ignoreCase: true),
        };

        var ctx = new ConversionContext(Path.GetFullPath(_sourcePath), options, sink);
        var result = await Task.Run(() => PipelineRunner.Default().RunAsync(ctx, _cts.Token));

        if (result.Success)
        {
            _lastOutputDir = ctx.ServeDir;
            _lastUrl = ctx.ServeUrl;

            if (ctx.ServeDir is { } built)
            {
                try { var l = Library.Load(); l.AddOrUpdate(built, _sourcePath); l.Save(); } catch { }
            }

            string size = ctx.Notes.TryGetValue("size", out var s) ? $"  ·  web build {s}" : "";

            if (ctx.RunningServer is { } srv && ctx.ServeUrl is { } url)
            {
                await SwapServer(srv);
                GameLauncher.Open(url);
                _resultLink.Text = $"Playing in your browser{size} — {url}";
            }
            else
            {
                _resultLink.Text = $"Build ready{size} — {ctx.ServeDir}";
            }
            _resultBar.Visible = true;
        }
        else
        {
            AppendLog($"\r\nFAILED at {result.FailedStage}: {result.Error?.Message}", true);
        }
        ResetGo();
    }

    private void ResetGo()
    {
        _cts?.Dispose();
        _cts = null;
        _goButton.Text = "Convert";
    }

    private void WireSink(UiProgressSink sink)
    {
        sink.StageStart += (i, _, n) => Ui(() =>
        {
            if (i - 1 < _stages.Items.Count) { _stages.Items[i - 1].SubItems[1].Text = "running…"; _stages.Items[i - 1].EnsureVisible(); }
        });
        sink.StageEnd += (i, _, skip) => Ui(() =>
        {
            if (i - 1 < _stages.Items.Count)
                _stages.Items[i - 1].SubItems[1].Text = skip is null ? "done" : $"skipped — {skip}";
        });
        sink.Line += (m, warn) => Ui(() => AppendLog(m, warn));
        sink.Sub += (label, frac) => Ui(() =>
        {
            int running = _stages.Items.Cast<ListViewItem>().ToList().FindIndex(x => x.SubItems[1].Text.StartsWith("running"));
            if (running >= 0) _stages.Items[running].SubItems[1].Text = $"{label} {frac * 100:0}%";
        });
    }

    private void AppendLog(string line, bool warn) => _log.AppendText((warn ? "! " : "") + line + "\r\n");

    private void Ui(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(a); else a();
    }

    private void OpenResult()
    {
        if (_lastUrl is { } u) GameLauncher.Open(u);
        else if (_lastOutputDir is { } d) OpenFolder(d);
    }

    // ---------------- Library ----------------

    private void RefreshLibrary()
    {
        var lib = Library.Load();
        _libList.BeginUpdate();
        _libList.Items.Clear();
        foreach (var e in lib.Entries)
        {
            string played = e.LastPlayedUtc is { } lp && DateTimeOffset.TryParse(lp, out var d)
                ? d.LocalDateTime.ToString("yyyy-MM-dd") : "—";
            var item = new ListViewItem(new[]
            {
                e.Title,
                e.SourceVersion ?? "?",
                Humanize.Bytes(e.SizeBytes),
                played,
                e.Path,
            })
            { Tag = e, ForeColor = e.Exists ? SystemColors.WindowText : SystemColors.GrayText };
            _libList.Items.Add(item);
        }
        _libList.EndUpdate();

        _libStatus.Text = lib.Entries.Count == 0
            ? "No builds yet. Convert a game, or use “Add…” to register a web build folder."
            : $"{lib.Entries.Count} build(s) · {Library.DefaultPath}";

        _ = UpdateScratchButtonAsync();
    }

    private static string AppRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenpyRehost");
    private static string WorkDir => Path.Combine(AppRoot, "work");

    private async Task UpdateScratchButtonAsync()
    {
        long bytes = await Task.Run(() => Housekeeping.SurveyWork(WorkDir).bytes);
        if (IsDisposed) return;
        _cleanBtn.Enabled = bytes > 0;
        _cleanBtn.Text = bytes > 0 ? $"Clean up {Humanize.Bytes(bytes)}…" : "Clean up…";
    }

    private async void CleanUpScratch()
    {
        var (bytes, items) = Housekeeping.SurveyWork(WorkDir);
        if (bytes == 0) { _libStatus.Text = "No build scratch to clean."; return; }

        if (MessageBox.Show(this,
                $"Delete {Humanize.Bytes(bytes)} of build scratch ({items} folder(s))?\n\n"
                + "These are reconstructed project clones and intermediate build output. "
                + "Your finished builds and the downloaded Ren'Py SDKs are not touched.\n\n"
                + "A future re-convert of those games will re-clone from scratch.",
                "Clean up — Ren'Py Rehost", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _cleanBtn.Enabled = false;
        _libStatus.Text = "Cleaning…";
        long freed = await Task.Run(() => Housekeeping.CleanAllWork(WorkDir));
        _libStatus.Text = $"Reclaimed {Humanize.Bytes(freed)}.";
        RefreshLibrary();
    }

    private LibraryEntry? SelectedEntry() =>
        _libList.SelectedItems.Count > 0 ? _libList.SelectedItems[0].Tag as LibraryEntry : null;

    /// <summary>Serve the selected build and open it. <paramref name="browserExe"/> null = the preferred/default browser.</summary>
    private async void PlaySelected(string? browserExe)
    {
        if (SelectedEntry() is not { } e) { _libStatus.Text = "Select a build first."; return; }
        if (!e.Exists) { _libStatus.Text = "That build folder is missing — Remove it, or re-convert."; return; }

        try
        {
            var server = LocalWebServer.Start(e.Path);
            await SwapServer(server);
            string url = server.Url + "index.html";
            if (browserExe is null) GameLauncher.Open(url); else GameLauncher.OpenWith(url, browserExe);

            var lib = Library.Load();
            lib.MarkPlayed(e.Path);
            lib.Save();
            RefreshLibrary();

            _libStatus.Text = $"Playing “{e.Title}” — {url}  (server stops when you close this window or play another)";
        }
        catch (Exception ex)
        {
            _libStatus.Text = $"Couldn't start: {ex.Message}";
        }
    }

    private void ShowBrowserMenu(Button anchor)
    {
        _browserMenu.Items.Clear();
        var browsers = BrowserCatalog.Installed();
        if (browsers.Count == 0)
            _browserMenu.Items.Add("(no browsers found)", null, (_, _) => { }).Enabled = false;
        foreach (var b in browsers)
            _browserMenu.Items.Add(b.Name, null, (_, _) => PlaySelected(b.ExePath));
        _browserMenu.Items.Add(new ToolStripSeparator());
        _browserMenu.Items.Add("Browse…", null, (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Pick a browser executable",
                Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) PlaySelected(dlg.FileName);
        });
        _browserMenu.Show(anchor, new Point(0, anchor.Height));
    }

    private void AddExisting()
    {
        using var dlg = new FolderBrowserDialog { Description = "Pick a web build folder (the one with index.html)" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var lib = Library.Load();
            var e = lib.AddOrUpdate(dlg.SelectedPath);
            lib.Save();
            RefreshLibrary();
            _libStatus.Text = $"Added “{e.Title}”.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ren'Py Rehost");
        }
    }

    private void RemoveSelected()
    {
        if (SelectedEntry() is not { } e) return;
        if (MessageBox.Show(this, $"Remove “{e.Title}” from the library?\n\nThe build files on disk are left alone.",
                "Ren'Py Rehost", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var lib = Library.Load();
        lib.Remove(e.Path);
        lib.Save();
        RefreshLibrary();
    }

    private static string? GameFolderOf(LibraryEntry? e)
    {
        if (e?.SourcePath is not { Length: > 0 } s) return null;
        if (Directory.Exists(s)) return s;
        if (File.Exists(s)) return Path.GetDirectoryName(s);
        return null;
    }

    private async void MoveSelected(string? destParent, bool toGame = false)
    {
        if (SelectedEntry() is not { } e) { _libStatus.Text = "Select a build first."; return; }
        if (!e.Exists) { _libStatus.Text = "That build folder is missing — nothing to move."; return; }

        if (toGame)
        {
            destParent = GameFolderOf(e);
            if (destParent is null)
            {
                // Older entries (and ones added by hand) don't know their source —
                // ask once, then remember it on the entry so it's one click next time.
                using var dlg = new FolderBrowserDialog
                {
                    Description = $"Where is the “{e.Title}” game? Pick its folder (the one with the .exe).",
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                destParent = dlg.SelectedPath;
                try
                {
                    var l = Library.Load();
                    if (l.Entries.FirstOrDefault(x => string.Equals(x.Path, e.Path, StringComparison.OrdinalIgnoreCase)) is { } le)
                    { le.SourcePath = destParent; l.Save(); }
                }
                catch { /* remembering is a nicety — don't block the move */ }
            }
        }
        else if (destParent is null)
        {
            using var dlg = new FolderBrowserDialog { Description = "Move the build into this folder" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            destParent = dlg.SelectedPath;
        }

        _libStatus.Text = $"Moving “{e.Title}” to {destParent} …";
        _libList.Enabled = false;
        _libButtons.Enabled = false;
        _libProgress.Value = 0;
        _libProgress.Visible = true;

        var progress = new Progress<Library.MoveProgress>(p =>
        {
            _libProgress.Value = (int)Math.Round(Math.Clamp(p.Fraction, 0, 1) * 1000);
            _libStatus.Text = p.Instant
                ? $"Moving “{e.Title}” …"
                : $"Moving “{e.Title}” — {p.Fraction * 100:0}%  ({Humanize.Bytes(p.BytesDone)} / {Humanize.Bytes(p.BytesTotal)})";
        });

        try
        {
            string moved = await Task.Run(() =>
            {
                var lib = Library.Load();
                string p = lib.Move(e.Path, destParent!, progress);
                lib.Save();
                return p;
            });
            RefreshLibrary();
            _libStatus.Text = $"Moved “{e.Title}” → {moved}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ren'Py Rehost");
            _libStatus.Text = "Move failed.";
        }
        finally
        {
            _libProgress.Visible = false;
            _libList.Enabled = true;
            _libButtons.Enabled = true;
        }
    }

    private async Task SwapServer(IAsyncDisposable next)
    {
        if (_server is { } old) { try { await old.DisposeAsync(); } catch { } }
        _server = next;
    }

    private static void OpenFolder(string dir)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); } catch { }
    }
}
