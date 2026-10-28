using System.Collections.Generic;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Client.Player;
using Game.Client.World;
using Game.Shared.Combat;
using Game.Shared.Progression;
using Game.Shared.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.UI
{
    /// <summary>
    /// HUD do protótipo em IMGUI: barra de combo, SP, skills, alvo e mensagens.
    /// Desenhado numa tela virtual de 1920x1080 escalada para a resolução real.
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;

        [SerializeField] private PlayerCombat combat;

        private GUIStyle _label;
        private GUIStyle _center;
        private GUIStyle _big;
        private GUIStyle _small;
        private GUIStyle _key;

        private static readonly Color Panel = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color BarBack = new Color(0.12f, 0.12f, 0.14f, 0.9f);
        private static readonly Color SpColor = new Color(0.95f, 0.75f, 0.2f);
        private static readonly Color HpColor = new Color(0.85f, 0.25f, 0.25f);
        private static readonly Color MpColor = new Color(0.25f, 0.45f, 0.95f);
        private static readonly Color WindowColor = new Color(0.25f, 0.75f, 0.35f, 0.85f);
        private static readonly Color ExcellentColor = new Color(1f, 0.9f, 0.3f, 0.95f);
        private static readonly Color PhysicalColor = new Color(0.95f, 0.55f, 0.25f);
        private static readonly Color MagicalColor = new Color(0.45f, 0.6f, 1f);
        private static readonly Color RouteColor = new Color(0.85f, 0.65f, 1f);

        private PlayerProgression _progression;
        private PlayerLoot _loot;
        private bool _showInventory;

        private static string _announcement;
        private static float _announcementAt = float.NegativeInfinity;

        /// <summary>Mostra um aviso grande no topo da tela por alguns segundos.</summary>
        public static void Announce(string text)
        {
            _announcement = text;
            _announcementAt = Time.unscaledTime;
        }

        public void SetCombat(PlayerCombat playerCombat) => combat = playerCombat;

        private void Awake()
        {
            if (combat == null) combat = FindAnyObjectByType<PlayerCombat>();
            if (combat != null)
            {
                _progression = combat.GetComponent<PlayerProgression>();
                _loot = combat.GetComponent<PlayerLoot>();
            }
        }

        private void Update()
        {
            var keyboard = GameInput.Keyboard;
            // Online o inventário de verdade fica nas janelas do jogo (GameWindows).
            if (KeyBindings.Pressed(GameAction.Inventory) && !GameMode.IsOnline) _showInventory = !_showInventory;
            if (KeyBindings.Pressed(GameAction.Map)) Minimap.Toggle();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !OptionsWindow.EscapeTaken) Minimap.BigOpen = false;
        }

        // Só GUI (sem GUILayout): sem a passada de layout, o OnGUI roda menos vezes por quadro.
        private void OnEnable() => useGUILayout = false;

        private void OnGUI()
        {
            if (combat == null || combat.Combo == null) return;
            EnsureStyles();

            // Plaquinhas dos monstros ficam em coordenadas de tela, antes da escala do resto do HUD.
            if (!OptionsWindow.IsOpen) DrawNameplates();

            var scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            GUI.matrix = Matrix4x4.TRS(
                new Vector3((Screen.width - VirtualWidth * scale) / 2, (Screen.height - VirtualHeight * scale) / 2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));

            if (Event.current.type == EventType.Repaint) PanelRects.Clear();
            PanelRects.Add(new Rect(VirtualWidth - 460, 20, 440, 74)); // zona e conexão
            PanelRects.Add(PlayerFrameRect); // quadro do jogador
            if (!Minimap.BigOpen && !_showInventory && !GameWindows.RightPanelOpen)
                PanelRects.Add(new Rect(VirtualWidth - Minimap.Size - 20, Minimap.Top, Minimap.Size, Minimap.Size));
            DrawTargetFrame();
            DrawComboBar();
            DrawPickupHint();
            DrawAwakening();
            DrawPlayerFrame();
            DrawSkillBar();
            DrawGradePopup();
            DrawMessage();
            DrawXpBar();
            DrawZone();
            DrawNetwork();
            DrawAnnouncement();
            DrawDeathOverlay();
            if (_showInventory) DrawInventory();
            // Com o inventário (ou outra janela do lado direito) aberto, o minimapa sai da frente.
            Minimap.Draw(combat.transform, combat.Target, VirtualWidth, VirtualHeight, _showInventory || GameWindows.RightPanelOpen);

            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
        }

        private readonly string[] _skillKeys = new string[SkillTraining.HotbarSize];
        private int _skillKeysVersion = -1;

        /// <summary>Tecla da casa (fileira de cima com "A+", de Alt), guardada até os atalhos mudarem.</summary>
        private string SlotKeyLabel(int slot)
        {
            if (_skillKeysVersion != KeyBindings.Version)
            {
                for (var i = 0; i < _skillKeys.Length; i++)
                {
                    var key = KeyBindings.Label(GameAction.Skill1 + i % SkillTraining.HotbarRow);
                    _skillKeys[i] = "<b>" + (i >= SkillTraining.HotbarRow ? "A+" + key : key) + "</b>";
                }
                _skillKeysVersion = KeyBindings.Version;
            }
            return slot < _skillKeys.Length ? _skillKeys[slot] : "";
        }

        /// <summary>Retângulos do HUD no quadro atual (tela virtual): os nomes dos NPCs não são escritos por cima deles.</summary>
        public static readonly List<Rect> PanelRects = new List<Rect>();

        private void DrawTargetFrame()
        {
            var target = combat.Target;
            if (target == null) return;

            var rect = new Rect(VirtualWidth / 2 - 220, 24, 440, 74);
            UiTheme.Panel(rect, 0.78f);
            GUI.Label(new Rect(rect.x, rect.y + 6, rect.width, 24), target.DisplayName, _center);
            var hp = target.Hp;
            Bar(new Rect(rect.x + 16, rect.y + 34, rect.width - 32, 16), (float)hp.Current / Mathf.Max(1, hp.Max), HpColor);
            GUI.Label(new Rect(rect.x, rect.y + 32, rect.width, 20), Loc.T("hud.hp_value", hp.Current, hp.Max), _small.WithCenter());

            var dummy = target.GetComponent<TrainingDummy>();
            if (dummy != null)
                GUI.Label(new Rect(rect.x, rect.y + 52, rect.width, 20), Loc.T("hud.dps", dummy.Dps, dummy.TotalDamage), _small.WithCenter());
        }

        /// <summary>Perto de um item no chão: mostra a tecla para pegar e o nome dele (passar por cima não pega).</summary>
        private void DrawPickupHint()
        {
            if (_loot == null) _loot = combat.GetComponent<PlayerLoot>();
            var pickup = _loot != null ? _loot.NearestPickup : null;
            if (pickup == null) return;
            var color = ColorUtility.ToHtmlStringRGB(LootPickup.ColorFor(pickup.Drop.Rarity));
            var name = pickup.Drop.Quantity > 1 ? $"{pickup.Drop.Name} x{pickup.Drop.Quantity}" : pickup.Drop.Name;
            var key = KeyBindings.Label(GameAction.Pickup);
            var rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight - 336, 660, 30);
            var text = Loc.T("hud.pickup_hint", key, $"<color=#{color}><b>{name}</b></color>");
            // A sombra não pode manter o <color> do nome: a cor rica sobrepõe GUI.color e desenha uma cópia colorida.
            var shadow = Loc.T("hud.pickup_hint", key, name);
            ShadowLabel(rect, text, _small.WithCenter(), Color.white, 1f, shadow);
        }

        private void DrawComboBar()
        {
            var combo = combat.Combo;
            var rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight - 300, 660, 34);

            if (combo.State != ComboState.Active && (combat.Resting || (combat.NearRestFire && combat.Target == null)))
            {
                // Na fogueira da praça, o lugar do aviso do combo mostra como sentar (ou que está descansando).
                var key = KeyBindings.Label(GameAction.Rest);
                GUI.color = new Color(1f, 0.82f, 0.55f);
                GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height),
                    combat.Resting ? Loc.T("rest.resting", key) : Loc.T("rest.hint", key), _center);
                GUI.color = Color.white;
                return;
            }
            if (combo.State != ComboState.Active)
            {
                var cost = combo.Config.ActivationSpCost;
                var ready = combat.Sp.Has(cost) && combat.Target != null;
                GUI.color = ready ? Color.white : new Color(1, 1, 1, 0.5f);
                GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height), Loc.T("hud.combo_start", KeyBindings.Label(GameAction.StartCombo), cost), _center);
                GUI.color = Color.white;
                return;
            }

            var now = Time.time;
            UiTheme.Panel(new Rect(rect.x - 6, rect.y - 6, rect.width + 12, rect.height + 12), 0.78f);
            Box(rect, BarBack);

            // Janela-alvo e zona Excellent.
            var start = (float)combo.WindowStart;
            var end = (float)combo.WindowEnd;
            Box(new Rect(rect.x + rect.width * start, rect.y, rect.width * (end - start), rect.height), WindowColor);
            var half = (end - start) / 2f * (float)combo.Config.ExcellentZoneRatio;
            var center = (float)combo.Config.WindowCenter;
            Box(new Rect(rect.x + rect.width * (center - half), rect.y, rect.width * half * 2, rect.height), ExcellentColor);

            // Marcador.
            var cursor = Mathf.Clamp01((float)combo.GetCursor(PlayerCombat.ComboNow));
            Box(new Rect(rect.x + rect.width * cursor - 3, rect.y - 8, 6, rect.height + 16), Color.white);

            GUI.Label(new Rect(rect.x, rect.y - 46, rect.width, 36), Loc.T("hud.combo_hits", combo.HitCount), _big.WithAlignment(TextAnchor.MiddleLeft));
            GUI.Label(new Rect(rect.x, rect.y - 46, rect.width, 36), Loc.T("hud.combo_bonus", combo.ChainBonus * 100), _label.WithAlignment(TextAnchor.MiddleRight));
            if (combo.CanFinish)
                GUI.Label(new Rect(rect.x, rect.y + rect.height + 6, rect.width, 24), Loc.T("hud.combo_finish", KeyBindings.Label(GameAction.Finisher)), _center);

            // Últimos tipos acertados na corrente, para o jogador planejar a próxima rota.
            var kinds = combo.ChainKinds;
            var shown = Mathf.Min(6, kinds.Count);
            for (var i = 0; i < shown; i++)
            {
                var kind = kinds[kinds.Count - shown + i];
                var chip = new Rect(rect.x + i * 30, rect.y + rect.height + 8, 26, 22);
                Box(chip, kind == SkillKind.Physical ? PhysicalColor : MagicalColor);
                GUI.Label(chip, kind == SkillKind.Physical ? Loc.T("hud.kind_physical_short") : Loc.T("hud.kind_magical_short"), _small.WithCenter());
            }
        }

        private void DrawXpBar()
        {
            if (_progression == null) return;
            var progress = _progression.Progress;
            // Online o Despertar fica embaixo, na mesma linha fina; a XP sobe uma linha.
            var rect = new Rect(0, GameMode.IsOnline ? VirtualHeight - 30 : VirtualHeight - 16, VirtualWidth, 12);
            var toNext = progress.XpToNext;
            var fraction = toNext == 0 ? 1f : (float)progress.Xp / toNext;
            Bar(rect, fraction, new Color(0.65f, 0.45f, 0.95f));
            var text = progress.Level >= LevelCurve.MaxLevel
                ? Loc.T("hud.max_level")
                : Loc.T("hud.xp", progress.Xp, toNext, fraction * 100);
            GUI.Label(new Rect(0, rect.y - 2, VirtualWidth, 16), text, _key.WithCenter());
        }

        private void DrawZone()
        {
            var zone = ZoneArea.At(combat.transform.position);
            var levels = zone == null ? "" : zone.MinLevel == zone.MaxLevel ? $"{zone.MinLevel}" : $"{zone.MinLevel}–{zone.MaxLevel}";
            var dungeon = Net.CharacterState.Dungeon != null ? Game.Shared.Dungeon.DungeonCatalog.Find(Net.CharacterState.Dungeon.DungeonId) : null;
            var arena = Net.CharacterState.Pvp != null && Net.CharacterState.Pvp.Mode == Game.Shared.Net.PvpMode.Arena;
            var text = arena ? Loc.T("hud.zone_arena") : dungeon != null ? Loc.T("hud.zone_dungeon", dungeon.Name) : zone == null ? Loc.T("hud.zone_road") : Loc.T("hud.zone", zone.DisplayName, levels);
            var rect = new Rect(VirtualWidth - 460, 20, 440, 40);
            UiTheme.Panel(rect, 0.78f);
            GUI.Label(rect, text, UiArt.Title(21, TextAnchor.MiddleCenter));
        }

        private void DrawNetwork()
        {
            var session = NetworkSession.Instance;
            if (session == null) return;

            string text;
            var color = Color.white;
            if (!string.IsNullOrEmpty(session.Error))
            {
                text = Loc.T("hud.disconnected", session.Error);
                color = new Color(1f, 0.5f, 0.45f);
            }
            else if (session.State == NetClient.Status.Connecting) text = Loc.T("hud.connecting", GameMode.Host, GameMode.Port);
            else if (!session.IsWelcomed) text = Loc.T("hud.entering_world");
            else text = Loc.T("hud.online", GameMode.PlayerName, session.Clock.LastRtt * 1000);

            var rect = new Rect(VirtualWidth - 460, 64, 440, 30);
            UiTheme.Panel(rect, 0.78f);
            GUI.color = color;
            GUI.Label(rect, text, _small.WithCenter());
            GUI.color = Color.white;
        }

        private void DrawAnnouncement()
        {
            var age = Time.unscaledTime - _announcementAt;
            if (age > 3.5f || string.IsNullOrEmpty(_announcement)) return;
            ShadowLabel(new Rect(0, 210, VirtualWidth, 60), $"<b>{_announcement}</b>", _big.WithCenter(),
                new Color(1f, 0.55f, 0.3f), 1f - Mathf.Clamp01((age - 2.8f) / 0.7f));
        }

        private void DrawDeathOverlay()
        {
            if (!combat.IsDead) return;
            Box(new Rect(0, 0, VirtualWidth, VirtualHeight), new Color(0.3f, 0f, 0f, 0.35f));
            ShadowLabel(new Rect(0, VirtualHeight / 2 - 80, VirtualWidth, 80), Loc.T("hud.you_died"), _big.WithCenter(),
                new Color(1f, 0.4f, 0.35f), 1f);
            GUI.Label(new Rect(0, VirtualHeight / 2, VirtualWidth, 30),
                Loc.T("hud.respawning", combat.RespawnRemaining), _center);
        }

        private void DrawInventory()
        {
            var rect = new Rect(VirtualWidth - 440, Minimap.PanelsTop, 400, 500);
            UiTheme.Panel(rect, 0.78f);
            GUI.Label(new Rect(rect.x + 16, rect.y + 10, rect.width, 30), Loc.T("hud.inventory_title", KeyBindings.Label(GameAction.Inventory)), _label);
            if (_loot == null || _loot.Inventory.Count == 0)
            {
                GUI.Label(new Rect(rect.x + 16, rect.y + 50, rect.width - 32, 60),
                    Loc.T("hud.inventory_empty", KeyBindings.Label(GameAction.Pickup)), _small);
                return;
            }

            var y = rect.y + 50;
            foreach (var stack in _loot.Inventory)
            {
                var color = LootPickup.ColorFor(stack.Rarity);
                Box(new Rect(rect.x + 16, y + 4, 16, 16), color);
                GUI.color = color;
                GUI.Label(new Rect(rect.x + 42, y, rect.width - 120, 26), stack.Name, _small);
                GUI.color = Color.white;
                GUI.Label(new Rect(rect.x, y, rect.width - 16, 26), $"x{stack.Quantity}", _small.WithAlignment(TextAnchor.UpperRight));
                y += 28;
                if (y > rect.yMax - 30) break;
            }
            GUI.Label(new Rect(rect.x + 16, rect.yMax - 30, rect.width, 24),
                Loc.T("hud.monsters_killed", _loot.MonstersKilled), _small);
        }

        /// <summary>Nome dos monstros elite (dourado).</summary>
        private static readonly Color EliteColor = new Color(1f, 0.75f, 0.2f);

        /// <summary>Nome, nível e vida acima dos monstros vivos por perto.</summary>
        private void DrawNameplates()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var scale = Mathf.Clamp(Screen.height / VirtualHeight, 0.6f, 2f);

            foreach (var target in Targetable.All)
            {
                if (target.Level <= 0 || !target.IsAlive) continue;
                if ((target.transform.position - combat.transform.position).sqrMagnitude > 30f * 30f) continue;
                var screen = cam.WorldToScreenPoint(target.HeadPosition + Vector3.up * 0.3f);
                if (screen.z <= 0) continue;

                var width = 140f * scale;
                var x = screen.x - width / 2;
                var y = Screen.height - screen.y - 30f * scale;
                var selected = target == combat.Target;
                GUI.color = selected ? new Color(1f, 0.9f, 0.4f) : target.Elite ? EliteColor : Color.white;
                _small.fontSize = Mathf.RoundToInt(14 * scale);
                GUI.Label(new Rect(x - 40, y - 4, width + 80, 20 * scale), target.NameplateText, _small.WithCenter());
                _small.fontSize = 16;
                GUI.color = Color.white;
                Bar(new Rect(x, y + 16 * scale, width, 6 * scale), (float)target.Hp.Current / Mathf.Max(1, target.Hp.Max), HpColor);
            }
        }

        /// <summary>Quadro do jogador no canto superior esquerdo: nível, Alz, HP, MP e SP.</summary>
        private static readonly Rect PlayerFrameRect = new Rect(20, 20, 380, 122);

        private void DrawPlayerFrame()
        {
            var rect = PlayerFrameRect;
            UiTheme.Panel(rect, 0.78f);
            GUI.Label(new Rect(rect.x + 16, rect.y + 6, rect.width, 26), Loc.T("hud.you", combat.Level), _label);
            if (_loot != null)
                GUI.Label(new Rect(rect.x, rect.y + 6, rect.width - 16, 26), Loc.T("hud.alz", _loot.Alz), _label.WithAlignment(TextAnchor.UpperRight));

            var hpRect = new Rect(rect.x + 16, rect.y + 38, rect.width - 32, 20);
            Bar(hpRect, (float)combat.Hp.Current / Mathf.Max(1, combat.Hp.Max), HpColor);
            GUI.Label(hpRect, Loc.T("hud.hp", combat.Hp.Current, combat.Hp.Max), _small.WithCenter());

            var mpRect = new Rect(rect.x + 16, rect.y + 64, rect.width - 32, 20);
            Bar(mpRect, (float)combat.Mp.Current / Mathf.Max(1, combat.Mp.Max), MpColor);
            GUI.Label(mpRect, Loc.T("hud.mp", combat.Mp.Current, combat.Mp.Max), _small.WithCenter());

            var spRect = new Rect(rect.x + 16, rect.y + 90, rect.width - 32, 20);
            Bar(spRect, (float)combat.Sp.Current / Mathf.Max(1, combat.Sp.Max), SpColor);
            GUI.Label(spRect, Loc.T("hud.sp", combat.Sp.Current, combat.Sp.Max), _small.WithCenter());
        }

        /// <summary>Barra do Despertar (online): enche com acertos de combo bons; cheia, R ativa.</summary>
        private void DrawAwakening()
        {
            var sheet = CharacterState.Sheet;
            if (!GameMode.IsOnline || sheet == null) return;
            var rect = new Rect(0, VirtualHeight - 16, VirtualWidth, 12); // embaixo da XP, no mesmo estilo
            var session = NetworkSession.Instance;
            var remaining = session != null ? sheet.AwakeningUntil - session.ServerNow : 0;
            if (remaining > 0)
            {
                var pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f);
                Bar(rect, (float)(remaining / AwakeningState.DurationSeconds), new Color(1f, 0.35f * pulse, 0.9f * pulse));
                GUI.Label(new Rect(rect.x, rect.y - 2, rect.width, 16), Loc.T("hud.awakening_active", remaining), _key.WithCenter());
                return;
            }
            var full = sheet.AwakeningGauge >= AwakeningState.MaxGauge;
            Bar(rect, sheet.AwakeningGauge / (float)AwakeningState.MaxGauge, full ? new Color(1f, 0.4f, 0.95f) : new Color(0.6f, 0.3f, 0.6f));
            GUI.Label(new Rect(rect.x, rect.y - 2, rect.width, 16), full ? Loc.T("hud.awakening_ready", KeyBindings.Label(GameAction.Awakening)) : Loc.T("hud.awakening_gauge", sheet.AwakeningGauge), _key.WithCenter());
        }

        /// <summary>Casas da barra de atalhos: online sempre 20 (o jogador arruma); offline, uma por skill.</summary>
        public static int HotbarCount(int skillCount) => GameMode.IsOnline ? SkillTraining.HotbarSize : skillCount;

        private const float SlotSize = 48f;
        private const float SlotGap = 4f;

        /// <summary>
        /// Retângulo da casa <paramref name="slot"/> (tela virtual): fileira de baixo = teclas 1 a 0, de cima = Alt.
        /// As janelas usam para arrastar.
        /// </summary>
        public static Rect HotbarRect(int slot, int count)
        {
            var row = slot / SkillTraining.HotbarRow;
            var column = slot % SkillTraining.HotbarRow;
            var columns = Mathf.Min(count, SkillTraining.HotbarRow);
            var total = columns * SlotSize + (columns - 1) * SlotGap;
            return new Rect(VirtualWidth / 2 - total / 2 + column * (SlotSize + SlotGap), BarBottom - SlotSize - row * (SlotSize + SlotGap),
                SlotSize, SlotSize);
        }

        private const float BarBottom = VirtualHeight - 40f;

        /// <summary>Topo da barra de atalhos (o SP e o Despertar ficam logo acima).</summary>
        private float BarTop
        {
            get
            {
                var rows = Mathf.CeilToInt(HotbarCount(combat.Skills.Count) / (float)SkillTraining.HotbarRow);
                return BarBottom - rows * SlotSize - (rows - 1) * SlotGap;
            }
        }

        private void DrawSkillBar()
        {
            var skills = combat.Skills;
            var count = HotbarCount(skills.Count);
            var mouse = Event.current.mousePosition;
            var hovered = -1;

            // Moldura atrás das fileiras.
            var first = HotbarRect(0, count);
            var last = HotbarRect(Mathf.Min(count, SkillTraining.HotbarRow) - 1, count);
            var frame = new Rect(first.x - 8, BarTop - 8, last.xMax - first.x + 16, BarBottom - BarTop + 16);
            UiTheme.Panel(frame, 0.85f);
            PanelRects.Add(frame);

            for (var slot = 0; slot < count; slot++)
            {
                var rect = HotbarRect(slot, count);
                var i = CharacterState.HotbarSkill(slot);
                var item = CharacterState.HotbarItem(slot);
                var filled = (i >= 0 && i < skills.Count) || item != null;
                UiTheme.Slot(rect, rect.Contains(mouse));
                if (item != null) DrawHotbarItem(rect, item);
                else if (filled)
                {
                    var skill = skills[i];
                    if (rect.Contains(mouse)) hovered = i;
                    var icon = SkillIcons.Get(skill);
                    if (icon != null) GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), icon);

                    // Sem mana ou ainda não aprendida (offline): escurece.
                    if (!combat.Mp.Has(skill.MpCost)) Box(rect, new Color(0.05f, 0.08f, 0.35f, 0.55f));
                    if (GameMode.IsOnline && !CharacterState.SkillUnlocked(i)) Box(rect, new Color(0.03f, 0.03f, 0.05f, 0.8f));

                    // Na fila, esperando a conjuração atual: moldura dourada piscando.
                    if (i == combat.QueuedSkill && Event.current.type == EventType.Repaint)
                    {
                        var pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 12f);
                        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                            new Color(1f, 0.82f, 0.35f, pulse), Vector4.one * 2.5f, Vector4.one * 4f);
                    }

                    var remaining = combat.CooldownRemaining(i);
                    if (remaining > 0)
                    {
                        var fraction = Mathf.Clamp01(remaining / Mathf.Max(0.01f, skill.CooldownSeconds));
                        Box(new Rect(rect.x + 2, rect.y + 2 + (rect.height - 4) * (1 - fraction), rect.width - 4, (rect.height - 4) * fraction), new Color(0, 0, 0, 0.65f));
                        ShadowLabel(rect, remaining >= 10f ? $"<b>{remaining:0}</b>" : $"<b>{remaining:0.0}</b>", _small.WithCenter(), Color.white, 1f);
                    }
                }
                ShadowLabel(new Rect(rect.x + 3, rect.y + 1, rect.width, 16), SlotKeyLabel(slot), _key, filled ? Color.white : new Color(1f, 1f, 1f, 0.45f), 1f);
            }

            DrawCastBar();
            if (hovered >= 0) DrawSkillTooltip(skills[hovered], hovered, mouse);
        }

        /// <summary>Poção na barra: o ícone e quantas há na bolsa (escurece quando acabou).</summary>
        private void DrawHotbarItem(Rect rect, string itemId)
        {
            var count = CharacterState.Bag.Count(itemId);
            var icon = ItemIcons.Get(itemId);
            if (icon != null) GUI.DrawTexture(new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8), icon, ScaleMode.ScaleToFit);
            else ShadowLabel(rect, Game.Shared.Items.ItemCatalog.Get(itemId).Name, _key.WithCenter(), Color.white, 1f);
            if (count == 0) Box(rect, new Color(0.03f, 0.03f, 0.05f, 0.7f));
            ShadowLabel(new Rect(rect.x, rect.yMax - 18, rect.width - 4, 16), $"<b>{count}</b>", _key.WithAlignment(TextAnchor.UpperRight),
                count == 0 ? new Color(1f, 0.45f, 0.4f) : Color.white, 1f);
        }

        /// <summary>Barra de conjuração logo acima da barra de atalhos, com o nome da skill.</summary>
        private void DrawCastBar()
        {
            var casting = combat.Casting;
            if (casting == null) return;
            var rect = new Rect(VirtualWidth / 2 - 130, BarTop - 30, 260, 10);
            Bar(rect, combat.CastProgress, new Color(1f, 0.78f, 0.35f));
            ShadowLabel(new Rect(rect.x, rect.y - 24, rect.width, 18), casting.Name, _key.WithCenter(), Color.white, 1f);
        }

        /// <summary>Nome, tipo, MP, recarga, dano, alcance e área da skill embaixo do mouse.</summary>
        private void DrawSkillTooltip(SkillData skill, int index, Vector2 mouse)
        {
            var kind = skill.Kind == SkillKind.Physical ? Loc.T("hud.kind_physical") : Loc.T("hud.kind_magical");
            if (skill.MpCost > 0) kind = Loc.T("hud.kind_with_mp", kind, skill.MpCost);
            var info = skill.AreaRadius > 0
                ? Loc.T("skill.info_area", skill.BaseDamage, skill.PowerScale, skill.Range, skill.AreaRadius)
                : Loc.T("skill.info", skill.BaseDamage, skill.PowerScale, skill.Range);
            var text = $"<b>{skill.Name}</b>\n<size=14>{kind} · {skill.CooldownSeconds:0.#} s</size>\n<size=13>{info}</size>";
            var extra = 0f;
            if (skill.Id == Game.Shared.Monsters.ThreatRules.TauntSkillId)
            {
                text += $"\n<size=13><color=#ffd27a>{Loc.T("skill.taunt_hint")}</color></size>";
                extra = 20f;
            }
            if (GameMode.IsOnline && !CharacterState.SkillUnlocked(index)) text += $"\n<size=14><color=#ff8a7a>{combat.UnlockText(index)}</color></size>";
            var rect = new Rect(Mathf.Clamp(mouse.x - 160, 10, VirtualWidth - 330), BarTop - 112 - extra, 320, 94 + extra);
            UiTheme.Panel(rect, 0.95f);
            GUI.Label(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 12), text, _small);
        }

        private void DrawGradePopup()
        {
            var age = Time.unscaledTime - combat.LastGradeAt;
            if (age > 0.6f) return;

            string text;
            Color color;
            switch (combat.LastGrade)
            {
                case ComboGrade.Excellent: text = Loc.T("hud.grade_excellent"); color = ExcellentColor; break;
                case ComboGrade.Good: text = Loc.T("hud.grade_good"); color = new Color(0.6f, 0.9f, 1f); break;
                default: text = Loc.T("hud.grade_miss"); color = new Color(1f, 0.35f, 0.3f); break;
            }

            var pop = 1f + Mathf.Max(0, 0.15f - age) * 3f;
            _big.fontSize = Mathf.RoundToInt(56 * pop);
            ShadowLabel(new Rect(0, VirtualHeight - 490, VirtualWidth, 80), $"<b>{text}</b>", _big.WithCenter(),
                color, 1f - Mathf.Clamp01((age - 0.35f) / 0.25f));
            _big.fontSize = 30;

            var routeAge = Time.unscaledTime - combat.LastRouteAt;
            if (combat.LastRoute != null && routeAge <= 0.9f)
            {
                ShadowLabel(new Rect(0, VirtualHeight - 412, VirtualWidth, 40),
                    Loc.T("hud.route", combat.LastRoute.Name.ToUpperInvariant(), combat.LastRoute.DamageBonus * 100), _big.WithCenter(),
                    RouteColor, 1f - Mathf.Clamp01((routeAge - 0.6f) / 0.3f));
            }
        }

        /// <summary>Texto com sombra escura, legível sobre qualquer fundo.</summary>
        private static void ShadowLabel(Rect rect, string text, GUIStyle style, Color color, float alpha, string shadowText = null)
        {
            GUI.color = new Color(0, 0, 0, alpha * 0.85f);
            GUI.Label(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), shadowText ?? text, style);
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        private void DrawMessage()
        {
            var age = Time.unscaledTime - combat.MessageAt;
            if (age > 2.5f || string.IsNullOrEmpty(combat.Message)) return;
            GUI.color = new Color(1, 1, 1, 1f - Mathf.Clamp01((age - 2f) / 0.5f));
            // Com janelas do lado direito abertas (loja, troca, ferreira) o aviso vai para o espaço livre à esquerda delas.
            var messageRect = GameWindows.RightPanelOpen ? new Rect(420, VirtualHeight - 548, 540, 30) : new Rect(0, VirtualHeight - 548, VirtualWidth, 30);
            ShadowLabel(messageRect, combat.Message, _center, Color.white, 1f);
            GUI.color = Color.white;
        }

        private static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Bar(Rect rect, float fraction, Color color) => UiTheme.Bar(rect, fraction, color);

        private void EnsureStyles()
        {
            if (_label != null) return;
            UiTheme.Ensure();
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true };
            _label.normal.textColor = Color.white;
            _center = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
            _big = new GUIStyle(_label) { fontSize = 30 };
            _small = new GUIStyle(_label) { fontSize = 16 };
            _key = new GUIStyle(_label) { fontSize = 12 };
        }
    }

    internal static class GuiStyleExtensions
    {
        private sealed class ByReference : IEqualityComparer<GUIStyle>
        {
            public bool Equals(GUIStyle a, GUIStyle b) => ReferenceEquals(a, b);
            public int GetHashCode(GUIStyle style) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(style);
        }

        // Criar um GUIStyle por chamada (dezenas por quadro, em cada passada do OnGUI) gerava lixo e coletas frequentes.
        private static readonly Dictionary<GUIStyle, GUIStyle[]> Aligned = new Dictionary<GUIStyle, GUIStyle[]>(new ByReference());

        public static GUIStyle WithCenter(this GUIStyle style) => style.WithAlignment(TextAnchor.MiddleCenter);

        /// <summary>
        /// O mesmo estilo com outro alinhamento, criado uma vez e reaproveitado. O tamanho, a cor e o negrito são copiados
        /// do original a cada uso, porque o HUD muda o tamanho da fonte na hora (ex.: plaquinhas que crescem com a tela).
        /// </summary>
        public static GUIStyle WithAlignment(this GUIStyle style, TextAnchor alignment)
        {
            if (!Aligned.TryGetValue(style, out var variants))
            {
                variants = new GUIStyle[9];
                Aligned[style] = variants;
            }
            var variant = variants[(int)alignment] ??= new GUIStyle(style) { alignment = alignment };
            variant.fontSize = style.fontSize;
            variant.fontStyle = style.fontStyle;
            variant.richText = style.richText;
            variant.wordWrap = style.wordWrap;
            variant.normal.textColor = style.normal.textColor;
            return variant;
        }
    }
}
