using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Velopack;
using Velopack.Sources;

namespace HybridSlicer.Launcher;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class LauncherForm : Form
{
    // Dark title bar via DWM
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly Process _server;
    private readonly string  _networkIp;
    private readonly string? _curaPath;
    private readonly System.Windows.Forms.Timer _healthTimer;

    private WebView2    _webView = null!;
    private NotifyIcon? _tray;
    private string _status = "starting";
    private bool   _webReady;

    // ── Update state ────────────────────────────────────────────────────────
    private string  _updateStatus = "checking";
    private string? _updateVersion;
    private int?    _updatePct;
    private Velopack.UpdateInfo? _pendingUpdate;
    private UpdateManager? _updateMgr;

    public LauncherForm(Process server, string networkIp, string? curaPath)
    {
        _server    = server;
        _networkIp = networkIp;
        _curaPath  = curaPath;

        _healthTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _healthTimer.Tick += (_, _) => PollHealth();

        BuildUi();
        CreateTray();
        _healthTimer.Start();
    }

    private void BuildUi()
    {
        Text            = "Fabrium";
        AutoScaleMode   = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.FromArgb(15, 23, 42);

        // Match AMTrack's exact window: 480×580
        float scale = DeviceDpi / 96f;
        ClientSize = new Size((int)(480 * scale), (int)(580 * scale));

        // Dark title bar (matches AMTrack's Electron window)
        int dark = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        using var icoStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("HybridSlicer.Launcher.icon.ico");
        if (icoStream is not null) Icon = new Icon(icoStream);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(15, 23, 42),
        };
        Controls.Add(_webView);

        InitWebView();

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    private async void InitWebView()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(Path.GetTempPath(), "Fabrium-WebView2"));
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            _webView.CoreWebView2.WebMessageReceived += OnWebMessage;

            var html = GetHtmlWithLogo();
            _webView.CoreWebView2.NavigateToString(html);

            _webReady = true;

            _webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                PushState();
                // Auto-check for updates on launch
                _ = CheckForUpdatesAsync();
            };
        }
        catch (Exception ex)
        {
            Controls.Clear();
            BackColor = Color.FromArgb(15, 23, 42);
            Controls.Add(new Label
            {
                Text = $"Fabrium is running at http://{_networkIp}:5000\n\n" +
                       $"Error: {ex.Message}",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(20),
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AUTO-UPDATER — checks GitHub releases, shows progress in UI
    // ═══════════════════════════════════════════════════════════════════════

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            SetUpdateStatus("checking");

            var source = new GithubSource("https://github.com/momoradik/hybridslicerv2", null, false);
            _updateMgr = new UpdateManager(source);

            var update = await _updateMgr.CheckForUpdatesAsync();
            if (update is null)
            {
                SetUpdateStatus("uptodate");
                return;
            }

            _updateVersion = update.TargetFullRelease.Version.ToString();
            SetUpdateStatus("downloading");

            await _updateMgr.DownloadUpdatesAsync(update, p =>
            {
                _updatePct = p;
                SetUpdateStatus("downloading");
            });

            _pendingUpdate = update;
            SetUpdateStatus("ready");
        }
        catch
        {
            SetUpdateStatus("error");
        }
    }

    private void ApplyUpdate()
    {
        if (_pendingUpdate is null || _updateMgr is null) return;
        _updateMgr.ApplyUpdatesAndRestart(_pendingUpdate);
    }

    private void SetUpdateStatus(string status)
    {
        _updateStatus = status;
        if (InvokeRequired)
            Invoke(PushState);
        else
            PushState();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  WEBVIEW MESSAGE HANDLING
    // ═══════════════════════════════════════════════════════════════════════

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var msg = args.TryGetWebMessageAsString();
        switch (msg)
        {
            case "open-browser":
                OpenUrl($"http://{_networkIp}:5000");
                break;
            case "check-updates":
                _ = CheckForUpdatesAsync();
                break;
            case "apply-update":
                ApplyUpdate();
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STATE PUSH TO WEBVIEW
    // ═══════════════════════════════════════════════════════════════════════

    private void PushState()
    {
        if (!_webReady || _webView.CoreWebView2 is null) return;

        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var json = JsonSerializer.Serialize(new
        {
            status        = _status,
            url           = $"http://{_networkIp}:5000",
            version       = ver,
            cura          = _curaPath is not null,
            update        = _updateStatus,
            updateVersion = _updateVersion,
            updatePct     = _updatePct,
        });

        try { _webView.CoreWebView2.PostWebMessageAsJson(json); } catch { }
    }

    private string GetHtmlWithLogo()
    {
        var asm = Assembly.GetExecutingAssembly();
        string html;
        using (var stream = asm.GetManifestResourceStream("HybridSlicer.Launcher.launcher.html"))
        {
            if (stream is null) return "<html><body>Error: HTML resource not found</body></html>";
            using var reader = new StreamReader(stream);
            html = reader.ReadToEnd();
        }

        using var iconStream = asm.GetManifestResourceStream("HybridSlicer.Launcher.icon.ico");
        if (iconStream is not null)
        {
            var ico = new Icon(iconStream, 64, 64);
            using var bmp = ico.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            var b64 = Convert.ToBase64String(ms.ToArray());
            html = html.Replace("src=\"\"", $"src=\"data:image/png;base64,{b64}\"");
        }

        return html;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SYSTEM TRAY
    // ═══════════════════════════════════════════════════════════════════════

    private void CreateTray()
    {
        var menu = new ContextMenuStrip();
        menu.BackColor = Color.FromArgb(30, 41, 59);
        menu.ForeColor = Color.FromArgb(248, 250, 252);
        menu.Renderer  = new DarkRenderer();

        menu.Items.Add(new ToolStripLabel("Fabrium") { ForeColor = Color.FromArgb(100, 116, 139), Enabled = false });
        menu.Items.Add(new ToolStripLabel($"http://{_networkIp}:5000") { ForeColor = Color.FromArgb(100, 116, 139), Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open in Browser", null, (_, _) => OpenUrl($"http://{_networkIp}:5000"));
        menu.Items.Add("Check for Updates", null, (_, _) => _ = CheckForUpdatesAsync());
        menu.Items.Add("Show Status Window", null, (_, _) => { Show(); BringToFront(); Activate(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Fabrium", null, (_, _) => QuitApp());

        _tray = new NotifyIcon
        {
            Icon             = Icon,
            Text             = $"Fabrium  \u00b7  http://{_networkIp}:5000",
            ContextMenuStrip = menu,
            Visible          = true,
        };
        _tray.Click += (_, _) => { Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate(); };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HEALTH POLLING
    // ═══════════════════════════════════════════════════════════════════════

    private void PollHealth()
    {
        if (_server.HasExited) { SetServerStatus("error"); return; }

        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var r = tcp.BeginConnect("127.0.0.1", 5000, null, null);
            if (r.AsyncWaitHandle.WaitOne(300)) { tcp.EndConnect(r); SetServerStatus("running"); return; }
        }
        catch { }

        SetServerStatus("starting");
    }

    private void SetServerStatus(string st)
    {
        _status = st;
        PushState();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private void QuitApp()
    {
        _healthTimer.Stop();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        try { if (!_server.HasExited) _server.Kill(entireProcessTree: true); } catch { }
        Environment.Exit(0);
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _healthTimer.Dispose(); _tray?.Dispose(); _webView?.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(248, 250, 252) : Color.FromArgb(100, 116, 139);
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color H = Color.FromArgb(30, 41, 59);
        private static readonly Color B = Color.FromArgb(51, 65, 85);
        public override Color MenuBorder => B;
        public override Color MenuItemBorder => B;
        public override Color MenuItemSelected => B;
        public override Color MenuItemSelectedGradientBegin => B;
        public override Color MenuItemSelectedGradientEnd => B;
        public override Color MenuStripGradientBegin => H;
        public override Color MenuStripGradientEnd => H;
        public override Color ToolStripDropDownBackground => H;
        public override Color ImageMarginGradientBegin => H;
        public override Color ImageMarginGradientMiddle => H;
        public override Color ImageMarginGradientEnd => H;
        public override Color SeparatorDark => B;
        public override Color SeparatorLight => B;
    }
}
