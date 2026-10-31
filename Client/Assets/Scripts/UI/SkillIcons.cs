using System.Collections.Generic;
using Game.Shared.Combat;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Ícones das skills desenhados em código: um medalhão na cor do tipo (físico quente, mágico frio) com o símbolo da
    /// skill em branco (corte, giro, runa, chama, raio...). Gerados uma vez e guardados.
    /// </summary>
    public static class SkillIcons
    {
        private const int Size = 64;

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        private abstract class Shape
        {
            public abstract float Distance(Vector2 p);
        }

        private sealed class Segment : Shape
        {
            private readonly Vector2 _a, _b;
            private readonly float _width;
            public Segment(float ax, float ay, float bx, float by, float width) { _a = new Vector2(ax, ay); _b = new Vector2(bx, by); _width = width; }

            public override float Distance(Vector2 p)
            {
                var ab = _b - _a;
                var t = Mathf.Clamp01(Vector2.Dot(p - _a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                // Mais fino nas pontas: parece lâmina, não bastão.
                var taper = Mathf.Lerp(0.45f, 1f, Mathf.Sin(t * Mathf.PI));
                return Vector2.Distance(p, _a + ab * t) - _width * taper;
            }
        }

        private sealed class Disc : Shape
        {
            private readonly Vector2 _c;
            private readonly float _r;
            public Disc(float x, float y, float r) { _c = new Vector2(x, y); _r = r; }
            public override float Distance(Vector2 p) => Vector2.Distance(p, _c) - _r;
        }

        private sealed class Ring : Shape
        {
            private readonly Vector2 _c;
            private readonly float _r, _width, _from, _to;
            public Ring(float x, float y, float r, float width, float fromDeg = 0f, float toDeg = 360f)
            {
                _c = new Vector2(x, y); _r = r; _width = width; _from = fromDeg; _to = toDeg;
            }

            public override float Distance(Vector2 p)
            {
                var d = p - _c;
                var angle = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - _from, 360f);
                if (angle > _to - _from) return 1f;
                return Mathf.Abs(d.magnitude - _r) - _width;
            }
        }

        /// <summary>O ícone da skill (64x64, fundo transparente fora do medalhão).</summary>
        public static Texture2D Get(SkillData skill)
        {
            if (skill == null) return null;
            if (Cache.TryGetValue(skill.Id, out var icon) && icon != null) return icon;
            icon = Draw(Glyph(skill.Id), ColorFor(skill));
            icon.name = "Skill " + skill.Id;
            Cache[skill.Id] = icon;
            return icon;
        }

        private static Color ColorFor(SkillData skill)
        {
            switch (skill.Id)
            {
                case "lamina_flamejante":
                case "meteoro": return new Color(0.95f, 0.42f, 0.15f);
                case "corrente_gelida": return new Color(0.3f, 0.75f, 0.95f);
                case "raio_duplo": return new Color(0.95f, 0.8f, 0.25f);
                case "cataclismo":
                case "explosao_arcana": return new Color(0.65f, 0.35f, 0.95f);
                case "golpe_runico": return new Color(0.35f, 0.55f, 1f);
                case "tiro_certeiro":
                case "flecha_perfurante":
                case "tiro_duplo":
                case "chuva_de_flechas":
                case "saraivada": return new Color(0.45f, 0.68f, 0.32f);
                case "flecha_flamejante": return new Color(0.95f, 0.42f, 0.15f);
                case "tiro_do_falcao": return new Color(0.9f, 0.72f, 0.25f);
            }
            return skill.Kind == SkillKind.Physical ? new Color(0.85f, 0.5f, 0.22f) : new Color(0.35f, 0.5f, 0.95f);
        }

        // Coordenadas de -1 a 1, y para cima.
        private static Shape[] Glyph(string id)
        {
            switch (id)
            {
                case "corte": return new Shape[] { new Segment(-0.5f, -0.5f, 0.52f, 0.52f, 0.13f) };
                case "giro_de_lamina":
                    return new Shape[] { new Ring(0, 0, 0.45f, 0.08f, 30f, 320f), new Segment(0.42f, 0.18f, 0.62f, -0.12f, 0.08f), new Segment(0.42f, 0.18f, 0.1f, 0.12f, 0.08f) };
                case "golpe_runico":
                    return new Shape[]
                    {
                        new Segment(0, 0.6f, 0.4f, 0, 0.07f), new Segment(0.4f, 0, 0, -0.6f, 0.07f), new Segment(0, -0.6f, -0.4f, 0, 0.07f),
                        new Segment(-0.4f, 0, 0, 0.6f, 0.07f), new Segment(0, 0.35f, 0, -0.35f, 0.07f),
                    };
                case "estocada_dupla":
                    return new Shape[] { new Segment(-0.55f, -0.25f, 0.55f, 0.15f, 0.1f), new Segment(-0.55f, 0.05f, 0.55f, 0.45f, 0.1f) };
                case "fenda_sismica":
                    return new Shape[]
                    {
                        new Segment(-0.15f, 0.6f, 0.1f, 0.2f, 0.07f), new Segment(0.1f, 0.2f, -0.12f, -0.1f, 0.07f), new Segment(-0.12f, -0.1f, 0.12f, -0.6f, 0.07f),
                        new Segment(-0.7f, -0.55f, 0.7f, -0.55f, 0.05f),
                    };
                case "lamina_flamejante":
                    return new Shape[] { new Disc(0, -0.22f, 0.3f), new Segment(-0.24f, -0.12f, 0.02f, 0.62f, 0.12f), new Segment(0.24f, -0.12f, 0.1f, 0.35f, 0.1f) };
                case "tempestade_de_aco":
                    return new Shape[]
                    {
                        new Segment(-0.55f, -0.45f, 0.5f, 0.5f, 0.09f), new Segment(-0.5f, 0.5f, 0.55f, -0.45f, 0.09f), new Segment(-0.65f, 0.05f, 0.65f, 0.05f, 0.08f),
                    };
                case "execucao": return new Shape[] { new Segment(-0.5f, -0.5f, 0.5f, 0.5f, 0.12f), new Segment(-0.5f, 0.5f, 0.5f, -0.5f, 0.12f) };
                case "faisca":
                    return new Shape[] { new Segment(0, 0.6f, 0, -0.6f, 0.11f), new Segment(-0.6f, 0, 0.6f, 0, 0.11f), new Disc(0, 0, 0.12f) };
                case "lanca_de_forca":
                    return new Shape[]
                    {
                        new Segment(-0.55f, -0.55f, 0.4f, 0.4f, 0.06f), new Segment(0.55f, 0.55f, 0.2f, 0.45f, 0.08f), new Segment(0.55f, 0.55f, 0.45f, 0.2f, 0.08f),
                    };
                case "golpe_de_cajado": return new Shape[] { new Segment(-0.55f, -0.6f, 0.3f, 0.3f, 0.07f), new Disc(0.38f, 0.38f, 0.17f) };
                case "explosao_arcana":
                    {
                        var shapes = new List<Shape> { new Disc(0, 0, 0.2f) };
                        for (var i = 0; i < 8; i++)
                        {
                            var a = i * Mathf.PI / 4f;
                            shapes.Add(new Segment(Mathf.Cos(a) * 0.32f, Mathf.Sin(a) * 0.32f, Mathf.Cos(a) * 0.66f, Mathf.Sin(a) * 0.66f, 0.07f));
                        }
                        return shapes.ToArray();
                    }
                case "corrente_gelida":
                    {
                        var shapes = new List<Shape>();
                        for (var i = 0; i < 3; i++)
                        {
                            var a = i * Mathf.PI / 3f + Mathf.PI / 2f;
                            shapes.Add(new Segment(Mathf.Cos(a) * 0.62f, Mathf.Sin(a) * 0.62f, -Mathf.Cos(a) * 0.62f, -Mathf.Sin(a) * 0.62f, 0.06f));
                        }
                        return shapes.ToArray();
                    }
                case "meteoro":
                    return new Shape[] { new Disc(0.22f, -0.22f, 0.26f), new Segment(-0.6f, 0.6f, 0.1f, -0.05f, 0.1f), new Segment(-0.25f, 0.65f, 0.2f, 0.1f, 0.06f) };
                case "raio_duplo":
                    return new Shape[]
                    {
                        new Segment(0.2f, 0.65f, -0.15f, 0.05f, 0.08f), new Segment(-0.15f, 0.05f, 0.18f, 0.02f, 0.08f), new Segment(0.18f, 0.02f, -0.2f, -0.65f, 0.08f),
                    };
                case "cataclismo":
                    return new Shape[] { new Ring(0, 0, 0.5f, 0.07f), new Disc(0, 0, 0.18f), new Segment(-0.62f, 0.62f, 0.62f, -0.62f, 0.05f), new Segment(-0.62f, -0.62f, 0.62f, 0.62f, 0.05f) };
                // ----- Ranger: flechas -----
                case "tiro_certeiro": return Arrow(-0.55f, -0.55f, 0.55f, 0.55f);
                case "flecha_perfurante":
                    {
                        var shapes = new List<Shape>(Arrow(-0.6f, 0f, 0.6f, 0f)) { new Ring(0.1f, 0, 0.32f, 0.05f) };
                        return shapes.ToArray();
                    }
                case "chute_de_recuo":
                    return new Shape[] { new Segment(-0.3f, 0.6f, -0.1f, -0.1f, 0.12f), new Segment(-0.1f, -0.1f, 0.5f, -0.35f, 0.12f), new Segment(0.55f, -0.1f, 0.7f, 0.2f, 0.05f) };
                case "chuva_de_flechas":
                    {
                        var shapes = new List<Shape>();
                        foreach (var x in new[] { -0.4f, 0f, 0.4f }) shapes.AddRange(Arrow(x, 0.6f, x, -0.55f));
                        return shapes.ToArray();
                    }
                case "tiro_duplo":
                    {
                        var shapes = new List<Shape>(Arrow(-0.6f, -0.3f, 0.5f, 0.3f));
                        shapes.AddRange(Arrow(-0.5f, -0.6f, 0.6f, 0f));
                        return shapes.ToArray();
                    }
                case "flecha_flamejante":
                    {
                        var shapes = new List<Shape>(Arrow(-0.6f, -0.6f, 0.45f, 0.45f)) { new Disc(0.4f, 0.4f, 0.2f) };
                        return shapes.ToArray();
                    }
                case "saraivada":
                    {
                        var shapes = new List<Shape>();
                        foreach (var x in new[] { -0.5f, -0.17f, 0.17f, 0.5f }) shapes.AddRange(Arrow(x, 0.65f, x, -0.4f));
                        shapes.Add(new Segment(-0.7f, -0.6f, 0.7f, -0.6f, 0.05f));
                        return shapes.ToArray();
                    }
                case "tiro_do_falcao":
                    {
                        var shapes = new List<Shape>(Arrow(-0.6f, 0f, 0.6f, 0f))
                        {
                            new Segment(-0.1f, 0.05f, -0.45f, 0.55f, 0.07f), new Segment(-0.1f, -0.05f, -0.45f, -0.55f, 0.07f),
                        };
                        return shapes.ToArray();
                    }
                // ----- Guardião: machado e escudo -----
                case "golpe_de_machado": return Axe(-0.5f, -0.6f, 0.35f, 0.45f);
                case "grito_de_guerra":
                    return new Shape[]
                    {
                        new Ring(-0.35f, 0, 0.25f, 0.07f), new Ring(-0.1f, 0, 0.5f, 0.06f, -50f, 50f), new Ring(-0.1f, 0, 0.75f, 0.05f, -45f, 45f),
                    };
                case "investida_de_escudo":
                    return new Shape[] { new Ring(0.1f, 0, 0.42f, 0.09f), new Disc(0.1f, 0, 0.12f), new Segment(-0.75f, 0.2f, -0.4f, 0.2f, 0.05f), new Segment(-0.75f, -0.2f, -0.4f, -0.2f, 0.05f) };
                case "golpe_esmagador":
                    {
                        var shapes = new List<Shape>(Axe(-0.45f, 0.6f, 0.2f, -0.2f)) { new Segment(-0.6f, -0.65f, 0.6f, -0.65f, 0.06f) };
                        return shapes.ToArray();
                    }
                case "desafio":
                    return new Shape[] { new Segment(0f, 0.65f, 0f, -0.15f, 0.12f), new Disc(0f, -0.5f, 0.12f), new Ring(0f, 0f, 0.78f, 0.04f, 120f, 420f) };
                case "terremoto":
                    return new Shape[]
                    {
                        new Segment(-0.7f, -0.3f, 0.7f, -0.3f, 0.05f), new Segment(0f, -0.3f, -0.2f, 0.1f, 0.06f), new Segment(-0.2f, 0.1f, 0.1f, 0.35f, 0.06f),
                        new Segment(0.1f, 0.35f, -0.05f, 0.65f, 0.05f), new Segment(-0.5f, -0.3f, -0.6f, 0.05f, 0.05f), new Segment(0.45f, -0.3f, 0.6f, 0.1f, 0.05f),
                    };
                case "furia_do_urso":
                    return new Shape[]
                    {
                        new Segment(-0.55f, 0.6f, -0.25f, -0.6f, 0.07f), new Segment(-0.15f, 0.65f, 0.1f, -0.65f, 0.07f), new Segment(0.25f, 0.6f, 0.5f, -0.6f, 0.07f),
                    };
                case "colosso":
                    {
                        var shapes = new List<Shape>(Axe(-0.4f, -0.55f, 0.3f, 0.5f)) { new Ring(0, 0, 0.7f, 0.05f) };
                        return shapes.ToArray();
                    }
            }
            return new Shape[] { new Disc(0, 0, 0.3f) };
        }

        /// <summary>Machado: cabo de (x0, y0) até (x1, y1) e a lâmina em meia-lua na ponta.</summary>
        private static Shape[] Axe(float x0, float y0, float x1, float y1)
        {
            var dir = new Vector2(x1 - x0, y1 - y0).normalized;
            var side = new Vector2(dir.y, -dir.x);
            var blade = new Vector2(x1, y1) - dir * 0.12f + side * 0.22f;
            var angle = Mathf.Atan2(side.y, side.x) * Mathf.Rad2Deg;
            return new Shape[]
            {
                new Segment(x0, y0, x1, y1, 0.06f),
                new Ring(blade.x, blade.y, 0.24f, 0.09f, angle - 70f, angle + 70f),
            };
        }

        /// <summary>Flecha de (x0, y0) até a ponta em (x1, y1): haste fina e duas farpas.</summary>
        private static Shape[] Arrow(float x0, float y0, float x1, float y1)
        {
            var dir = new Vector2(x1 - x0, y1 - y0).normalized;
            var side = new Vector2(-dir.y, dir.x);
            var back = new Vector2(x1, y1) - dir * 0.22f;
            return new Shape[]
            {
                new Segment(x0, y0, x1, y1, 0.06f),
                new Segment(x1, y1, back.x + side.x * 0.16f, back.y + side.y * 0.16f, 0.06f),
                new Segment(x1, y1, back.x - side.x * 0.16f, back.y - side.y * 0.16f, 0.06f),
            };
        }

        private static Texture2D Draw(Shape[] glyph, Color tint)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixel = 2f / Size;
            var dark = new Color(tint.r * 0.18f, tint.g * 0.18f, tint.b * 0.22f);
            var gold = UiTheme.BorderBright;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                var r = p.magnitude;

                // Medalhão: degradê do centro (cor da skill) para a borda escura, aro dourado.
                var badge = Mathf.Clamp01((0.94f - r) / pixel);
                var fill = Color.Lerp(tint * 0.85f, dark, Mathf.SmoothStep(0f, 1f, r / 0.94f));
                var rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.88f) / 0.06f);
                var color = Color.Lerp(fill, gold, rim * 0.9f);

                var d = 1f;
                foreach (var shape in glyph) d = Mathf.Min(d, shape.Distance(p));
                var glyphAlpha = Mathf.Clamp01(0.5f - d / pixel);
                var glow = Mathf.Clamp01(1f - Mathf.Max(0f, d) / 0.18f) * 0.55f;
                color = Color.Lerp(color, Color.Lerp(tint, Color.white, 0.6f), glow * (1f - rim));
                color = Color.Lerp(color, Color.white, glyphAlpha * (1f - rim));
                color.a = badge;
                texture.SetPixel(x, y, color);
            }
            texture.Apply();
            return texture;
        }
    }
}
