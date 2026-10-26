using System.Collections.Generic;
using System.Linq;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Shared.Dungeon;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Pvp;
using Game.Shared.Text;
using Game.Shared.World;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Minimapa no canto de cima à direita (norte para cima) e mapa grande na tecla de mapa. Mostra as zonas, o corredor
    /// da dungeon ou o campo da arena, NPCs, monstros, chefes, outros jogadores (grupo em verde), itens no chão e o
    /// jogador como uma seta. Tudo lido do que o cliente já conhece; nada vem só para o mapa.
    /// </summary>
    public static class Minimap
    {
        public const float Size = 240f;
        public const float Top = 104f;

        /// <summary>Onde começam os painéis do lado direito (missões, dungeon, fila da arena), logo abaixo do minimapa.</summary>
        public const float PanelsTop = Top + Size + 12f;

        /// <summary>Área da foto do mundo visto de cima (<c>Resources/Visuals/mapa_mundo</c>): centro em xz e meia largura.</summary>
        public static readonly Vector2 WorldMapCenter = new Vector2(0f, 92f);
        public const float WorldMapHalf = 128f;

        private const float SmallRange = 45f;
        private const float BigSize = 820f;

        private static readonly Color Background = new Color(0.05f, 0.07f, 0.06f, 0.88f);
        private static readonly Color Border = new Color(1f, 0.82f, 0.35f, 0.8f);
        private static readonly Color MonsterColor = new Color(0.95f, 0.3f, 0.25f);
        private static readonly Color BossColor = new Color(1f, 0.55f, 0.1f);
        // NPC sem missão em tom neutro, para o amarelo e o azul dos marcadores de missão se destacarem.
        private static readonly Color NpcColor = new Color(0.85f, 0.82f, 0.72f);
        private static readonly Color QuestDestinationColor = new Color(1f, 0.85f, 0.2f);
        private static readonly Color PlayerColor = new Color(0.45f, 0.75f, 1f);
        private static readonly Color PartyColor = new Color(0.4f, 1f, 0.5f);
        private static readonly Color LootColor = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color[] ZoneColors =
        {
            new Color(0.75f, 0.68f, 0.5f), new Color(0.45f, 0.75f, 0.35f), new Color(0.2f, 0.45f, 0.25f),
            new Color(0.55f, 0.52f, 0.6f), new Color(0.6f, 0.35f, 0.3f),
        };

        private static readonly HashSet<int> PartyIds = new HashSet<int>(); // reaproveitado a cada quadro
        private static List<ZoneDefinition> _zones;
        private static HashSet<string> _bossIds;
        private static Texture2D _disc;
        private static Texture2D _worldPicture;
        private static bool _pictureLoaded;
        private static GUIStyle _label;
        private static GUIStyle _arrow;

        public static bool BigOpen { get; set; }

        public static void Toggle() => Fx.UiSounds.Toggle(BigOpen = !BigOpen);

        /// <summary>Mapa grande no meio da tela (chamado pelo <see cref="MapOverlay"/>, por cima das janelas).</summary>
        public static void DrawBig(Transform player, Targetable selected, float virtualWidth, float virtualHeight)
        {
            if (player == null) return;
            EnsureResources();
            Box(new Rect(0, 0, virtualWidth, virtualHeight), new Color(0, 0, 0, 0.45f));
            var rect = new Rect((virtualWidth - BigSize) / 2, (virtualHeight - BigSize) / 2 - 20, BigSize, BigSize);
            var (center, range) = BigView(player.position);
            DrawMap(rect, center, range, player, selected, big: true);
            GUI.Label(new Rect(rect.x, rect.yMax + 6, rect.width, 24),
                Loc.T("map.big_hint", KeyBindings.Label(GameAction.Map)), _label.WithCenter());
        }

        /// <summary>Minimapa no canto (dentro do OnGUI do HUD, já na tela virtual de 1920x1080).</summary>
        public static void Draw(Transform player, Targetable selected, float virtualWidth, float virtualHeight, bool smallHidden)
        {
            if (player == null || BigOpen || smallHidden) return;
            EnsureResources();
            var small = new Rect(virtualWidth - Size - 20, Top, Size, Size);
            DrawMap(small, player.position, SmallRange, player, selected, big: false);
            GUI.Label(new Rect(small.x, small.yMax - 22, small.width - 6, 20),
                $"<size=12>{KeyBindings.Label(GameAction.Map)}: {Loc.T("map.title")}</size>", _label.WithAlignment(TextAnchor.LowerRight));
        }

        /// <summary>No mapa aberto, o mundo inteiro; na dungeon e na arena, em volta do jogador.</summary>
        private static (Vector3 Center, float Range) BigView(Vector3 player)
        {
            if (CharacterState.Dungeon != null || IsArena) return (player, 90f);
            return (new Vector3(0, 0, 92f), 118f);
        }

        private static bool IsArena => CharacterState.Pvp != null && CharacterState.Pvp.Mode == PvpMode.Arena;

        private static void DrawMap(Rect rect, Vector3 center, float range, Transform player, Targetable selected, bool big)
        {
            Box(rect, Background);
            GUI.BeginGroup(rect);
            var local = new Rect(0, 0, rect.width, rect.height);
            var scale = rect.width / 2f / range;

            Vector2 ToMap(float x, float z) => new Vector2(local.width / 2 + (x - center.x) * scale, local.height / 2 - (z - center.z) * scale);

            var dungeon = CharacterState.Dungeon != null ? DungeonCatalog.Find(CharacterState.Dungeon.DungeonId) : null;
            if (dungeon != null) DrawDungeon(dungeon, CharacterState.Dungeon.OriginX, CharacterState.Dungeon.OriginZ, ToMap, scale);
            else if (IsArena) DrawArena(CharacterState.Pvp, ToMap, scale);
            else DrawZones(ToMap, scale, big);

            // NPCs
            var session = NetworkSession.Instance;
            if (session != null && dungeon == null && !IsArena)
            {
                foreach (var npc in NpcCatalog.All)
                {
                    var p = ToMap(npc.X, npc.Z);
                    // Quem tem missão para dar ou receber aparece com a cor e o símbolo do marcador ("!" ou "?").
                    var marker = GameWindows.QuestMarker(npc);
                    Dot(p, big ? 11 : 8, marker?.Color ?? NpcColor);
                    if (marker is { } m)
                        GUI.Label(new Rect(p.x - 12, p.y - (big ? 26 : 22), 24, 18), $"<b>{m.Text}</b>", _label.WithCenter());
                    // Nome só de quem dá missões: os NPCs da vila ficam juntos e os nomes se sobrepunham.
                    if (big && npc.Role == NpcRole.QuestGiver)
                        GUI.Label(new Rect(p.x - 70, p.y + 6, 140, 18), $"<size=12>{npc.Name}</size>", _label.WithCenter());
                }

                // Destino das missões aceitas: no minimapa, preso à borda quando está longe.
                foreach (var progress in CharacterState.Quests.Active)
                {
                    var quest = QuestCatalog.Find(progress.QuestId);
                    if (quest == null || !(QuestGuide.For(quest, progress.Counts) is { } destination) || destination.NpcId != "") continue;
                    var p = ToMap(destination.X, destination.Z);
                    p.x = Mathf.Clamp(p.x, 8, local.width - 8);
                    p.y = Mathf.Clamp(p.y, 8, local.height - 8);
                    Ring(p, big ? 18 : 12, QuestDestinationColor);
                }
            }

            // Itens no chão
            foreach (var loot in LootPickup.All)
            {
                if (loot == null) continue;
                var pos = loot.transform.position;
                Dot(ToMap(pos.x, pos.z), big ? 6 : 4, LootColor);
            }

            // Monstros e outros jogadores
            var party = PartyIds;
            party.Clear();
            var members = SocialWindows.Instance?.Party?.Members;
            if (members != null) foreach (var member in members) party.Add(member.Id);
            if (session != null)
            {
                foreach (var view in session.Views.Values)
                {
                    if (view == null || view.IsDead) continue;
                    var pos = view.transform.position;
                    var p = ToMap(pos.x, pos.z);
                    if (view.Kind == EntityKind.Monster)
                    {
                        var boss = _bossIds.Contains(view.TypeId);
                        Dot(p, boss ? (big ? 16 : 12) : (big ? 7 : 5), boss ? BossColor : MonsterColor);
                    }
                    else Dot(p, big ? 10 : 7, party.Contains(view.Id) ? PartyColor : PlayerColor);
                }
            }
            else
            {
                foreach (var target in Targetable.All)
                {
                    if (target == null || !target.IsAlive || target.transform == player) continue;
                    var pos = target.transform.position;
                    Dot(ToMap(pos.x, pos.z), big ? 7 : 5, MonsterColor);
                }
            }

            // Alvo selecionado
            if (selected != null)
            {
                var pos = selected.transform.position;
                Ring(ToMap(pos.x, pos.z), big ? 20 : 14, Color.white);
            }

            var me = ToMap(player.position.x, player.position.z) + rect.position;
            GUI.EndGroup();
            UiTheme.Frame(rect);

            // O jogador: seta virada para onde ele olha. Desenhada fora do grupo (a rotação usa a tela virtual inteira).
            if (!rect.Contains(me)) return;
            var matrix = GUI.matrix;
            GUI.matrix = matrix * Matrix4x4.TRS(me, Quaternion.Euler(0, 0, player.eulerAngles.y), Vector3.one) *
                         Matrix4x4.TRS(-(Vector3)me, Quaternion.identity, Vector3.one);
            var arrow = big ? 30f : 24f;
            _arrow.fontSize = Mathf.RoundToInt(arrow);
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(me.x - arrow / 2 + 1, me.y - arrow / 2 + 1, arrow, arrow), "▲", _arrow);
            GUI.color = Color.white;
            GUI.Label(new Rect(me.x - arrow / 2, me.y - arrow / 2, arrow, arrow), "▲", _arrow);
            GUI.matrix = matrix;
        }

        private static void DrawZones(System.Func<float, float, Vector2> toMap, float scale, bool big)
        {
            // Fundo: o mundo fotografado de cima. Sem a foto, só os discos das zonas.
            if (_worldPicture != null)
            {
                var topLeft = toMap(WorldMapCenter.x - WorldMapHalf, WorldMapCenter.y + WorldMapHalf);
                var size = WorldMapHalf * 2 * scale;
                GUI.DrawTexture(new Rect(topLeft.x, topLeft.y, size, size), _worldPicture);
            }
            for (var i = 0; i < _zones.Count; i++)
            {
                var zone = _zones[i];
                var c = ZoneColors[i % ZoneColors.Length];
                var p = toMap(zone.CenterX, zone.CenterZ);
                var radius = zone.Radius * scale;
                GUI.color = new Color(c.r, c.g, c.b, _worldPicture != null ? (big ? 0.16f : 0.08f) : 0.42f);
                GUI.DrawTexture(new Rect(p.x - radius, p.y - radius, radius * 2, radius * 2), _disc);
                GUI.color = Color.white;
                if (big)
                {
                    var levels = zone.MinLevel == zone.MaxLevel ? $"{zone.MinLevel}" : $"{zone.MinLevel}–{zone.MaxLevel}";
                    // Com sombra, para ler por cima da foto.
                    var text = $"<b>{zone.Name}</b>\n<size=13>{Loc.T("map.zone_levels", levels)}</size>";
                    var labelRect = new Rect(p.x - 140, p.y - radius + 6, 280, 40);
                    GUI.color = new Color(0, 0, 0, 0.9f);
                    GUI.Label(new Rect(labelRect.x + 2, labelRect.y + 2, labelRect.width, labelRect.height), text, _label.WithCenter());
                    GUI.color = Color.white;
                    GUI.Label(labelRect, text, _label.WithCenter());
                }
            }
        }

        private static void DrawDungeon(DungeonDefinition dungeon, float originX, float originZ, System.Func<float, float, Vector2> toMap, float scale)
        {
            var a = toMap(originX - dungeon.HalfWidth, originZ + dungeon.Length);
            var b = toMap(originX + dungeon.HalfWidth, originZ);
            Box(new Rect(a.x, a.y, b.x - a.x, b.y - a.y), new Color(0.35f, 0.33f, 0.38f, 0.7f));
            var state = CharacterState.Dungeon;
            for (var i = 0; i < dungeon.Rooms.Count - 1; i++)
            {
                var gate = toMap(originX - dungeon.HalfWidth, originZ + dungeon.Rooms[i].GateZ);
                var open = state != null && i < state.Room;
                Box(new Rect(gate.x, gate.y - 2, dungeon.HalfWidth * 2 * scale, 4), open ? new Color(0.4f, 0.9f, 0.4f, 0.8f) : new Color(1f, 0.4f, 0.3f, 0.9f));
            }
        }

        private static void DrawArena(PvpState pvp, System.Func<float, float, Vector2> toMap, float scale)
        {
            var a = toMap(pvp.OriginX - ArenaRules.HalfWidth, pvp.OriginZ + ArenaRules.HalfLength);
            var b = toMap(pvp.OriginX + ArenaRules.HalfWidth, pvp.OriginZ - ArenaRules.HalfLength);
            Box(new Rect(a.x, a.y, b.x - a.x, b.y - a.y), new Color(0.4f, 0.36f, 0.3f, 0.7f));
            foreach (var team in new[] { ArenaTeam.Blue, ArenaTeam.Red })
            {
                var (bx, bz) = ArenaRules.BaseOf(team);
                var p = toMap(pvp.OriginX + bx, pvp.OriginZ + bz);
                Box(new Rect(p.x - 7 * scale, p.y - 3 * scale, 14 * scale, 6 * scale), TeamColor(team, 0.6f));
            }
            for (var i = 0; i < ArenaRules.Points.Count; i++)
            {
                var point = ArenaRules.Points[i];
                var p = toMap(pvp.OriginX + point.X, pvp.OriginZ + point.Z);
                var owner = i < pvp.Points.Count ? (ArenaTeam)pvp.Points[i].Owner : ArenaTeam.None;
                var radius = ArenaRules.CaptureRadius * scale;
                GUI.color = TeamColor(owner, 0.55f);
                GUI.DrawTexture(new Rect(p.x - radius, p.y - radius, radius * 2, radius * 2), _disc);
                GUI.color = Color.white;
                GUI.Label(new Rect(p.x - 20, p.y - 12, 40, 24), $"<b>{point.Name}</b>", _label.WithCenter());
            }
        }

        private static Color TeamColor(ArenaTeam team, float alpha) => team switch
        {
            ArenaTeam.Blue => new Color(0.3f, 0.55f, 1f, alpha),
            ArenaTeam.Red => new Color(1f, 0.35f, 0.3f, alpha),
            _ => new Color(0.8f, 0.8f, 0.8f, alpha * 0.6f),
        };

        private static void Dot(Vector2 p, float size, Color color)
        {
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(p.x - size / 2 - 1, p.y - size / 2 - 1, size + 2, size + 2), _disc);
            GUI.color = color;
            GUI.DrawTexture(new Rect(p.x - size / 2, p.y - size / 2, size, size), _disc);
            GUI.color = Color.white;
        }

        private static void Ring(Vector2 p, float size, Color color)
        {
            var half = size / 2;
            Box(new Rect(p.x - half, p.y - half, size, 2), color);
            Box(new Rect(p.x - half, p.y + half - 2, size, 2), color);
            Box(new Rect(p.x - half, p.y - half, 2, size), color);
            Box(new Rect(p.x + half - 2, p.y - half, 2, size), color);
        }

        private static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Outline(Rect rect, Color color)
        {
            Box(new Rect(rect.x, rect.y, rect.width, 2), color);
            Box(new Rect(rect.x, rect.yMax - 2, rect.width, 2), color);
            Box(new Rect(rect.x, rect.y, 2, rect.height), color);
            Box(new Rect(rect.xMax - 2, rect.y, 2, rect.height), color);
        }

        private static void EnsureResources()
        {
            _zones ??= WorldContent.Zones();
            if (!_pictureLoaded)
            {
                _pictureLoaded = true;
                _worldPicture = Resources.Load<Texture2D>("Visuals/mapa_mundo");
            }
            if (_bossIds == null)
            {
                _bossIds = new HashSet<string>(WorldContent.Spawns().Where(s => s.IsBoss).Select(s => s.Data.Id));
                foreach (var dungeon in DungeonCatalog.All()) _bossIds.Add(dungeon.BossId);
            }
            if (_disc == null)
            {
                // Disco branco com borda suave, pintado com GUI.color (zonas, pontos e círculos de captura).
                const int size = 64;
                _disc = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    var alpha = Mathf.Clamp01((1f - d) * size / 3f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
                _disc.SetPixels32(pixels);
                _disc.Apply();
            }
            if (_label == null)
            {
                UiTheme.Ensure();
                _label = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = false };
                _label.normal.textColor = Color.white;
                _arrow = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = 24 };
            }
        }
    }
}
