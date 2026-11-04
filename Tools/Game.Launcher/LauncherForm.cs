using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Game.Launcher
{
    /// <summary>
    /// Janela do launcher: o brasão e o nome no alto, as novidades da versão, se o servidor está no ar, a barra de
    /// progresso e o botão Jogar (liberado quando a pasta já está igual à versão publicada).
    /// </summary>
    public sealed class LauncherForm : Form
    {
        private static readonly Color Night = Color.FromArgb(10, 16, 34);
        private static readonly Color Blue = Color.FromArgb(28, 52, 96);
        private static readonly Color Gold = Color.FromArgb(244, 200, 86);
        private static readonly Color GoldDark = Color.FromArgb(158, 108, 32);
        private static readonly Color TextColor = Color.FromArgb(226, 232, 244);
        private static readonly Color Muted = Color.FromArgb(150, 164, 190);

        private readonly Updater _updater;
        private readonly string[] _args;
        private readonly Image? _crest;
        private readonly Timeline _notes;
        private readonly Label _status;
        private readonly Label _server;
        private readonly Label _version;
        private readonly Button _play;
        private long _done;
        private long _total = 1;
        private Release? _release;
        private bool _ready;

        public LauncherForm(Updater updater, string[] args)
        {
            _updater = updater;
            _args = args;
            using (var stream = typeof(LauncherForm).Assembly.GetManifestResourceStream("brasao.png"))
                _crest = stream != null ? Image.FromStream(stream) : null;

            Text = "Terras de Astralis";
            ClientSize = new Size(760, 440);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Night;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (ArgumentException) { }

            Controls.Add(new Label
            {
                Text = "Novidades", ForeColor = Gold, BackColor = Color.Transparent, AutoSize = true,
                Font = new Font("Georgia", 11f, FontStyle.Bold), Location = new Point(28, 150),
            });
            _notes = new Timeline(Gold, TextColor, Muted)
            {
                BackColor = Color.FromArgb(18, 28, 54), Location = new Point(28, 178), Size = new Size(704, 150),
            };
            _notes.ShowMessage("Procurando a versão mais nova...");
            Controls.Add(_notes);

            _server = new Label { ForeColor = Muted, BackColor = Color.Transparent, AutoSize = true, Location = new Point(560, 152), Text = "" };
            Controls.Add(_server);
            _status = new Label { ForeColor = TextColor, BackColor = Color.Transparent, AutoEllipsis = true, Location = new Point(28, 344), Size = new Size(520, 22) };
            Controls.Add(_status);
            _version = new Label { ForeColor = Muted, BackColor = Color.Transparent, AutoSize = true, Location = new Point(28, 404), Text = "" };
            Controls.Add(_version);

            _play = new Button
            {
                Text = "JOGAR", Enabled = false, FlatStyle = FlatStyle.Flat, Size = new Size(184, 56), Location = new Point(548, 362),
                BackColor = Gold, ForeColor = Night, Font = new Font("Georgia", 15f, FontStyle.Bold), Cursor = Cursors.Hand,
            };
            _play.FlatAppearance.BorderColor = GoldDark;
            _play.FlatAppearance.BorderSize = 2;
            _play.Click += OnPlay;
            Controls.Add(_play);
            AcceptButton = _play;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var server = ShowServerAsync();
            await UpdateAsync();
            // --foto ARQUIVO (testes): guarda a janela pronta e fecha.
            var photo = Array.IndexOf(_args, "--foto");
            if (photo >= 0 && photo + 1 < _args.Length)
            {
                await server;
                await Task.Delay(300);
                using var bitmap = new Bitmap(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(_args[photo + 1], System.Drawing.Imaging.ImageFormat.Png);
                Close();
            }
        }

        private async Task UpdateAsync()
        {
            SetReady(false, "Procurando atualização...");
            var progress = new Progress<Report>(report =>
            {
                _status.Text = report.Text;
                _done = report.Done;
                _total = Math.Max(1, report.Total);
                Invalidate(new Rectangle(0, 368, 540, 30));
            });
            try
            {
                var release = await _updater.FetchAsync();
                _release = release;
                _notes.Show(release.Timeline());
                _version.Text = "Versão " + release.Version;
                var missing = await Task.Run(() => _updater.Missing(release, progress));
                if (missing.Count > 0)
                {
                    if (Updater.GameRunning(release))
                    {
                        SetRetry("Feche o jogo para atualizar e clique em Tentar de novo.");
                        return;
                    }
                    var replaced = await Task.Run(() => _updater.ApplyAsync(release, missing, progress));
                    if (replaced)
                    {
                        // O launcher novo termina o serviço (e mostra a janela de novo).
                        Process.Start(new ProcessStartInfo(Path.Combine(_updater.Folder, Updater.LauncherFile), string.Join(" ", Quote(_args)))
                        {
                            WorkingDirectory = _updater.Folder, UseShellExecute = false,
                        });
                        Close();
                        return;
                    }
                }
                _done = _total = 1;
                SetReady(true, "Pronto para jogar.");
            }
            catch (Exception exception)
            {
                var installed = _updater.Installed;
                var game = Path.Combine(_updater.Folder, installed?.Game ?? "TerrasDeAstralis.exe");
                if (_release == null && File.Exists(game))
                {
                    _release = installed ?? new Release();
                    _notes.ShowMessage("Não deu para procurar atualização agora (sem internet ou o servidor está fora do ar)." +
                                       Environment.NewLine + "Dá para abrir a versão instalada.");
                    if (installed != null) _version.Text = "Versão " + installed.Version;
                    _done = _total = 1;
                    SetReady(true, "Sem conexão com a atualização.");
                }
                else SetRetry("Falhou: " + exception.Message);
            }
        }

        private async Task ShowServerAsync()
        {
            var url = Updater.HealthUrlFrom(_updater.Folder);
            if (url == null) return;
            _server.Text = "● Servidor: conferindo...";
            var health = await _updater.GetTextAsync(url);
            if (health != null && health.Contains("\"status\":\"ok\""))
            {
                var players = Regex.Match(health, "\"players\":(\\d+)");
                _server.ForeColor = Color.FromArgb(110, 220, 130);
                _server.Text = "● Servidor no ar" + (players.Success ? $" · {players.Groups[1].Value} jogando" : "");
            }
            else
            {
                _server.ForeColor = Color.FromArgb(235, 110, 100);
                _server.Text = "● Servidor fora do ar";
            }
        }

        private void SetReady(bool ready, string status)
        {
            _ready = ready;
            _status.Text = status;
            _play.Text = "JOGAR";
            _play.Enabled = ready;
            _play.BackColor = ready ? Gold : Color.FromArgb(90, 96, 112);
            Invalidate();
        }

        private void SetRetry(string status)
        {
            _ready = false;
            _status.Text = status;
            _play.Text = "Tentar de novo";
            _play.Font = new Font("Georgia", 12f, FontStyle.Bold);
            _play.Enabled = true;
            _play.BackColor = Gold;
            Invalidate();
        }

        private async void OnPlay(object? sender, EventArgs e)
        {
            if (!_ready)
            {
                _play.Font = new Font("Georgia", 15f, FontStyle.Bold);
                await UpdateAsync();
                return;
            }
            var game = Path.Combine(_updater.Folder, _release?.Game ?? "TerrasDeAstralis.exe");
            if (!File.Exists(game))
            {
                SetRetry("O jogo não está na pasta. Clique para baixar de novo.");
                return;
            }
            Process.Start(new ProcessStartInfo(game) { WorkingDirectory = _updater.Folder, UseShellExecute = false });
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Faixa do alto: azul que escurece, filete dourado embaixo, brasão e o nome.
            var header = new Rectangle(0, 0, ClientSize.Width, 132);
            using (var brush = new LinearGradientBrush(header, Blue, Night, LinearGradientMode.Vertical)) g.FillRectangle(brush, header);
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(-40, -70, 360, 240);
                using var brush = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, 110, 200, 255), SurroundColors = new[] { Color.FromArgb(0, 110, 200, 255) } };
                g.FillPath(brush, glow);
            }
            using (var line = new Pen(GoldDark, 2f)) g.DrawLine(line, 0, 131, ClientSize.Width, 131);
            if (_crest != null) g.DrawImage(_crest, new Rectangle(28, 14, 104, 104));
            using (var title = new Font("Georgia", 28f, FontStyle.Bold))
            using (var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
            using (var gold = new LinearGradientBrush(new Rectangle(150, 26, 400, 50), Color.FromArgb(255, 230, 150), Gold, LinearGradientMode.Vertical))
            {
                g.DrawString("Terras de Astralis", title, shadow, 152, 30);
                g.DrawString("Terras de Astralis", title, gold, 150, 28);
            }

            // Barra de progresso.
            var bar = new Rectangle(28, 372, 504, 18);
            using (var back = new SolidBrush(Color.FromArgb(18, 28, 54))) FillRounded(g, back, bar, 8);
            var fraction = Math.Min(1.0, Math.Max(0.0, _done / (double)_total));
            if (fraction > 0.005)
            {
                var filled = new Rectangle(bar.X, bar.Y, Math.Max(16, (int)(bar.Width * fraction)), bar.Height);
                using var brush = new LinearGradientBrush(filled, Color.FromArgb(120, 220, 255), Color.FromArgb(40, 130, 220), LinearGradientMode.Vertical);
                FillRounded(g, brush, filled, 8);
            }
            using (var border = new Pen(Color.FromArgb(60, 80, 120))) DrawRounded(g, border, bar, 8);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void FillRounded(Graphics g, Brush brush, Rectangle r, int radius)
        {
            using var path = Rounded(r, radius);
            g.FillPath(brush, path);
        }

        private static void DrawRounded(Graphics g, Pen pen, Rectangle r, int radius)
        {
            using var path = Rounded(r, radius);
            g.DrawPath(pen, path);
        }

        private static string[] Quote(string[] args) => Array.ConvertAll(args, a => a.Contains(" ") ? "\"" + a + "\"" : a);
    }
}
