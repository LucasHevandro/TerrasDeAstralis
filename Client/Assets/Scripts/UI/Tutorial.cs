using Game.Client.Combat;
using Game.Client.Net;
using Game.Client.Player;
using Game.Shared.Combat;
using Game.Shared.Progression;
using Game.Shared.Text;
using UnityEngine;
using PvpMode = Game.Shared.Net.PvpMode;

namespace Game.Client.UI
{
    /// <summary>
    /// Tutorial curto dos primeiros minutos: andar, escolher o alvo, atacar e esquivar; depois (quando a missão libera o
    /// combo, ou já no offline) começar o combo, acertar na janela três vezes e finalizar. Cada passo termina sozinho
    /// quando o jogador faz a ação. O andamento fica salvo por personagem; dá para pular e rever pelas opções.
    /// </summary>
    public sealed class Tutorial : MonoBehaviour
    {
        private enum Step
        {
            Move, Target, Skill, Dodge, UnlockCombo, StartCombo, ComboHits, Finisher, Routes, Done,
        }

        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;
        private const int HitsToLearn = 3;
        private const float RoutesSeconds = 12f;
        private const float DoneSeconds = 6f;
        private static readonly Color Panel = new Color(0.04f, 0.06f, 0.1f, 0.9f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.35f);

        private static Tutorial _instance;

        private Step _step;
        private bool _loaded;
        private string _owner = "";
        private Vector3 _startPosition;
        private int _hitsAtStep;
        private int _dodgesAtStep;
        private int _finishersAtStep;
        private float _stepStartedAt;
        private float _missAt = float.NegativeInfinity;
        private float _lastGradeSeen = float.NegativeInfinity;
        private GUIStyle _title;
        private GUIStyle _text;
        private GUIStyle _button;
        private GUIStyle _arrow;

        /// <summary>O tutorial está aparecendo (passo ainda não concluído).</summary>
        public static bool Active => _instance != null && _instance._loaded && _instance._step != Step.Done;

        /// <summary>Recomeça do primeiro passo (botão nas opções).</summary>
        public static void Restart()
        {
            if (_instance == null) return;
            _instance._loaded = false;
            PlayerPrefs.DeleteKey(_instance.SaveKey);
            _instance._owner = "";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("Tutorial");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Tutorial>();
        }

        private string SaveKey => "tutorial." + (GameMode.IsOnline ? GameMode.PlayerName : "offline");

        private void Update()
        {
            var player = PlayerCombat.Local;
            if (player == null || ConnectMenu.IsOpen) return;
            if (GameMode.IsOnline && (NetworkSession.Instance == null || !NetworkSession.Instance.IsWelcomed)) return;
            if (!_loaded || _owner != SaveKey) Load(player);
            if (_step == Step.Done) return;

            // Quem já se adiantou (começou um combo ou finalizou) não fica preso nos passos básicos.
            if (_step < Step.Finisher && player.FinishersLanded > _finishersAtStep && ComboUnlocked)
            {
                Go(player, Step.Routes);
                return;
            }
            if (_step < Step.StartCombo && ComboUnlocked && player.Combo != null && player.Combo.State == ComboState.Active)
            {
                Go(player, Step.ComboHits);
                return;
            }

            var motor = player.GetComponent<PlayerMotor>();
            switch (_step)
            {
                case Step.Move:
                    if ((player.transform.position - _startPosition).magnitude > 4f) Advance(player);
                    break;
                case Step.Target:
                    if (player.Target != null) Advance(player);
                    break;
                case Step.Skill:
                    if (player.HitsLanded > _hitsAtStep) Advance(player);
                    break;
                case Step.Dodge:
                    if (motor == null || motor.DodgeCount > _dodgesAtStep) Advance(player);
                    break;
                case Step.UnlockCombo:
                    if (ComboUnlocked) Advance(player);
                    break;
                case Step.StartCombo:
                    if (player.Combo != null && player.Combo.State == ComboState.Active) Advance(player);
                    break;
                case Step.ComboHits:
                    if (player.LastGrade == ComboGrade.Miss && player.LastGradeAt > _lastGradeSeen) _missAt = Time.unscaledTime;
                    _lastGradeSeen = player.LastGradeAt;
                    if (player.Combo != null && player.Combo.HitCount >= HitsToLearn) Advance(player);
                    break;
                case Step.Finisher:
                    if (player.FinishersLanded > _finishersAtStep) Advance(player);
                    // O combo acabou sem finalizar: volta a pedir o combo.
                    else if (player.Combo != null && player.Combo.State != ComboState.Active) Go(player, Step.StartCombo);
                    break;
                case Step.Routes:
                    if (Time.unscaledTime - _stepStartedAt > RoutesSeconds) Advance(player);
                    break;
            }
        }

        private static bool ComboUnlocked => !GameMode.IsOnline || (CharacterState.HasSheet && CharacterState.Sheet.ComboUnlocked);

        private void Load(PlayerCombat player)
        {
            _owner = SaveKey;
            _loaded = true;
            var saved = Step.Move;
            try
            {
                saved = (Step)Mathf.Clamp(PlayerPrefs.GetInt(_owner, 0), 0, (int)Step.Done);
            }
            catch (System.Exception)
            {
                // Sem PlayerPrefs: começa do início.
            }
            Go(player, saved, save: false);
        }

        private void Advance(PlayerCombat player)
        {
            var next = _step + 1;
            // Quem já tem o combo (offline ou missão já feita) pula o passo da missão.
            if (next == Step.UnlockCombo && ComboUnlocked) next = Step.StartCombo;
            Go(player, next);
            if (next != Step.Done) SoundPlayerHint();
        }

        private void Go(PlayerCombat player, Step step, bool save = true)
        {
            // A esquiva só libera no nível 5: antes disso o passo dela fica para depois (não trava o tutorial).
            var motor = player.GetComponent<PlayerMotor>();
            if (step == Step.Dodge && motor != null && !motor.DodgeUnlocked) step = Step.UnlockCombo;
            if (step == Step.UnlockCombo && ComboUnlocked) step = Step.StartCombo;
            _step = step;
            _stepStartedAt = Time.unscaledTime;
            _startPosition = player.transform.position;
            _hitsAtStep = player.HitsLanded;
            _finishersAtStep = player.FinishersLanded;
            _dodgesAtStep = motor != null ? motor.DodgeCount : 0;
            _missAt = float.NegativeInfinity;
            _lastGradeSeen = player.LastGradeAt;
            if (!save) return;
            // Métrica do teste fechado: até onde cada um chega no tutorial (sem os passos restaurados ao entrar).
            Net.ClientMetrics.Report(Game.Shared.Accounts.ClientReportRules.Tutorial, (int)step, step.ToString());
            try
            {
                PlayerPrefs.SetInt(_owner, (int)step);
                PlayerPrefs.Save();
            }
            catch (System.Exception)
            {
                // Não salvou: recomeça deste passo só nesta sessão.
            }
        }

        private static void SoundPlayerHint() => Fx.SoundPlayer.Play(Fx.SoundId.Good, 0.5f, 0f);

        // Só GUI (sem GUILayout): sem a passada de layout, o OnGUI roda menos vezes por quadro.
        private void OnEnable() => useGUILayout = false;

        private void OnGUI()
        {
            var player = PlayerCombat.Local;
            if (!_loaded || player == null || ConnectMenu.IsOpen || OptionsWindow.IsOpen || Minimap.BigOpen || player.IsDead) return;
            if (CharacterState.Pvp != null && CharacterState.Pvp.Mode != PvpMode.None) return;
            // Conversa com NPC ou janela do lado direito aberta (ferreira, loja, troca): o tutorial sai da frente.
            if (CharacterState.Dialog != null || GameWindows.RightPanelOpen) return;
            var done = _step == Step.Done;
            if (done && Time.unscaledTime - _stepStartedAt > DoneSeconds) return;

            GUI.depth = -5; // por cima do HUD, por baixo das janelas
            EnsureStyles();
            var scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            GUI.matrix = Matrix4x4.TRS(
                new Vector3((Screen.width - VirtualWidth * scale) / 2, (Screen.height - VirtualHeight * scale) / 2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));

            var rect = new Rect(VirtualWidth / 2 - 330, 108, 660, 118);
            UiTheme.Panel(rect);
            Box(new Rect(rect.x, rect.y, 4, rect.height), Accent);
            var title = done ? Loc.T("tutorial.title_done") : Loc.T("tutorial.title", StepNumber, StepCount);
            GUI.Label(new Rect(rect.x + 18, rect.y + 8, rect.width - 200, 26), title, _title);
            GUI.Label(new Rect(rect.x + 18, rect.y + 36, rect.width - 36, 78), StepText(player), _text);
            if (!done && GUI.Button(new Rect(rect.xMax - 150, rect.y + 8, 136, 28), Loc.T("tutorial.skip"), _button)) Go(player, Step.Done);
            if (_step == Step.Routes && GUI.Button(new Rect(rect.xMax - 150, rect.yMax - 36, 136, 28), Loc.T("tutorial.ok"), _button))
                Advance(player);

            DrawPointer();
            GUI.matrix = Matrix4x4.identity;
        }

        private int StepNumber => Mathf.Clamp((int)_step + 1 - (ComboUnlockedFromStart && _step > Step.UnlockCombo ? 1 : 0), 1, StepCount);
        private int StepCount => (int)Step.Done - (ComboUnlockedFromStart ? 1 : 0);

        /// <summary>Offline o combo já vem liberado: o passo da missão não conta.</summary>
        private static bool ComboUnlockedFromStart => !GameMode.IsOnline;

        private string StepText(PlayerCombat player)
        {
            switch (_step)
            {
                case Step.Move:
                    return Loc.T("tutorial.move", KeyBindings.MoveLabel(), KeyBindings.Label(GameAction.Run));
                case Step.Target:
                    return Loc.T("tutorial.target", KeyBindings.Label(GameAction.NextTarget));
                case Step.Skill:
                    return Loc.T("tutorial.skill", KeyBindings.Label(GameAction.Skill1));
                case Step.Dodge:
                    return Loc.T("tutorial.dodge", KeyBindings.Label(GameAction.Dodge));
                case Step.UnlockCombo:
                    return Loc.T("tutorial.unlock", Loc.T("npc." + NpcCatalog.Elder), KeyBindings.Label(GameAction.Talk),
                        Loc.T("quest." + ClassCatalog.ComboQuestId));
                case Step.StartCombo:
                    return Loc.T("tutorial.start_combo", KeyBindings.Label(GameAction.StartCombo));
                case Step.ComboHits:
                    var hits = Mathf.Min(player.Combo?.HitCount ?? 0, HitsToLearn);
                    var text = Loc.T("tutorial.combo_hits", KeyBindings.SkillsLabel(Mathf.Min(4, player.Skills.Count)), hits, HitsToLearn);
                    return Time.unscaledTime - _missAt < 3f ? text + "\n<color=#ff8a7a>" + Loc.T("tutorial.combo_miss") + "</color>" : text;
                case Step.Finisher:
                    return Loc.T("tutorial.finisher", KeyBindings.Label(GameAction.Finisher));
                case Step.Routes:
                    return Loc.T("tutorial.routes");
                default:
                    return Loc.T("tutorial.done", "Esc");
            }
        }

        /// <summary>Seta piscando para a parte do HUD que o passo usa (barra de skills ou barra do combo).</summary>
        private void DrawPointer()
        {
            float y;
            switch (_step)
            {
                case Step.Skill: y = VirtualHeight - 214; break;
                case Step.StartCombo:
                case Step.ComboHits:
                case Step.Finisher: y = VirtualHeight - 352; break;
                default: return;
            }
            var bounce = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 10f;
            GUI.color = Accent;
            GUI.Label(new Rect(VirtualWidth / 2 - 30, y - bounce, 60, 40), "▼", _arrow);
            GUI.color = Color.white;
        }

        private static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_text != null) return;
            UiTheme.Ensure();
            _text = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, wordWrap = true };
            _text.normal.textColor = Color.white;
            _title = new GUIStyle(_text) { fontSize = 17, fontStyle = FontStyle.Bold };
            if (UiArt.TitleFont != null) _title.font = UiArt.TitleFont;
            _title.normal.textColor = Accent;
            _button = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true };
            _arrow = new GUIStyle(_text) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
        }
    }
}
