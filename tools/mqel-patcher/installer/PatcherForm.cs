using System.Drawing;
using System.Windows.Forms;

// Simple, clean UI for non-technical users. No-args launch shows this; the CLI stays fully functional.
class PatcherForm : Form
{
    readonly TextBox _game = new();
    readonly TextBox _launcher = new();
    readonly TextBox _server = new();
    readonly TextBox _log = new();

    static readonly Color Bg = Color.FromArgb(245, 246, 248);
    static readonly Color Card = Color.White;
    static readonly Color Accent = Color.FromArgb(45, 108, 223);
    static readonly Color Ink = Color.FromArgb(32, 36, 44);
    static readonly Color Muted = Color.FromArgb(120, 128, 140);
    static readonly Color Line = Color.FromArgb(223, 227, 233);

    public PatcherForm()
    {
        Text = "MQEL Patcher";
        Font = new Font("Segoe UI", 9f);
        BackColor = Bg; ForeColor = Ink;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 590);

        Controls.Add(new Label { Text = "MQEL Patcher", Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold), ForeColor = Ink, AutoSize = true, Location = new Point(22, 18) });
        Controls.Add(new Label { Text = "Play The Mighty Quest for Epic Loot from Steam on a private server.", ForeColor = Muted, AutoSize = true, Location = new Point(24, 54) });

        // ── 1. Game folder ───────────────────────────────────────────
        var gGame = MakeCard("1   Game folder", 20, 110, 560, 74);
        _game.SetBounds(16, 22, 402, 26); _game.BorderStyle = BorderStyle.FixedSingle; _game.Font = new Font("Consolas", 9f);
        gGame.Controls.Add(_game);
        var browse = Btn("Browse…", 426, 21, 116, 28, false); browse.Click += (_, __) => Browse(); gGame.Controls.Add(browse);

        // ── 2. Server ────────────────────────────────────────────────
        var gSrv = MakeCard("2   Server", 20, 220, 560, 132);
        gSrv.Controls.Add(Lbl("Launcher URL   —   the launcher's http address", 16, 12));
        _launcher.SetBounds(16, 32, 528, 26); _launcher.BorderStyle = BorderStyle.FixedSingle; _launcher.Font = new Font("Consolas", 9.5f);
        _launcher.Text = Program.DEFAULT_LAUNCHER_URL; gSrv.Controls.Add(_launcher);
        gSrv.Controls.Add(Lbl("Server URL   —   the game server's https address", 16, 72));
        _server.SetBounds(16, 92, 528, 26); _server.BorderStyle = BorderStyle.FixedSingle; _server.Font = new Font("Consolas", 9.5f);
        _server.Text = Program.DEFAULT_SERVER_URL; gSrv.Controls.Add(_server);

        // ── Actions ──────────────────────────────────────────────────
        var install = Btn("Install", 20, 372, 180, 44, true); install.Click += (_, __) => Do(true);
        var unin = Btn("Uninstall", 210, 372, 150, 44, false); unin.Click += (_, __) => Do(false);
        Controls.Add(install); Controls.Add(unin);

        int ly = 432;
        _log.SetBounds(20, ly, 560, ClientSize.Height - ly - 16);
        _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.FromArgb(24, 27, 33); _log.ForeColor = Color.FromArgb(210, 214, 220);
        _log.Font = new Font("Consolas", 9f); _log.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_log);

        Load += (_, __) => { try { _game.Text = Program.ResolveGamePath(null); Status("Detected your game install. Check the URLs, then click Install."); } catch { Status("Couldn't auto-detect the game — click Browse to pick the folder."); } };
    }

    static Label Lbl(string t, int x, int yy) => new() { Text = t, ForeColor = Muted, AutoSize = true, Location = new Point(x, yy) };

    // A titled card: a header label + a bordered white panel added straight to the form; returns the
    // panel so the caller can add controls into it (with body-relative coordinates).
    Panel MakeCard(string title, int x, int y, int w, int h)
    {
        Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(70, 78, 90), AutoSize = true, Location = new Point(x + 2, y - 20) });
        var body = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Card, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(body);
        return body;
    }

    static Button Btn(string t, int x, int yy, int w, int h, bool accent)
    {
        var b = new Button { Text = t, Location = new Point(x, yy), Size = new Size(w, h), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", accent ? 10.5f : 9.5f, accent ? FontStyle.Bold : FontStyle.Regular), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = accent ? 0 : 1; b.FlatAppearance.BorderColor = Line;
        b.BackColor = accent ? Accent : Color.White; b.ForeColor = accent ? Color.White : Ink;
        return b;
    }

    void Browse()
    {
        using var d = new FolderBrowserDialog { Description = "Select the game folder (…\\steamapps\\common\\The Mighty Quest For Epic Loot)" };
        if (d.ShowDialog() == DialogResult.OK) { if (Program.IsGameDir(d.SelectedPath)) { _game.Text = d.SelectedPath; Status("Game folder set."); } else Status("That isn't the game folder — pick the one with GameData and Launcher inside."); }
    }

    void Do(bool install)
    {
        string game = _game.Text.Trim();
        if (!Program.IsGameDir(game)) { Status("ERROR: set a valid game folder first (use Browse)."); return; }
        try
        {
            if (install) { Program.Install(game, _launcher.Text.Trim(), _server.Text.Trim(), Status); Status("✔ Installed. Launch the game from Steam."); }
            else { Program.Uninstall(game, Status); Status("✔ Uninstalled — everything back to default."); }
        }
        catch (System.Exception ex) { Status("ERROR: " + ex.Message); }
    }

    void Status(string s) => _log.AppendText(s + "\r\n");
}
