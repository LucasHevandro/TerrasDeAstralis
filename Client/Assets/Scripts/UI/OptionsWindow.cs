using System.Collections.Generic;
using Game.Client.Combat;
using Game.Client.Fx;
using Game.Client.Net;
using Game.Shared.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.UI
{
    /// <summary>
    /// Janela de opções (Esc no jogo, botão no menu de entrada): vídeo, áudio e idioma. Volume, qualidade, FPS e idioma
    /// valem na hora; resolução e tela cheia só com "Aplicar". Funciona online e offline (criada sozinha ao abrir o jogo).
    /// </summary>
    [DefaultExecutionOrder(-100)] // lê o Esc antes das outras janelas (que fecham com ele)
    public sealed class OptionsWindow : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;
        private static readonly Color Panel = new Color(0.03f, 0.03f, 0.05f, 0.94f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.35f);

        private static OptionsWindow _instance;

        private GUIStyle _title;
        private GUIStyle _label;
        private GUIStyle _small;
        private GUIStyle _button;
        private GUIStyle _tab;
        private GUIStyle _keyButton;

        private List<Vector2Int> _resolutions = new List<Vector2Int>();
        private int _resolutionIndex;
        private bool _fullscreen;
        private IReadOnlyList<string> _languages = new List<string>();
        private readonly Dictionary<string, string> _languageNames = new Dictionary<string, string>();
        private Rect _rect;
        /// <summary>Abas da janela.</summary>
        public enum Page
        {
            General,
            Keys,
            Controls,
        }

        private Page _page;
        private string _controlsText;
        private (int Language, int Keys, bool Online, int BestChain) _controlsKey;
        private GameAction? _waitingFor;
        private string _keysMessage = "";
        private float _scale = 1f;
        private Vector2 _offset;

        public static bool IsOpen { get; private set; }

        private static int _escapeFrame = -1;

        /// <summary>O Esc deste quadro abriu ou fechou as opções: as outras janelas não fazem nada com ele.</summary>
        public static bool EscapeTaken => _escapeFrame == Time.frameCount;

        private bool _confirmQuit;

        /// <summary>O ponteiro está sobre a janela (o clique não vai para o mundo).</summary>
        public static bool Contains(Vector2 screenPoint)
        {
            if (!IsOpen || _instance == null) return false;
            var point = new Vector2((screenPoint.x - _instance._offset.x) / _instance._scale,
                (Screen.height - screenPoint.y - _instance._offset.y) / _instance._scale);
            return _instance._rect.Contains(point);
        }

        public static void Open(Page page = Page.General)
        {
            if (_instance == null) return;
            _instance.Refresh();
            _instance._page = page;
            if (!IsOpen) UiSounds.Toggle(true);
            IsOpen = true;
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_instance != null) _instance._confirmQuit = false;
            UiSounds.Toggle(false);
            if (_instance != null) _instance._waitingFor = null;
            GameSettings.Save();
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("Opções");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<OptionsWindow>();
        }

        private void Refresh()
        {
            _resolutions = GameSettings.Resolutions();
            _resolutionIndex = Mathf.Max(0, _resolutions.IndexOf(new Vector2Int(Screen.width, Screen.height)));
            _fullscreen = Screen.fullScreen;
            _languages = GameLocale.Available;
            foreach (var language in _languages)
                if (!_languageNames.ContainsKey(language)) _languageNames[language] = GameLocale.DisplayName(language);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || GameInput.Typing || GameInput.FieldFocused) return;
            if (IsOpen && _waitingFor is { } action)
            {
                // Esperando a tecla nova: a próxima tecla apertada vira o atalho (Esc cancela).
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    _waitingFor = null;
                    return;
                }
                foreach (var key in keyboard.allKeys)
                {
                    if (key == null || !key.wasPressedThisFrame) continue;
                    if (KeyBindings.Set(action, key.keyCode))
                    {
                        _waitingFor = null;
                        _keysMessage = "";
                    }
                    else _keysMessage = Loc.T("options.keys_reserved");
                    break;
                }
                return;
            }
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                // Esc: fecha a confirmação de sair, depois as opções. Fechado, abre as opções quando não há outra
                // coisa para o Esc fechar antes (janela, conversa, mapa grande, amigos ou alvo selecionado).
                if (_confirmQuit) _confirmQuit = false;
                else if (IsOpen) Close();
                else if (!SomethingElseToClose()) Open();
                else return;
                _escapeFrame = Time.frameCount;
            }
            else if (KeyBindings.PressedRaw(GameAction.Options)) Toggle();
        }

        /// <summary>Para o teste automático: abre as opções já perguntando se quer sair.</summary>
        public static void OpenQuitConfirm()
        {
            Open();
            if (_instance != null) _instance._confirmQuit = true;
        }

        private static bool SomethingElseToClose() =>
            GameWindows.AnyOpen || Minimap.BigOpen || SocialWindows.FriendsOpen ||
            (PlayerCombat.Local != null && PlayerCombat.Local.Target != null);

        /// <summary>Online com conta, dentro do mundo: dá para voltar à lista de personagens.</summary>
        private static bool CanSwitchCharacter => GameMode.IsOnline && !string.IsNullOrEmpty(GameMode.Token) && !ConnectMenu.IsOpen;

        /// <summary>
        /// Trocar de personagem: salva as opções, sai do mundo e volta para a lista da conta (o servidor salva o
        /// personagem ao perder a conexão e espera esse salvamento antes de deixar alguém entrar nele de novo).
        /// </summary>
        public static void SwitchCharacter()
        {
            GameSettings.Save();
            Close();
            GameMode.ReturnToCharacters();
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Sair do jogo: salva as opções e fecha (online, o servidor salva o personagem ao perder a conexão).</summary>
        private static void QuitGame()
        {
            GameSettings.Save();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // Só GUI (sem GUILayout): sem a passada de layout, o OnGUI roda menos vezes por quadro.
        private void OnEnable() => useGUILayout = false;

        private void OnGUI()
        {
            if (!IsOpen) return;
            GUI.depth = -30; // por cima de tudo, inclusive do menu de entrada
            EnsureStyles();
            _scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            _offset = new Vector2((Screen.width - VirtualWidth * _scale) / 2, (Screen.height - VirtualHeight * _scale) / 2);
            GUI.matrix = Matrix4x4.TRS(new Vector3(_offset.x, _offset.y, 0), Quaternion.identity, new Vector3(_scale, _scale, 1));
            if (Event.current.type == EventType.MouseDown && _rect.Contains(Event.current.mousePosition)) UiSounds.Click();

            _rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight / 2 - 398, 660, 796);
            Box(new Rect(0, 0, VirtualWidth, VirtualHeight), new Color(0, 0, 0, 0.35f));
            UiTheme.Panel(_rect);
            var x = _rect.x + 28;
            var y = _rect.y + 18;
            var width = _rect.width - 56;
            GUI.Label(new Rect(x, y, width, 34), Loc.T("options.title"), _title);
            y += 48;
            if (Tab(new Rect(x, y, 150, 34), Loc.T("options.tab_general"), _page == Page.General)) _page = Page.General;
            if (Tab(new Rect(x + 156, y, 150, 34), Loc.T("options.tab_keys"), _page == Page.Keys)) _page = Page.Keys;
            if (Tab(new Rect(x + 312, y, 150, 34), Loc.T("options.tab_controls"), _page == Page.Controls)) _page = Page.Controls;
            y += 50;

            if (_page == Page.Keys) DrawKeys(x, y, width);
            else if (_page == Page.Controls) DrawControls(x, y, width);
            else DrawGeneral(x, y, width);

            var buttonsY = _rect.yMax - 60;
            if (GUI.Button(new Rect(_rect.xMax - 28 - 160, buttonsY, 160, 40), "<b>" + Loc.T("ui.close") + "</b>", _button)) Close();
            GUI.Label(new Rect(x, buttonsY - 34, width, 24), Loc.T("options.hint"), _small);
            if (GUI.Button(new Rect(_rect.xMax - 28 - 170, _rect.y + 18, 170, 36), Loc.T("options.quit"), _button)) _confirmQuit = true;
            if (CanSwitchCharacter && GUI.Button(new Rect(_rect.xMax - 28 - 170 - 10 - 220, _rect.y + 18, 220, 36), Loc.T("options.switch_character"), _button))
                SwitchCharacter();
            if (_confirmQuit) DrawQuitConfirm();
            GUI.matrix = Matrix4x4.identity;
        }

        /// <summary>Confirmação de sair, por cima da janela.</summary>
        private void DrawQuitConfirm()
        {
            UiTheme.Panel(_rect, 0.75f);
            var box = new Rect(_rect.center.x - 200, _rect.center.y - 90, 400, 180);
            UiTheme.Panel(box, 1f);
            GUI.Label(new Rect(box.x + 20, box.y + 18, box.width - 40, 34), "<b>" + Loc.T("options.quit_title") + "</b>", _label.WithCenter());
            GUI.Label(new Rect(box.x + 20, box.y + 54, box.width - 40, 50), Loc.T("options.quit_text"), _small.WithCenter());
            if (GUI.Button(new Rect(box.x + 30, box.yMax - 62, 160, 40), "<b>" + Loc.T("options.quit_yes") + "</b>", _button)) QuitGame();
            if (GUI.Button(new Rect(box.xMax - 190, box.yMax - 62, 160, 40), Loc.T("ui.cancel"), _button)) _confirmQuit = false;
        }

        private void DrawGeneral(float x, float y, float width)
        {
            // ----- Vídeo -----
            y = Section(x, y, width, Loc.T("options.video"));
            var size = _resolutions.Count > 0 ? _resolutions[_resolutionIndex] : new Vector2Int(Screen.width, Screen.height);
            y = Chooser(x, y, Loc.T("options.resolution"), $"{size.x} x {size.y}", step =>
                _resolutionIndex = Mathf.Clamp(_resolutionIndex + step, 0, Mathf.Max(0, _resolutions.Count - 1)));
            _fullscreen = UiTheme.Toggle(new Rect(x + 230, y, 220, 28), _fullscreen, " " + Loc.T("options.fullscreen"), _label);
            var changed = size.x != Screen.width || size.y != Screen.height || _fullscreen != Screen.fullScreen;
            GUI.enabled = changed;
            if (GUI.Button(new Rect(_rect.xMax - 28 - 150, y - 4, 150, 34), Loc.T("options.apply"), _button))
                Screen.SetResolution(size.x, size.y, _fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            GUI.enabled = true;
            y += 40;

            var qualityNames = QualitySettings.names;
            var quality = Mathf.Clamp(GameSettings.QualityLevel, 0, qualityNames.Length - 1);
            y = Chooser(x, y, Loc.T("options.quality"), QualityName(qualityNames[quality]), step =>
            {
                GameSettings.QualityLevel = Mathf.Clamp(quality + step, 0, qualityNames.Length - 1);
                GameSettings.Apply();
            });

            var fpsIndex = Mathf.Max(0, IndexOf(GameSettings.FpsChoices, GameSettings.FpsLimit));
            GUI.enabled = !GameSettings.VSync;
            y = Chooser(x, y, Loc.T("options.fps"), FpsName(GameSettings.FpsLimit), step =>
            {
                GameSettings.FpsLimit = GameSettings.FpsChoices[Mathf.Clamp(fpsIndex + step, 0, GameSettings.FpsChoices.Count - 1)];
                GameSettings.Apply();
            });
            GUI.enabled = true;
            var vsync = UiTheme.Toggle(new Rect(x + 230, y, 400, 28), GameSettings.VSync, " " + Loc.T("options.vsync"), _label);
            if (vsync != GameSettings.VSync)
            {
                GameSettings.VSync = vsync;
                GameSettings.Apply();
            }
            y += 46;

            // ----- Áudio -----
            y = Section(x, y, width, Loc.T("options.audio"));
            var master = Slider(x, y, Loc.T("options.volume_master"), GameSettings.MasterVolume);
            y += 36;
            var effects = Slider(x, y, Loc.T("options.volume_effects"), GameSettings.EffectsVolume);
            y += 36;
            GameSettings.MusicVolume = Slider(x, y, Loc.T("options.volume_music"), GameSettings.MusicVolume);
            y += 36;
            GameSettings.AmbientVolume = Slider(x, y, Loc.T("options.volume_ambient"), GameSettings.AmbientVolume);
            y += 46;
            if (!Mathf.Approximately(master, GameSettings.MasterVolume))
            {
                GameSettings.MasterVolume = master;
                GameSettings.Apply();
            }
            if (!Mathf.Approximately(effects, GameSettings.EffectsVolume))
            {
                GameSettings.EffectsVolume = effects;
                SoundPlayer.Play(SoundId.Hit, 1f, 0f); // amostra do volume novo
            }

            // ----- Idioma -----
            y = Section(x, y, width, Loc.T("options.language"));
            var languageIndex = Mathf.Max(0, IndexOf(_languages, Loc.Language));
            var languageName = _languageNames.TryGetValue(Loc.Language, out var display) ? display : Loc.Language;
            GUI.enabled = _languages.Count > 1;
            y = Chooser(x, y, Loc.T("options.language_label"), languageName, step =>
            {
                if (_languages.Count == 0) return;
                var next = (languageIndex + step + _languages.Count) % _languages.Count;
                GameLocale.Select(_languages[next]);
            });
            GUI.enabled = true;

            // Métricas do cliente (tutorial, FPS, erros): ligadas por padrão, explicado na política de privacidade.
            var metrics = UiTheme.Toggle(new Rect(x, y + 4, width, 28), GameSettings.ClientMetrics, " " + Loc.T("options.client_metrics"), _label);
            if (metrics != GameSettings.ClientMetrics)
            {
                GameSettings.ClientMetrics = metrics;
                GameSettings.Save();
            }
            var autoAttack = UiTheme.Toggle(new Rect(x, y + 34, width, 28), GameSettings.AutoAttack, " " + Loc.T("options.auto_attack"), _label);
            if (autoAttack != GameSettings.AutoAttack)
            {
                GameSettings.AutoAttack = autoAttack;
                GameSettings.Save();
            }

            if (GUI.Button(new Rect(x, _rect.yMax - 60, 220, 40), Loc.T("options.defaults"), _button))
            {
                GameSettings.ResetDefaults();
                GameSettings.Apply();
            }
            if (GUI.Button(new Rect(x + 232, _rect.yMax - 60, 200, 40), Loc.T("options.tutorial_again"), _button))
            {
                Tutorial.Restart();
                Close();
            }
        }

        /// <summary>
        /// Como jogar: andar, câmera, alvo, skills, janelas, chat e as rotas de combo, com as teclas escolhidas.
        /// Antes ficava sempre na tela (canto de cima); agora só aqui, pelo F1 ou pela aba.
        /// </summary>
        private void DrawControls(float x, float y, float width)
        {
            var online = GameMode.IsOnline;
            var best = PlayerCombat.Local != null ? PlayerCombat.Local.BestChain : 0;
            // O texto só muda com o idioma, os atalhos, o modo ou a melhor corrente: montado de novo só nessa hora.
            var key = (Loc.Version, KeyBindings.Version, online, best);
            if (_controlsText == null || key != _controlsKey)
            {
                _controlsKey = key;
                _controlsText =
                    Loc.T("help.controls", KeyBindings.MoveLabel(), KeyBindings.Label(GameAction.Run), KeyBindings.Label(GameAction.Dodge),
                        "Esc", KeyBindings.Label(GameAction.NextTarget), KeyBindings.Label(GameAction.Map)) + "\n\n" +
                    (online
                        ? Loc.T("help.keys_online", KeyBindings.SkillsLabel(8), KeyBindings.Label(GameAction.Pickup),
                            KeyBindings.Label(GameAction.Inventory), KeyBindings.Label(GameAction.Attributes), KeyBindings.Label(GameAction.Talk),
                            KeyBindings.Label(GameAction.QuestLog), KeyBindings.Label(GameAction.Awakening), KeyBindings.Label(GameAction.Friends))
                        : Loc.T("help.keys_offline", KeyBindings.SkillsLabel(4), KeyBindings.Label(GameAction.Pickup),
                            KeyBindings.Label(GameAction.Inventory))) + "\n\n" +
                    Loc.T("help.combo", KeyBindings.Label(GameAction.StartCombo), KeyBindings.Label(GameAction.Finisher)) + "\n\n" +
                    Loc.T("help.best_chain", best);
            }
            GUI.Label(new Rect(x, y, width, _rect.yMax - 110 - y), _controlsText, _label);
        }

        /// <summary>Lista dos atalhos em duas colunas; clicar na tecla espera a nova.</summary>
        private void DrawKeys(float x, float y, float width)
        {
            GUI.Label(new Rect(x, y - 8, width, 24), Loc.T("options.keys_hint"), _small);
            y += 24;
            var actions = new System.Collections.Generic.List<GameAction>(KeyBindings.All);
            var perColumn = (actions.Count + 1) / 2;
            var columnWidth = width / 2;
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                var cx = x + (i / perColumn) * columnWidth;
                var cy = y + (i % perColumn) * 32;
                GUI.Label(new Rect(cx, cy, columnWidth - 120, 30), KeyBindings.ActionName(action), _small);
                var waiting = _waitingFor == action;
                var label = waiting ? "<color=#ffd25a>" + Loc.T("options.keys_waiting") + "</color>" : "<b>" + KeyBindings.Label(action) + "</b>";
                if (GUI.Button(new Rect(cx + columnWidth - 130, cy, 116, 28), label, _keyButton))
                {
                    _waitingFor = waiting ? (GameAction?)null : action;
                    _keysMessage = "";
                }
            }
            if (_keysMessage.Length > 0)
            {
                GUI.color = new Color(1f, 0.55f, 0.5f);
                GUI.Label(new Rect(x + 240, _rect.yMax - 56, 220, 30), _keysMessage, _small);
                GUI.color = Color.white;
            }
            if (GUI.Button(new Rect(x, _rect.yMax - 60, 220, 40), Loc.T("options.keys_defaults"), _button))
            {
                KeyBindings.ResetDefaults();
                _waitingFor = null;
            }
        }

        private bool Tab(Rect rect, string text, bool active)
        {
            Box(rect, active ? new Color(1f, 0.82f, 0.35f, 0.25f) : new Color(1, 1, 1, 0.06f));
            if (active) Box(new Rect(rect.x, rect.yMax - 3, rect.width, 3), Accent);
            return GUI.Button(rect, active ? "<b>" + text + "</b>" : text, _tab);
        }

        private float Section(float x, float y, float width, string title)
        {
            GUI.color = Accent;
            GUI.Label(new Rect(x, y, width, 28), "<b>" + title + "</b>", _label);
            GUI.color = Color.white;
            Box(new Rect(x, y + 30, width, 1), new Color(1, 1, 1, 0.2f));
            return y + 40;
        }

        /// <summary>Linha "rótulo  ‹ valor ›" com os botões de anterior e próximo.</summary>
        private float Chooser(float x, float y, string label, string value, System.Action<int> step)
        {
            GUI.Label(new Rect(x, y, 220, 30), label, _label);
            if (GUI.Button(new Rect(x + 230, y, 40, 32), "‹", _button)) step(-1);
            GUI.Label(new Rect(x + 276, y, 200, 30), value, _label.WithCenter());
            if (GUI.Button(new Rect(x + 482, y, 40, 32), "›", _button)) step(1);
            return y + 40;
        }

        private float Slider(float x, float y, string label, float value)
        {
            GUI.Label(new Rect(x, y, 220, 30), label, _label);
            var result = GUI.HorizontalSlider(new Rect(x + 230, y + 10, 292, 20), value, 0f, 1f);
            GUI.Label(new Rect(x + 534, y, 80, 30), $"{Mathf.RoundToInt(result * 100)}%", _label);
            return result;
        }

        private static string QualityName(string unityName)
        {
            var key = "options.quality_" + unityName.ToLowerInvariant();
            return Loc.Has(key) ? Loc.T(key) : unityName;
        }

        private static string FpsName(int fps) => fps == 0 ? Loc.T("options.fps_unlimited") : fps.ToString();

        private static int IndexOf<T>(IReadOnlyList<T> list, T value)
        {
            for (var i = 0; i < list.Count; i++)
                if (EqualityComparer<T>.Default.Equals(list[i], value)) return i;
            return -1;
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
            if (_label != null) return;
            UiTheme.Ensure();
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = true };
            _label.normal.textColor = Color.white;
            _title = new GUIStyle(UiArt.Title(30)) { wordWrap = true };
            _small = new GUIStyle(_label) { fontSize = 14 };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 18, richText = true };
            _tab = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, alignment = TextAnchor.MiddleCenter };
            _tab.normal.textColor = _tab.hover.textColor = Color.white;
            _keyButton = new GUIStyle(GUI.skin.button) { fontSize = 15, richText = true };
        }
    }
}
