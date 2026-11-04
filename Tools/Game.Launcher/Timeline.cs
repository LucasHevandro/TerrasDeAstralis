using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Game.Launcher
{
    /// <summary>
    /// Novidades em linha do tempo: uma linha vertical com um marcador por versão (a mais nova em cima, em dourado), a
    /// data e o número, e as novidades embaixo. Rola quando não cabe.
    /// </summary>
    public sealed class Timeline : Panel
    {
        private const int LineX = 16;
        private const int TextX = 36;

        private readonly Color _gold;
        private readonly Color _text;
        private readonly Color _muted;
        private readonly Font _title;
        private readonly Font _body;
        private readonly Canvas _canvas = new Canvas();
        private List<ReleaseNote> _notes = new List<ReleaseNote>();
        private string _message = "";

        public Timeline(Color gold, Color text, Color muted)
        {
            _gold = gold;
            _text = text;
            _muted = muted;
            _title = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _body = new Font("Segoe UI", 9.5f);
            AutoScroll = true;
            _canvas.Location = Point.Empty;
            _canvas.Paint += (_, e) => Draw(e.Graphics);
            Controls.Add(_canvas);
        }

        /// <summary>Um aviso no lugar da linha do tempo (procurando, sem conexão...).</summary>
        public void ShowMessage(string message)
        {
            _message = message;
            _notes = new List<ReleaseNote>();
            Relayout();
        }

        public void Show(IEnumerable<ReleaseNote> notes)
        {
            _message = "";
            _notes = notes.ToList();
            Relayout();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Relayout();
        }

        private int TextWidth => Math.Max(100, ClientSize.Width - TextX - SystemInformation.VerticalScrollBarWidth - 8);

        private string Body(ReleaseNote note) =>
            string.IsNullOrWhiteSpace(note.Notes) ? "Sem novidades nesta versão." : note.Notes.Replace("\n", Environment.NewLine);

        private static string Title(ReleaseNote note, bool current)
        {
            var date = note.Date is { } d ? d.ToString("dd/MM/yyyy HH:mm") + " · " : "";
            return date + "versão " + note.Version + (current ? " (atual)" : "");
        }

        /// <summary>Altura de cada versão: o título e as novidades quebradas na largura disponível.</summary>
        private int Measure(ReleaseNote note) =>
            22 + TextRenderer.MeasureText(Body(note), _body, new Size(TextWidth, int.MaxValue), TextFormatFlags.WordBreak).Height + 14;

        private void Relayout()
        {
            var height = _message.Length > 0 ? ClientSize.Height : 8 + _notes.Sum(Measure);
            _canvas.BackColor = BackColor;
            _canvas.Size = new Size(Math.Max(1, ClientSize.Width - (height > ClientSize.Height ? SystemInformation.VerticalScrollBarWidth : 0)), Math.Max(ClientSize.Height, height));
            _canvas.Invalidate();
        }

        private void Draw(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (_message.Length > 0)
            {
                TextRenderer.DrawText(g, _message, _body, new Rectangle(10, 8, _canvas.Width - 20, _canvas.Height - 16), _text,
                    TextFormatFlags.WordBreak);
                return;
            }
            if (_notes.Count == 0) return;

            var y = 8;
            var tops = new List<int>();
            foreach (var note in _notes)
            {
                tops.Add(y);
                y += Measure(note);
            }
            using (var line = new Pen(Color.FromArgb(70, _gold), 2f))
                g.DrawLine(line, LineX, tops[0] + 8, LineX, tops[tops.Count - 1] + 8);

            for (var i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];
                var top = tops[i];
                var current = i == 0;
                var dot = new Rectangle(LineX - (current ? 6 : 4), top + 8 - (current ? 6 : 4), current ? 12 : 8, current ? 12 : 8);
                using (var fill = new SolidBrush(current ? _gold : Color.FromArgb(18, 28, 54))) g.FillEllipse(fill, dot);
                using (var ring = new Pen(_gold, 2f)) g.DrawEllipse(ring, dot);
                TextRenderer.DrawText(g, Title(note, current), _title, new Point(TextX, top), current ? _gold : _muted);
                TextRenderer.DrawText(g, Body(note), _body, new Rectangle(TextX, top + 22, TextWidth, 2000), _text, TextFormatFlags.WordBreak);
            }
        }

        /// <summary>A superfície que rola dentro do painel (o painel só cuida da barra de rolagem).</summary>
        private sealed class Canvas : Control
        {
            public Canvas()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }
        }
    }
}
