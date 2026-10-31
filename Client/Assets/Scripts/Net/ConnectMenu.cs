using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Client.Fx;
using Game.Shared.Accounts;
using Game.Shared.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client.Net
{
    /// <summary>
    /// Tela inicial: entrar com a conta (ou criar), escolher ou criar personagem e jogar; ou jogar offline.
    /// Ao escolher o personagem, recarrega a cena já no modo online com o token e o personagem.
    /// Com <c>-autologin conta senha</c> faz tudo sozinho (testes): cria conta e personagem se faltarem.
    /// </summary>
    public sealed class ConnectMenu : MonoBehaviour
    {
        private enum Screen
        {
            Login,
            Register,
            Characters,
            CreateCharacter,
            Terms,
            Busy,
        }

        private Screen _screen = Screen.Login;
        private string _address = "127.0.0.1:7777";

        // Servidor escolhido na lista (o último fica salvo); "outro endereço" é para testes e servidores fora da lista.
        private const string ChosenServerKey = "servidor.escolhido";
        private const float StatusEverySeconds = 15f;
        private int _selected;
        private bool _customAddress;
        private string _apiFor = "";
        private GUIStyle _serverRow;
        private string _account = "";
        private string _password = "";
        private string _passwordAgain = "";
        private string _newName = "";
        private CharacterClass _newClass = CharacterClass.Blade;
        private int _newHair;
        private int _newCloth;
        private int _newHead;
        private int _newSkin;
        private bool _newHideHelmet = true; // na criação a prévia começa sem elmo, para ver o penteado
        private readonly CharacterPreview _preview = new CharacterPreview();
        private int _previewed; // personagem da lista mostrado na prévia (o do mouse; senão o último mostrado)
        private string _message = "";
        private bool _messageIsError;
        private string _busyText = "";
        private AccountClient _api;
        private List<CharacterSummary> _characters = new List<CharacterSummary>();
        private GUIStyle _title;
        private GUIStyle _label;
        private GUIStyle _small;

        // Termos e política: aceite no cadastro, leitura e novo aceite quando a versão muda.
        private bool _acceptTerms;
        private bool _termsUpdate;
        private string _termsShown = Legal.TermsName;
        private Screen _termsReturn = Screen.Register;
        private Vector2 _termsScroll;
        private GUIStyle _termsText;

        public static bool IsOpen => !GameMode.ChoiceMade;

        // Fundo da tela de entrada: uma foto do mundo (a ponte no fim da tarde), tirada uma vez. Depois a câmera do
        // jogo fica desligada até escolher como jogar: nada se mexe atrás do menu e a placa de vídeo descansa.
        private RenderTexture _backdrop;
        private bool _backdropReady;
        private Camera _backdropCamera;
        private bool _forcedTime;

        private System.Collections.IEnumerator Start()
        {
            if (GameMode.ChoiceMade) yield break;
            World.Atmosphere.ForcedTime = 0.62f;
            _forcedTime = true;
            // Espera o céu, a névoa e a luz chegarem no fim da tarde (a atmosfera muda aos poucos).
            yield return new WaitForSecondsRealtime(0.8f);
            if (!GameMode.ChoiceMade) CaptureBackdrop();
        }

        private void CaptureBackdrop()
        {
            var camera = Camera.main;
            if (camera == null) return;
            // O personagem não entra na foto.
            var hidden = new List<Renderer>();
            foreach (var motor in FindObjectsByType<Player.PlayerMotor>(FindObjectsSortMode.None))
            foreach (var renderer in motor.GetComponentsInChildren<Renderer>())
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    hidden.Add(renderer);
                }

            var riverAt = World.Ground.RiverZ(0f);
            camera.transform.position = World.Ground.Point(9f, World.Ground.RiverZ(9f) - 9f) + Vector3.up * 4.5f;
            camera.transform.LookAt(World.Ground.Point(0f, riverAt));
            _backdrop = new RenderTexture(UnityEngine.Screen.width, UnityEngine.Screen.height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = _backdrop;
            camera.Render();
            camera.targetTexture = null;
            camera.enabled = false;
            _backdropCamera = camera;
            _backdropReady = true;
            foreach (var renderer in hidden) renderer.enabled = true;
        }

        /// <summary>Saiu da tela de entrada: o jogo volta a ser desenhado e a hora volta ao relógio.</summary>
        private void ReleaseBackdrop()
        {
            // Só solta a hora que a própria tela de entrada prendeu (os testes também prendem a hora).
            if (_forcedTime) World.Atmosphere.ForcedTime = null;
            _forcedTime = false;
            if (_backdropCamera != null) _backdropCamera.enabled = true;
            _backdropCamera = null;
            if (_backdrop != null) _backdrop.Release();
            _backdrop = null;
            _backdropReady = false;
        }

        /// <summary>Para o teste da tela de entrada: abre a criação com estas escolhas.</summary>
        /// <summary>Para o teste da tela de entrada: abre a seleção com esta lista (sem servidor).</summary>
        internal void OpenCharactersForTest(List<CharacterSummary> characters)
        {
            _characters = characters;
            _screen = Screen.Characters;
            _previewed = 0;
        }

        internal void OpenCreateForTest(CharacterClass characterClass, int hair, int cloth, int head = 0, int skin = 0)
        {
            _screen = Screen.CreateCharacter;
            _newClass = characterClass;
            _newHair = hair;
            _newCloth = cloth;
            _newHead = head;
            _newSkin = skin;
        }

        private void OnDestroy()
        {
            ReleaseBackdrop();
            _preview.Hide();
        }

        private void Awake()
        {
            if (GameMode.ChoiceMade)
            {
                enabled = false;
                return;
            }
            _address = $"{GameMode.Host}:{GameMode.Port}";
            var chosen = "";
            try { chosen = PlayerPrefs.GetString(ChosenServerKey, ""); } catch { }
            _selected = Mathf.Max(0, ServerList.Entries.FindIndex(e => e.Address == chosen));
            _ = ServerList.LoadRemoteAsync();
            if (!string.IsNullOrEmpty(GameMode.ResumeToken)) _ = ResumeAsync();
            else if (!string.IsNullOrEmpty(GameMode.AutoLoginAccount)) _ = AutoLoginAsync();
        }

        /// <summary>
        /// Voltou do jogo para trocar de personagem: abre a lista da conta com o mesmo login. Se o login venceu (ou o
        /// servidor reiniciou), cai na tela de entrada com o aviso.
        /// </summary>
        private async Task ResumeAsync()
        {
            _api = new AccountClient(GameMode.Host, GameMode.Port);
            _apiFor = Chosen?.ApiUrl ?? "";
            _api.UseToken(GameMode.ResumeToken);
            GameMode.ClearResume();
            await Run(Loc.T("login.loading_characters"), LoadCharactersAsync);
        }

        // ===== Ações =====

        private async Task LoginAsync(string acceptTerms = "")
        {
            if (!PrepareApi()) return;
            await Run(Loc.T("login.entering"), async () =>
            {
                var login = await _api.LoginAsync(_account, _password, acceptTerms);
                if (!login.Ok && login.ErrorKey == "auth.terms_updated")
                {
                    // Termos novos: mostra o texto com o botão de aceitar (que tenta o login de novo).
                    ShowError(login.Error);
                    OpenTerms(Legal.TermsName, Screen.Login, update: true);
                    return;
                }
                if (!login.Ok)
                {
                    ShowError(login.Error);
                    _screen = Screen.Login;
                    return;
                }
                _password = "";
                await LoadCharactersAsync();
            });
        }

        private async Task RegisterAsync()
        {
            var error = (AccountRules.ValidateAccount(_account) ?? AccountRules.ValidatePassword(_password))?.ToString();
            if (error == null && _password != _passwordAgain) error = Loc.T("login.passwords_differ");
            if (error == null && !_acceptTerms) error = Loc.T("auth.terms_required");
            if (error != null)
            {
                ShowError(error);
                return;
            }
            if (!PrepareApi()) return;
            await Run(Loc.T("login.creating_account"), async () =>
            {
                var register = await _api.RegisterAsync(_account, _password, Legal.Version);
                if (!register.Ok)
                {
                    ShowError(register.Error);
                    _screen = Screen.Register;
                    return;
                }
                ShowInfo(Loc.T("login.account_created"));
                await LoginAsync();
            });
        }

        private async Task LoadCharactersAsync()
        {
            var list = await _api.ListCharactersAsync();
            if (!list.Ok)
            {
                ShowError(list.Error);
                _screen = Screen.Login;
                return;
            }
            _characters = list.Value.Characters ?? new List<CharacterSummary>();
            _screen = _characters.Count == 0 ? Screen.CreateCharacter : Screen.Characters;
        }

        private async Task CreateCharacterAsync()
        {
            var error = AccountRules.ValidateCharacterName(_newName)?.ToString();
            if (error != null)
            {
                ShowError(error);
                return;
            }
            await Run(Loc.T("login.creating_character"), async () =>
            {
                var created = await _api.CreateCharacterAsync(_newName, _newClass, _newHair, _newCloth, _newHead, _newSkin);
                if (!created.Ok)
                {
                    ShowError(created.Error);
                    _screen = Screen.CreateCharacter;
                    return;
                }
                _newName = "";
                ShowInfo(Loc.T("login.character_created", created.Value.Name));
                await LoadCharactersAsync();
            });
        }

        private void Play(CharacterSummary character)
        {
            GameMode.SetOnlineCharacter(GameMode.Host, GameMode.Port, _api.Token, character.Id, character.Name);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Testes: entra (ou cria a conta), usa o primeiro personagem (ou cria um) e joga.</summary>
        private async Task AutoLoginAsync()
        {
            _account = GameMode.AutoLoginAccount;
            _password = GameMode.AutoLoginPassword;
            if (!PrepareApi()) return;
            _screen = Screen.Busy;
            _busyText = Loc.T("login.auto");

            var login = await _api.LoginAsync(_account, _password);
            if (!login.Ok)
            {
                // Teste automático: aceita os termos em nome de quem rodou o teste.
                await _api.RegisterAsync(_account, _password, Legal.Version);
                login = await _api.LoginAsync(_account, _password, Legal.Version);
            }
            if (!login.Ok)
            {
                ShowError(Loc.T("login.auto_failed", login.Error));
                _screen = Screen.Login;
                return;
            }

            var list = await _api.ListCharactersAsync();
            var characters = list.Ok ? list.Value.Characters : new List<CharacterSummary>();
            if (characters.Count == 0)
            {
                var name = "Teste" + Mathf.Abs(_account.GetHashCode() % 100000);
                var created = await _api.CreateCharacterAsync(name, CharacterClass.Blade);
                if (!created.Ok)
                {
                    ShowError(Loc.T("login.auto_create_failed", created.Error));
                    _screen = Screen.Login;
                    return;
                }
                characters = new List<CharacterSummary> { created.Value };
            }
            Debug.Log($"[Login] Login automático como {_account}, personagem {characters[0].Name} (id {characters[0].Id})");
            Play(characters[0]);
        }

        /// <summary>O servidor escolhido na lista (nulo com "outro endereço" ou lista vazia).</summary>
        private ServerEntry Chosen => !_customAddress && _selected < ServerList.Entries.Count ? ServerList.Entries[_selected] : null;

        private bool PrepareApi()
        {
            var entry = Chosen;
            if (entry == null)
            {
                if (!GameMode.TryParseAddress(_address, out var host, out var port))
                {
                    ShowError(Loc.T("login.bad_address"));
                    return false;
                }
                entry = new ServerEntry { Host = host, Port = port };
            }
            GameMode.SetServer(entry);
            if (_api == null || _apiFor != entry.ApiUrl) _api = new AccountClient(entry.Host, entry.Port);
            _apiFor = entry.ApiUrl;
            try
            {
                PlayerPrefs.SetString(ChosenServerKey, entry.Address);
                PlayerPrefs.Save();
            }
            catch { }
            return true;
        }

        private async Task Run(string busyText, System.Func<Task> work)
        {
            var previous = _screen;
            _screen = Screen.Busy;
            _busyText = busyText;
            await work();
            if (_screen == Screen.Busy) _screen = previous;
        }

        private void OpenTerms(string name, Screen back, bool update = false)
        {
            _termsShown = name;
            _termsReturn = back;
            _termsUpdate = update;
            _termsScroll = Vector2.zero;
            _screen = Screen.Terms;
        }

        /// <summary>Texto dos termos ou da política (Resources/Legal do Shared; o mesmo que o servidor publica).</summary>
        private static string LegalText(string name)
        {
            var asset = Resources.Load<TextAsset>("Legal/" + name);
            return asset != null ? asset.text : Loc.T("login.terms_missing");
        }

        private void ShowError(string text)
        {
            _message = text;
            _messageIsError = true;
        }

        private void ShowInfo(string text)
        {
            _message = text;
            _messageIsError = false;
        }

        // ===== Desenho =====

        private void OnGUI()
        {
            if (GameMode.ChoiceMade)
            {
                if (_backdrop != null || _forcedTime) ReleaseBackdrop();
                return;
            }
            Game.Client.UI.UiTheme.Ensure();
            _title ??= new GUIStyle(Game.Client.UI.UiArt.Title(32, TextAnchor.MiddleCenter));
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 17 };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };

            // Foto do mundo ao fundo, escurecida para o menu ler bem (preto até a foto ficar pronta).
            var screen = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            if (_backdropReady && Event.current.type == EventType.Repaint) GUI.DrawTexture(screen, _backdrop, ScaleMode.ScaleAndCrop, false);
            GUI.color = new Color(0, 0, 0, _backdropReady ? 0.45f : 1f);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var scale = Mathf.Max(1f, UnityEngine.Screen.height / 900f);
            // Os termos precisam de mais espaço para ler.
            var wide = _screen == Screen.CreateCharacter || (_screen == Screen.Characters && _characters.Count > 0);
            var width = (_screen == Screen.Terms || wide ? 820 : 480) * scale;
            var height = (_screen == Screen.Terms ? 780 : _screen == Screen.CreateCharacter ? 700 : wide ? 600 : 520) * scale;
            var area = new Rect((UnityEngine.Screen.width - width) / 2, (UnityEngine.Screen.height - height) / 2, width, height);
            Game.Client.UI.UiTheme.Panel(area, 0.94f);
            GUILayout.BeginArea(new Rect(area.x + 14 * scale, area.y + 6 * scale, area.width - 28 * scale, area.height - 12 * scale));
            GUILayout.Space(8 * scale);
            GUILayout.Label(Loc.T("login.title"), _title);
            GUILayout.Space(12 * scale);

            switch (_screen)
            {
                case Screen.Login: DrawLogin(scale); break;
                case Screen.Register: DrawRegister(scale); break;
                case Screen.Characters: DrawCharacters(scale); break;
                case Screen.CreateCharacter: DrawCreate(scale); break;
                case Screen.Terms: DrawTerms(scale); break;
                case Screen.Busy: GUILayout.Label(_busyText, _label); break;
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GUILayout.Space(8 * scale);
                GUI.color = _messageIsError ? new Color(1f, 0.55f, 0.5f) : new Color(0.6f, 1f, 0.6f);
                GUILayout.Label(_message, _small);
                GUI.color = Color.white;
            }
            GUILayout.EndArea();

            // Versão do jogo no canto (a do Player Settings; o launcher publica com ela).
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(UnityEngine.Screen.width - 260 * scale, UnityEngine.Screen.height - 30 * scale, 248 * scale, 24 * scale),
                Loc.T("login.version", Application.version), new GUIStyle(_small) { alignment = TextAnchor.LowerRight, wordWrap = false });
            GUI.color = Color.white;
        }

        private void Update()
        {
            // Prévia do personagem só na tela de criação.
            if (!GameMode.ChoiceMade && _screen == Screen.CreateCharacter)
                _preview.Show(_newClass, new CharacterPaint.Style(_newHair, _newCloth, _newHead, _newSkin, _newHideHelmet));
            else if (!GameMode.ChoiceMade && _screen == Screen.Characters && _characters.Count > 0)
            {
                var shown = _characters[Mathf.Clamp(_previewed, 0, _characters.Count - 1)];
                _preview.Show((CharacterClass)shown.ClassId, new CharacterPaint.Style(shown.Hair, shown.Cloth, shown.Head, shown.Skin, shown.HideHelmet),
                    new CharacterPaint.Gear(shown.ArmorGrade, shown.GlovesGrade, shown.BootsGrade));
            }
            else _preview.Hide();

            // Status dos servidores enquanto a lista está na tela.
            if (GameMode.ChoiceMade || _screen != Screen.Login) return;
            foreach (var entry in ServerList.Entries)
                if (!entry.Checking && Time.unscaledTime - entry.CheckedAt > (entry.Checks < 2 ? 0.5f : StatusEverySeconds))
                    _ = ServerList.RefreshAsync(entry);
        }

        private static string StatusText(ServerEntry entry) => entry.Status switch
        {
            ServerStatus.Online => Loc.T("servers.online", entry.Players, entry.PingMs),
            ServerStatus.Unstable => Loc.T("servers.unstable"),
            ServerStatus.OtherVersion => Loc.T("servers.other_version"),
            ServerStatus.Offline => Loc.T("servers.offline"),
            _ => Loc.T("servers.checking"),
        };

        private void DrawServers(float scale)
        {
            GUILayout.Label(Loc.T("login.server_choose"), _label);
            if (_serverRow == null)
            {
                _serverRow = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true, fontSize = 15 };
                _serverRow.padding = new RectOffset(12, 12, 2, 2);
            }
            for (var i = 0; i < ServerList.Entries.Count; i++)
            {
                var entry = ServerList.Entries[i];
                var selected = !_customAddress && i == _selected;
                var text = (selected ? "▶  " : "     ") + $"<b>{entry.Name}</b>     {StatusText(entry)}";
                if (GUILayout.Toggle(selected, text, _serverRow, GUILayout.Height(32 * scale)) && !selected)
                {
                    _selected = i;
                    _customAddress = false;
                    _message = "";
                }
            }
            if (GUILayout.Toggle(_customAddress, (_customAddress ? "▶  " : "     ") + Loc.T("login.server_other"), _serverRow, GUILayout.Height(28 * scale)) != _customAddress)
                _customAddress = !_customAddress;
            if (_customAddress) _address = GUILayout.TextField(_address, 60, GUILayout.Height(26 * scale));
            GUILayout.Space(6 * scale);
        }

        private void DrawLogin(float scale)
        {
            DrawServers(scale);
            GUILayout.Label(Loc.T("login.account"), _label);
            _account = GUILayout.TextField(_account, AccountRules.MaxAccountLength, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.password"), _label);
            _password = GUILayout.PasswordField(_password, '•', AccountRules.MaxPasswordLength, GUILayout.Height(26 * scale));
            GUILayout.Space(10 * scale);
            if (GUILayout.Button(Loc.T("login.enter"), GUILayout.Height(38 * scale))) _ = LoginAsync();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("login.create_account"), GUILayout.Height(30 * scale)))
            {
                _screen = Screen.Register;
                _message = "";
            }
            if (GUILayout.Button(Loc.T("login.offline"), GUILayout.Height(30 * scale))) GameMode.SetOffline();
            if (GUILayout.Button(Loc.T("options.title"), GUILayout.Height(30 * scale))) UI.OptionsWindow.Open();
            GUILayout.EndHorizontal();
        }

        private void DrawRegister(float scale)
        {
            GUILayout.Label(Loc.T("login.new_account"), _label);
            GUILayout.Label(Loc.T("login.account_rule", AccountRules.MinAccountLength, AccountRules.MaxAccountLength), _small);
            _account = GUILayout.TextField(_account, AccountRules.MaxAccountLength, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.password_rule", AccountRules.MinPasswordLength), _small);
            _password = GUILayout.PasswordField(_password, '•', AccountRules.MaxPasswordLength, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.repeat_password"), _small);
            _passwordAgain = GUILayout.PasswordField(_passwordAgain, '•', AccountRules.MaxPasswordLength, GUILayout.Height(26 * scale));
            GUILayout.Space(8 * scale);
            _acceptTerms = GUILayout.Toggle(_acceptTerms, " " + Loc.T("login.accept_terms"), GUILayout.Height(24 * scale));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("login.view_terms"), GUILayout.Height(24 * scale))) OpenTerms(Legal.TermsName, Screen.Register);
            if (GUILayout.Button(Loc.T("login.view_privacy"), GUILayout.Height(24 * scale))) OpenTerms(Legal.PrivacyName, Screen.Register);
            GUILayout.EndHorizontal();
            GUILayout.Space(8 * scale);
            if (GUILayout.Button(Loc.T("login.create_and_enter"), GUILayout.Height(38 * scale))) _ = RegisterAsync();
            if (GUILayout.Button(Loc.T("ui.back"), GUILayout.Height(28 * scale)))
            {
                _screen = Screen.Login;
                _message = "";
            }
        }

        private void DrawTerms(float scale)
        {
            _termsText ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = false };
            _termsText.fontSize = Mathf.RoundToInt(15 * scale);
            if (_termsUpdate) GUILayout.Label(Loc.T("login.terms_updated_title"), _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_termsShown == Legal.TermsName, Loc.T("login.view_terms"), GUI.skin.button, GUILayout.Height(28 * scale)))
                _termsShown = Legal.TermsName;
            if (GUILayout.Toggle(_termsShown == Legal.PrivacyName, Loc.T("login.view_privacy"), GUI.skin.button, GUILayout.Height(28 * scale)))
                _termsShown = Legal.PrivacyName;
            GUILayout.EndHorizontal();
            _termsScroll = GUILayout.BeginScrollView(_termsScroll, GUI.skin.box, GUILayout.Height((_termsUpdate ? 470 : 510) * scale));
            GUILayout.Label(LegalText(_termsShown), _termsText);
            GUILayout.EndScrollView();
            GUILayout.Space(8 * scale);
            if (_termsUpdate && GUILayout.Button(Loc.T("login.accept_and_enter"), GUILayout.Height(38 * scale)))
            {
                _termsUpdate = false;
                _message = "";
                _ = LoginAsync(Legal.Version);
            }
            if (GUILayout.Button(_termsUpdate ? Loc.T("ui.back") : Loc.T("login.close"), GUILayout.Height(28 * scale)))
            {
                _termsUpdate = false;
                _screen = _termsReturn;
            }
        }

        private void DrawCharacters(float scale)
        {
            if (_characters.Count == 0)
            {
                DrawCharacterList(scale);
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(450 * scale));
            DrawCharacterList(scale);
            GUILayout.EndVertical();
            GUILayout.Space(12 * scale);
            GUILayout.BeginVertical();
            DrawPreview(scale, Loc.T("login.preview_select"));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary>A prévia 3D com moldura e uma linha de texto embaixo.</summary>
        private void DrawPreview(float scale, string caption)
        {
            var preview = GUILayoutUtility.GetRect(300 * scale, 366 * scale, GUILayout.ExpandWidth(false));
            if (_preview.Texture != null) GUI.DrawTexture(preview, _preview.Texture, ScaleMode.ScaleAndCrop, false);
            Game.Client.UI.UiTheme.Frame(preview);
            GUILayout.Label(caption, _small, GUILayout.Width(300 * scale));
        }

        private void DrawCharacterList(float scale)
        {
            GUILayout.Label(Loc.T("login.your_characters", _characters.Count, AccountRules.MaxCharactersPerAccount), _label);
            for (var i = 0; i < _characters.Count; i++)
            {
                var character = _characters[i];
                var text = Loc.T("login.character_row", character.Name, AccountRules.ClassDisplayName((CharacterClass)character.ClassId), character.Level);
                if (GUILayout.Button(text, GUILayout.Height(44 * scale))) Play(character);
                // Com o mouse em cima, a prévia mostra este personagem.
                if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) _previewed = i;
            }
            GUILayout.Space(10 * scale);
            if (_characters.Count < AccountRules.MaxCharactersPerAccount && GUILayout.Button(Loc.T("login.create_character"), GUILayout.Height(30 * scale)))
            {
                _screen = Screen.CreateCharacter;
                _message = "";
            }
            if (GUILayout.Button(Loc.T("login.logout"), GUILayout.Height(28 * scale)))
            {
                _screen = Screen.Login;
                _api = null;
                _message = "";
            }
        }

        private void DrawCreate(float scale)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(450 * scale));
            DrawCreateOptions(scale);
            GUILayout.EndVertical();
            GUILayout.Space(12 * scale);
            GUILayout.BeginVertical();
            DrawPreview(scale, Loc.T("login.preview_hint"));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary>Fileira de quadradinhos de cor (o primeiro é a cor original do modelo).</summary>
        private int ColorRow(string title, Color[] palette, int current, float scale)
        {
            GUILayout.Label(title, _label);
            GUILayout.BeginHorizontal();
            for (var i = 0; i < palette.Length; i++)
            {
                var rect = GUILayoutUtility.GetRect((i == 0 ? 86 : 48) * scale, 34 * scale, GUILayout.ExpandWidth(false));
                if (GUI.Button(rect, i == 0 ? Loc.T("login.color_original") : "")) current = i;
                if (i > 0)
                {
                    GUI.color = palette[i];
                    GUI.DrawTexture(new Rect(rect.x + 4 * scale, rect.y + 4 * scale, rect.width - 8 * scale, rect.height - 8 * scale), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                if (i == current)
                {
                    // A escolhida ganha uma moldura clara e grossa.
                    var t = 3 * scale;
                    GUI.color = new Color(1f, 0.86f, 0.45f);
                    GUI.DrawTexture(new Rect(rect.x - t, rect.y - t, rect.width + 2 * t, t), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.x - t, rect.yMax, rect.width + 2 * t, t), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.x - t, rect.y, t, rect.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.xMax, rect.y, t, rect.height), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                GUILayout.Space(4 * scale);
            }
            GUILayout.EndHorizontal();
            return current;
        }

        private static readonly string[] HeadLabels =
            { "login.head_0", "login.head_1", "login.head_2", "login.head_3", "login.head_4", "login.head_5" };

        /// <summary>Penteados em duas fileiras de botões com o nome (a prévia mostra cada um).</summary>
        private int HeadRow(int current, float scale)
        {
            GUILayout.Label(Loc.T("login.head"), _label);
            for (var row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (var i = row * 3; i < row * 3 + 3 && i < HeadLabels.Length; i++)
                    if (GUILayout.Toggle(current == i, Loc.T(HeadLabels[i]), GUI.skin.button, GUILayout.Height(28 * scale))) current = i;
                GUILayout.EndHorizontal();
            }
            return current;
        }

        private void DrawCreateOptions(float scale)
        {
            GUILayout.Label(Loc.T("login.new_character"), _label);
            GUILayout.Label(Loc.T("login.name_rule", AccountRules.MinCharacterNameLength, AccountRules.MaxCharacterNameLength), _small);
            _newName = GUILayout.TextField(_newName, AccountRules.MaxCharacterNameLength, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.class"), _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_newClass == CharacterClass.Blade, " " + Loc.T("login.class_blade"), GUI.skin.button, GUILayout.Height(34 * scale)))
                _newClass = CharacterClass.Blade;
            if (GUILayout.Toggle(_newClass == CharacterClass.Arcanist, " " + Loc.T("login.class_arcanist"), GUI.skin.button, GUILayout.Height(34 * scale)))
                _newClass = CharacterClass.Arcanist;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_newClass == CharacterClass.Ranger, " " + Loc.T("login.class_ranger"), GUI.skin.button, GUILayout.Height(34 * scale)))
                _newClass = CharacterClass.Ranger;
            if (GUILayout.Toggle(_newClass == CharacterClass.Guardian, " " + Loc.T("login.class_guardian"), GUI.skin.button, GUILayout.Height(34 * scale)))
                _newClass = CharacterClass.Guardian;
            GUILayout.EndHorizontal();
            _newHair = ColorRow(Loc.T("login.hair"), CharacterPaint.HairColors, _newHair, scale);
            _newCloth = ColorRow(Loc.T("login.cloth"), CharacterPaint.ClothColors, _newCloth, scale);
            _newSkin = ColorRow(Loc.T("login.skin"), CharacterPaint.SkinColors, _newSkin, scale);
            // O Guardião (esqueleto grande) não troca de cabeça: o penteado fica o do modelo.
            if (_newClass != CharacterClass.Guardian) _newHead = HeadRow(_newHead, scale);
            _newHideHelmet = !GUILayout.Toggle(!_newHideHelmet, " " + Loc.T("login.show_helmet"), GUILayout.Height(24 * scale));
            GUILayout.Space(10 * scale);
            if (GUILayout.Button(Loc.T("login.create"), GUILayout.Height(38 * scale))) _ = CreateCharacterAsync();
            if (_characters.Count > 0 && GUILayout.Button(Loc.T("ui.back"), GUILayout.Height(28 * scale)))
            {
                _screen = Screen.Characters;
                _message = "";
            }
        }
    }
}
