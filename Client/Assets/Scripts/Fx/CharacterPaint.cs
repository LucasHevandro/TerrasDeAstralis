using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Cores do personagem: a textura dos modelos do KayKit é uma paleta (grade de 8×4 cores em degradê), então trocar
    /// a cor de uma parte é repintar a célula dela. O metal segue o grau da peça vestida (Ferro, Aço Negro, Astralita):
    /// a couraça no corpo, as luvas nos braços e as botas nas pernas (cada uma é uma malha do modelo). O cabelo e a
    /// roupa seguem o que o jogador escolheu na criação. Índice 0 mantém a cor original do modelo.
    /// O metal de Astralita brilha (emissão, que o bloom espalha) e a couraça de Astralita solta faíscas azuis.
    /// Só o corpo (malhas com esqueleto) é repintado: a arma na mão tem o próprio brilho.
    /// </summary>
    public static class CharacterPaint
    {
        public static readonly Color[] HairColors =
        {
            Color.clear, // original
            new Color(0.86f, 0.68f, 0.34f), // loiro
            new Color(0.66f, 0.26f, 0.1f), // ruivo
            new Color(0.09f, 0.08f, 0.08f), // preto
            new Color(0.74f, 0.74f, 0.76f), // grisalho
            new Color(0.36f, 0.22f, 0.12f), // castanho
        };

        public static readonly Color[] ClothColors =
        {
            Color.clear, // original
            new Color(0.2f, 0.36f, 0.78f), // azul
            new Color(0.2f, 0.56f, 0.3f), // verde
            new Color(0.46f, 0.26f, 0.68f), // roxo
            new Color(0.14f, 0.14f, 0.16f), // preto
            new Color(0.92f, 0.9f, 0.86f), // branco
        };

        public static readonly Color[] SkinColors =
        {
            Color.clear, // original
            new Color(1f, 0.87f, 0.76f), // clara
            new Color(0.8f, 0.58f, 0.42f), // morena
            new Color(0.6f, 0.4f, 0.27f), // parda
            new Color(0.36f, 0.23f, 0.16f), // negra
        };

        /// <summary>Penteados (a cabeça de qual modelo): 0 = a da classe; depois os aventureiros do pacote.</summary>
        public static readonly string[] HeadModels = { "", "Knight", "Mage", "Ranger", "Rogue", "Barbarian" };

        /// <summary>Cabeça de capuz que faz o papel de elmo na Ranger (o modelo dela não tem chapéu).</summary>
        public const string RangerHood = "RogueHooded_Head";

        /// <summary>Elmo e chapéu: o jogador pode esconder.</summary>
        private static readonly string[] HelmetParts = { "Knight_Helmet", "Knight_HelmetVisor", "Mage_Hat", "Barbarian_Large_BearHat" };

        /// <summary>O que o jogador escolheu na criação (cores, penteado, pele) e se o elmo aparece.</summary>
        public readonly struct Style
        {
            public readonly int Hair;
            public readonly int Cloth;
            public readonly int Head;
            public readonly int Skin;
            public readonly bool HideHelmet;

            public Style(int hair, int cloth, int head = 0, int skin = 0, bool hideHelmet = false)
            {
                Hair = hair;
                Cloth = cloth;
                Head = head;
                Skin = skin;
                HideHelmet = hideHelmet;
            }

            public Style WithHelmet(bool hide) => new Style(Hair, Cloth, Head, Skin, hide);
        }

        /// <summary>Metal por grau da armadura (0 = sem armadura: cor original).</summary>
        public static readonly Color[] MetalColors =
        {
            Color.clear,
            new Color(0.66f, 0.68f, 0.7f), // Ferro
            new Color(0.2f, 0.19f, 0.24f), // Aço Negro
            new Color(0.45f, 0.72f, 1f), // Astralita
        };

        /// <summary>
        /// Que células da paleta são metal, cabelo e roupa em cada modelo (coluna, linha a partir de cima). Braços e pernas
        /// podem ter células a mais que seguem as luvas e as botas (os punhos e os sapatos do Mage não são metal).
        /// </summary>
        private sealed class Layout
        {
            public Vector2Int[] Metal;
            public Vector2Int[] ArmMetal = System.Array.Empty<Vector2Int>();
            public Vector2Int[] LegMetal = System.Array.Empty<Vector2Int>();
            public Vector2Int[] Hair = System.Array.Empty<Vector2Int>();
            public Vector2Int[] Cloth = System.Array.Empty<Vector2Int>();

            /// <summary>Pele (rosto e mãos): a mesma célula em todos os aventureiros do pacote.</summary>
            public Vector2Int[] Skin = { new Vector2Int(0, 0) };
        }

        private enum Part { Body, Arm, Leg }

        private static readonly Dictionary<string, Layout> Layouts = new Dictionary<string, Layout>
        {
            // Knight: armadura (3,0), detalhes (7,0) e (7,1), manopla (4,0); cabelo (1,0); tabardo e capa vermelhos (0,1).
            ["knight_texture"] = new Layout
            {
                Metal = new[] { new Vector2Int(3, 0), new Vector2Int(7, 0), new Vector2Int(7, 1) },
                ArmMetal = new[] { new Vector2Int(4, 0) },
                Hair = new[] { new Vector2Int(1, 0) },
                Cloth = new[] { new Vector2Int(0, 1) },
            },
            // Mage: metal (3,0) e a capa (2,1) seguem a armadura, os punhos (4,0) as luvas e os sapatos (3,2) as botas;
            // cabelo (1,0); túnica e chapéu (0,1) e (1,1).
            ["mage_texture"] = new Layout
            {
                Metal = new[] { new Vector2Int(3, 0), new Vector2Int(2, 1) },
                ArmMetal = new[] { new Vector2Int(4, 0) },
                LegMetal = new[] { new Vector2Int(3, 2) },
                Hair = new[] { new Vector2Int(1, 0) },
                Cloth = new[] { new Vector2Int(0, 1), new Vector2Int(1, 1) },
            },
            // Ranger: o couro marrom (7,0) e as placas (3,0) do corpo seguem a armadura, as luvas (7,2) as luvas e as pernas
            // e botas (3,2) as botas; cabelo (1,0); túnica e capa azuis (0,1).
            ["ranger_texture"] = new Layout
            {
                Metal = new[] { new Vector2Int(3, 0), new Vector2Int(7, 0) },
                ArmMetal = new[] { new Vector2Int(7, 2) },
                LegMetal = new[] { new Vector2Int(3, 2) },
                Hair = new[] { new Vector2Int(1, 0) },
                Cloth = new[] { new Vector2Int(0, 1) },
            },
            // Os outros aventureiros só emprestam a cabeça (penteado): cabelo (1,0) e pele (0,0), como nos de cima.
            ["rogue_texture"] = new Layout { Metal = System.Array.Empty<Vector2Int>(), Hair = new[] { new Vector2Int(1, 0) } },
            // Barbarian: nos outros só empresta a cabeça; no Guardião (o Barbarian grande) as fivelas e os cravos de aço
            // (3,0) e (3,1) seguem a armadura e as manoplas (4,0) as luvas.
            ["barbarian_texture"] = new Layout
            {
                Metal = new[] { new Vector2Int(3, 0), new Vector2Int(3, 1) },
                ArmMetal = new[] { new Vector2Int(4, 0) },
                Hair = new[] { new Vector2Int(1, 0) },
            },
        };

        private const int AstralGrade = 3;
        private static readonly Color AstralGlow = new Color(0.25f, 0.6f, 1f);
        private const string AuraName = "Aura de Astralita";

        private const int Size = 256; // a paleta reduzida: cada célula vira 32×64 pixels, o degradê continua liso
        private static readonly Dictionary<(Material, Part, int, int, int, int), Material> Painted = new Dictionary<(Material, Part, int, int, int, int), Material>();
        private static readonly Dictionary<Texture, Color32[]> Originals = new Dictionary<Texture, Color32[]>();

        /// <summary>Repinta o corpo do modelo dentro de <paramref name="root"/> (sem modelo ou sem paleta conhecida, nada muda).</summary>
        /// <summary>Grau de cada peça vestida (0 = nada: cor original).</summary>
        public readonly struct Gear
        {
            public readonly int Armor;
            public readonly int Gloves;
            public readonly int Boots;

            public Gear(int armor, int gloves, int boots)
            {
                Armor = armor;
                Gloves = gloves;
                Boots = boots;
            }

            /// <summary>Conjunto completo do mesmo grau (vitrines e prévias).</summary>
            public static Gear Set(int grade) => new Gear(grade, grade, grade);
        }

        public static void Apply(Transform root, int armorGrade, int hair, int cloth) => Apply(root, Gear.Set(armorGrade), new Style(hair, cloth));

        public static void Apply(Transform root, Gear gear, int hair, int cloth) => Apply(root, gear, new Style(hair, cloth));

        public static void Apply(Transform root, Gear gear, Style style)
        {
            if (root == null) return;
            SetHead(root, style.Head, style.HideHelmet);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Knight_ArmLeft, Mage_LegRight...: braços seguem as luvas, pernas as botas, o resto a couraça.
                var name = renderer.name;
                var part = name.Contains("Arm") ? Part.Arm : name.Contains("Leg") ? Part.Leg : Part.Body;
                var grade = part == Part.Arm ? gear.Gloves : part == Part.Leg ? gear.Boots : gear.Armor;
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var painted = PaintedFor(materials[i], part, grade, style.Hair, style.Cloth, style.Skin);
                    if (painted == null || painted == materials[i]) continue;
                    materials[i] = painted;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
            SetAura(root, gear.Armor >= AstralGrade);
        }

        /// <summary>
        /// Liga a cabeça do penteado escolhido (as outras ficam escondidas no prefab da classe) e mostra ou esconde o elmo
        /// e o chapéu. Penteado que o modelo não tem fica com a cabeça da própria classe.
        /// </summary>
        private static void SetHead(Transform root, int head, bool hideHelmet)
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            var own = animator.gameObject.name;
            var wanted = (head > 0 && head < HeadModels.Length ? HeadModels[head] : own) + "_Head";
            // A Ranger não tem chapéu no modelo: o "elmo" dela é a cabeça de capuz do Rogue (posta no prefab pelo
            // KayKitBuilder). À mostra, ela usa o capuz; escondendo, aparece o penteado escolhido.
            if (own == "Ranger" && !hideHelmet) wanted = RangerHood;
            var heads = new List<SkinnedMeshRenderer>();
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.name.EndsWith("_Head")) heads.Add(renderer);
            if (!heads.Exists(h => h.name == wanted)) wanted = own + "_Head";
            foreach (var renderer in heads) renderer.gameObject.SetActive(renderer.name == wanted);
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(HelmetParts, t.name) >= 0) t.gameObject.SetActive(!hideHelmet);
        }

        /// <summary>Faíscas azuis subindo devagar em volta do corpo (só com a couraça de Astralita).</summary>
        private static void SetAura(Transform root, bool on)
        {
            var existing = root.Find(AuraName);
            if (!on)
            {
                if (existing != null) Object.Destroy(existing.gameObject);
                return;
            }
            if (existing != null) return;
            var material = Resources.Load<Material>("Visuals/fx/particula_suave");
            if (material == null) return;

            var obj = new GameObject(AuraName);
            obj.transform.SetParent(root, false);
            obj.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = new Color(AstralGlow.r * 1.8f, AstralGlow.g * 1.8f, AstralGlow.b * 2f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; // ficam para trás quando o personagem anda
            main.maxParticles = 40;
            var emission = ps.emission;
            emission.rateOverTime = 9f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.75f, 1.3f, 0.75f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = obj.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
        }

        private static Material PaintedFor(Material material, Part part, int armorGrade, int hair, int cloth, int skin)
        {
            if (material == null) return null;
            // Um material já pintado guarda o original: repintar parte sempre da paleta do pacote.
            if (OriginalOf.TryGetValue(material, out var original)) material = original;
            var texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
            if (texture == null || !Layouts.TryGetValue(texture.name, out var layout)) return null;

            var metal = Pick(MetalColors, armorGrade);
            var hairColor = Pick(HairColors, hair);
            var clothColor = Pick(ClothColors, cloth);
            var skinColor = Pick(SkinColors, skin);
            if (metal.a == 0 && hairColor.a == 0 && clothColor.a == 0 && skinColor.a == 0) return material;

            var key = (material, part, armorGrade, hair, cloth, skin);
            if (Painted.TryGetValue(key, out var cached) && cached != null) return cached;

            var pixels = (Color32[])OriginalPixels(texture).Clone();
            Recolor(pixels, layout.Metal, metal);
            if (part == Part.Arm) Recolor(pixels, layout.ArmMetal, metal);
            if (part == Part.Leg) Recolor(pixels, layout.LegMetal, metal);
            Recolor(pixels, layout.Hair, hairColor);
            Recolor(pixels, layout.Cloth, clothColor);
            Recolor(pixels, layout.Skin, skinColor);
            var result = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = texture.name + "_pintada", wrapMode = TextureWrapMode.Clamp };
            result.SetPixels32(pixels);
            result.Apply(true, true);

            var painted = new Material(material) { name = material.name + " (pintado)" };
            if (painted.HasProperty("_BaseMap")) painted.SetTexture("_BaseMap", result);
            painted.mainTexture = result;
            if (armorGrade >= AstralGrade && painted.HasProperty("_EmissionMap"))
            {
                // Só as células de metal brilham: a emissão é preta no resto da paleta.
                var glow = new Color32[Size * Size];
                for (var i = 0; i < glow.Length; i++) glow[i] = new Color32(0, 0, 0, 255);
                Fill(glow, layout.Metal, AstralGlow);
                if (part == Part.Arm) Fill(glow, layout.ArmMetal, AstralGlow);
                if (part == Part.Leg) Fill(glow, layout.LegMetal, AstralGlow);
                var emission = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = texture.name + "_brilho", wrapMode = TextureWrapMode.Clamp };
                emission.SetPixels32(glow);
                emission.Apply(true, true);
                painted.SetTexture("_EmissionMap", emission);
                painted.SetColor("_EmissionColor", Color.white * 0.45f);
                painted.EnableKeyword("_EMISSION");
                painted.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            Painted[key] = painted;
            OriginalOf[painted] = material;
            return painted;
        }

        private static readonly Dictionary<Material, Material> OriginalOf = new Dictionary<Material, Material>();

        private static Color Pick(Color[] palette, int index) => index > 0 && index < palette.Length ? palette[index] : Color.clear;

        /// <summary>A paleta do pacote em 256×256, lida da GPU (a textura importada não precisa ser legível).</summary>
        private static Color32[] OriginalPixels(Texture texture)
        {
            if (Originals.TryGetValue(texture, out var pixels)) return pixels;
            var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var copy = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            pixels = copy.GetPixels32();
            Object.Destroy(copy);
            Originals[texture] = pixels;
            return pixels;
        }

        /// <summary>
        /// Pinta as células com a cor nova mantendo o degradê: cada pixel fica com o brilho relativo que tinha em
        /// relação ao meio da célula.
        /// </summary>
        private static void Recolor(Color32[] pixels, Vector2Int[] cells, Color color)
        {
            if (color.a == 0 || cells == null) return;
            const int cellWidth = Size / 8;
            const int cellHeight = Size / 4;
            foreach (var cell in cells)
            {
                var x0 = cell.x * cellWidth;
                var y0 = Size - (cell.y + 1) * cellHeight; // a linha 0 é a de cima; a textura começa embaixo
                var middle = pixels[(y0 + cellHeight / 2) * Size + x0 + cellWidth / 2];
                var reference = Mathf.Max(0.05f, Luminance(middle));
                for (var y = y0; y < y0 + cellHeight; y++)
                for (var x = x0; x < x0 + cellWidth; x++)
                {
                    var index = y * Size + x;
                    var shade = Mathf.Clamp(Luminance(pixels[index]) / reference, 0.35f, 1.6f);
                    var c = color * shade;
                    pixels[index] = new Color32(ToByte(c.r), ToByte(c.g), ToByte(c.b), pixels[index].a);
                }
            }
        }

        /// <summary>Células inteiras numa cor só (a máscara de brilho).</summary>
        private static void Fill(Color32[] pixels, Vector2Int[] cells, Color color)
        {
            const int cellWidth = Size / 8;
            const int cellHeight = Size / 4;
            var c = new Color32(ToByte(color.r), ToByte(color.g), ToByte(color.b), 255);
            foreach (var cell in cells)
            {
                var x0 = cell.x * cellWidth;
                var y0 = Size - (cell.y + 1) * cellHeight;
                for (var y = y0; y < y0 + cellHeight; y++)
                for (var x = x0; x < x0 + cellWidth; x++)
                    pixels[y * Size + x] = c;
            }
        }

        private static float Luminance(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
