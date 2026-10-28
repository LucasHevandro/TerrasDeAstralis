using System.Collections.Generic;
using Game.Client.Net;
using Game.Shared.Accounts;
using Game.Shared.Loot;
using Game.Shared.Net;
using Game.Shared.Social;
using Game.Shared.Text;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Modo online: chat (Enter abre e envia, Esc cancela, Tab troca o canal) e quadro do grupo com a vida e o MP de
    /// cada membro, mais o convite para aceitar ou recusar. Quem decide tudo é o servidor; aqui só mostra e pede.
    /// </summary>
    public sealed class SocialWindows : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;
        private const int MaxLines = 80;
        private const int VisibleLines = 9;
        private const float FadeAfterSeconds = 12f;
        private const string InputControl = "chat-input";
        private const string FriendControl = "friend-name";

        private static readonly Color Panel = new Color(0.05f, 0.06f, 0.09f, 0.82f);
        private static readonly Color HpColor = new Color(0.85f, 0.2f, 0.2f);
        private static readonly Color MpColor = new Color(0.25f, 0.45f, 0.95f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.35f);

        private readonly struct Line
        {
            public readonly ChatChannel Channel;
            public readonly string Text;
            public readonly float At;

            public Line(ChatChannel channel, string text, float at)
            {
                Channel = channel;
                Text = text;
                At = at;
            }
        }

        private readonly List<Line> _lines = new List<Line>();
        private NetworkSession _session;
        private ChatChannel _channel = ChatChannel.Map;
        private string _draft = "";
        private bool _focusInput;
        private string _lastWhisperFrom = "";
        private int _scroll;

        private PartyState _party = new PartyState();
        private SocialLists _lists = new SocialLists();
        private bool _friendsOpen;
        private string _friendName = "";
        private Vector2 _friendsScroll;
        private bool _cursorToEnd;
        private PartyInvite _invite;
        private float _inviteUntil;
        private FriendInvite _friendInvite;
        private float _friendInviteUntil;

        // Sorteios do loot do grupo esperando resposta (o mais antigo aparece primeiro).
        private readonly List<(LootRollStart Roll, float Until)> _rolls = new List<(LootRollStart, float)>();

        private GUIStyle _label;
        private GUIStyle _small;
        private GUIStyle _wrapped;
        private GUIStyle _input;
        private GUIStyle _button;
        private float _scale = 1f;
        private Vector2 _offset;

        /// <summary>Retângulos desenhados neste quadro (em coordenadas virtuais), para o clique não ir para o mundo.</summary>
        private static readonly List<Rect> OpenRects = new List<Rect>();

        public static SocialWindows Instance { get; private set; }

        public static bool Contains(Vector2 virtualPoint)
        {
            foreach (var rect in OpenRects) if (rect.Contains(virtualPoint)) return true;
            return false;
        }

        private static void AddRect(Rect rect) => OpenRects.Add(rect);
        public PartyState Party => _party;
        public SocialLists Lists => _lists;
        public void SetFriendsOpen(bool open) => _friendsOpen = open;
        public PartyInvite PendingInvite => Time.unscaledTime < _inviteUntil ? _invite : null;
        public FriendInvite PendingFriendInvite => Time.unscaledTime < _friendInviteUntil ? _friendInvite : null;
        public IEnumerable<string> RecentLines { get { foreach (var line in _lines) yield return line.Text; } }

        /// <summary>Para o teste automático: manda uma linha como se o jogador tivesse digitado.</summary>
        public void Submit(string text) => SubmitLine(text);

        public void AcceptInvite() => Answer(true);

        private void Awake()
        {
            if (!GameMode.IsOnline)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            _session = NetworkSession.Instance;
            if (_session != null) _session.MessageReceived += OnMessage;
            AddLine(ChatChannel.System, Loc.T("chat.hint"));
        }

        private void OnDestroy()
        {
            if (_session != null) _session.MessageReceived -= OnMessage;
            if (Instance == this) Instance = null;
            OpenRects.Clear();
            GameInput.Typing = false;
            GameInput.FieldFocused = false;
        }

        private void Update()
        {
            var keyboard = GameInput.Keyboard;
            if (keyboard == null || ConnectMenu.IsOpen) return;
            if (KeyBindings.Pressed(GameAction.Friends)) Fx.UiSounds.Toggle(_friendsOpen = !_friendsOpen);
            if (keyboard.escapeKey.wasPressedThisFrame && !OptionsWindow.EscapeTaken) _friendsOpen = false;
            FriendsOpen = _friendsOpen;
        }

        /// <summary>A lista de amigos está aberta (o Esc fecha ela antes de abrir as opções).</summary>
        public static bool FriendsOpen { get; private set; }

        private void OnMessage(INetMessage message)
        {
            switch (message)
            {
                case ChatMessage chat:
                    OnChat(chat);
                    break;
                case PartyState party:
                    _party = party;
                    break;
                case SocialLists lists:
                    _lists = lists;
                    break;
                case PartyInvite invite:
                    _invite = invite;
                    _inviteUntil = Time.unscaledTime + invite.Seconds;
                    AddLine(ChatChannel.System, Loc.T("party.invited_you", invite.InviterName));
                    break;
                case LootRollStart roll:
                    _rolls.Add((roll, Time.unscaledTime + roll.Seconds));
                    break;
                case FriendInvite friend:
                    _friendInvite = friend;
                    _friendInviteUntil = Time.unscaledTime + friend.Seconds;
                    AddLine(ChatChannel.System, Loc.T("friend.invite_title", Escape(friend.InviterName)));
                    break;
            }
        }

        private void OnChat(ChatMessage chat)
        {
            var text = Escape(chat.Display);
            switch (chat.Channel)
            {
                case ChatChannel.System:
                    AddLine(chat.Channel, text);
                    break;
                case ChatChannel.Whisper when chat.ToName.Length > 0:
                    AddLine(chat.Channel, Loc.T("chat.whisper_to", Escape(chat.ToName), text));
                    break;
                case ChatChannel.Whisper:
                    _lastWhisperFrom = chat.FromName;
                    AddLine(chat.Channel, Loc.T("chat.whisper_from", Escape(chat.FromName), text));
                    break;
                default:
                    AddLine(chat.Channel, $"<b>{Escape(chat.FromName)}</b>: {text}");
                    break;
            }
        }

        private void AddLine(ChatChannel channel, string text)
        {
            _lines.Add(new Line(channel, text, Time.unscaledTime));
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
            _scroll = 0;
        }

        // ----- Desenho -----

        // Só GUI (sem GUILayout): sem a passada de layout, o OnGUI roda menos vezes por quadro.
        private void OnEnable() => useGUILayout = false;

        private void OnGUI()
        {
            if (ConnectMenu.IsOpen || _session == null || !_session.IsWelcomed) return;
            EnsureStyles();
            OpenRects.Clear();
            HandleKeys(Event.current);

            _scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            _offset = new Vector2((Screen.width - VirtualWidth * _scale) / 2, (Screen.height - VirtualHeight * _scale) / 2);
            GUI.matrix = Matrix4x4.TRS(new Vector3(_offset.x, _offset.y, 0), Quaternion.identity, new Vector3(_scale, _scale, 1));
            GUI.depth = -5;

            DrawParty();
            DrawChat();
            if (_friendsOpen) DrawFriends();
            else GameInput.FieldFocused = false;
            if (PendingInvite != null) DrawInvite();
            else if (PendingFriendInvite != null) DrawFriendInvite();
            DrawLootRoll();

            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
        }

        /// <summary>Teclas do chat: tratadas aqui (antes da caixa de texto) para Enter e Esc não virarem texto.</summary>
        private void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            var enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if (GameInput.FieldFocused)
            {
                // Enter na caixa de nome da janela de amigos adiciona; Esc sai da caixa.
                if (enter) AddFriendFromField();
                if (enter || e.keyCode == KeyCode.Escape)
                {
                    GUI.FocusControl(null);
                    GameInput.FieldFocused = false;
                    e.Use();
                }
                return;
            }
            if (!GameInput.Typing)
            {
                if (!enter) return;
                GameInput.Typing = true;
                _focusInput = true;
                e.Use();
                return;
            }

            if (enter)
            {
                SubmitLine(_draft);
                CloseInput();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CloseInput();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Tab)
            {
                _channel = _channel == ChatChannel.Map ? ChatChannel.Party : ChatChannel.Map;
                e.Use();
            }
            else if (e.keyCode == KeyCode.PageUp)
            {
                _scroll = Mathf.Min(_scroll + 3, Mathf.Max(0, _lines.Count - VisibleLines));
                e.Use();
            }
            else if (e.keyCode == KeyCode.PageDown)
            {
                _scroll = Mathf.Max(0, _scroll - 3);
                e.Use();
            }
        }

        private void CloseInput()
        {
            _draft = "";
            GameInput.Typing = false;
            GUI.FocusControl(null);
        }

        private void DrawChat()
        {
            var typing = GameInput.Typing;
            var rect = new Rect(20, VirtualHeight - 340, 460, 290); // canto inferior esquerdo, acima das linhas de XP
            var now = Time.unscaledTime;

            // Sem digitar, as linhas antigas somem; o fundo só aparece enquanto digita.
            if (typing) UiTheme.Panel(rect);
            // De baixo para cima: linhas longas quebram em várias e ocupam mais altura.
            var width = rect.width - 24;
            var bottom = rect.yMax - 44;
            var top = rect.y + 28;
            var end = _lines.Count - (typing ? _scroll : 0);
            var y = bottom;
            for (var i = end - 1; i >= 0 && end - i <= VisibleLines; i--)
            {
                var line = _lines[i];
                var text = $"<color=#{ColorUtility.ToHtmlStringRGB(ChannelColor(line.Channel))}>{Prefix(line.Channel)}{line.Text}</color>";
                var height = _wrapped.CalcHeight(new GUIContent(text), width) + 2;
                if (y - height < top) break;
                y -= height;
                var alpha = typing ? 1f : Mathf.Clamp01((FadeAfterSeconds + 2f - (now - line.At)) / 2f);
                if (alpha <= 0) continue;
                GUI.color = new Color(0, 0, 0, 0.8f * alpha);
                GUI.Label(new Rect(rect.x + 13, y + 1, width, height), text, _wrapped);
                GUI.color = new Color(1, 1, 1, alpha);
                GUI.Label(new Rect(rect.x + 12, y, width, height), text, _wrapped);
            }
            GUI.color = Color.white;

            if (!typing)
            {
                return;
            }
            AddRect(rect);
            var channelRect = new Rect(rect.x + 8, rect.yMax - 38, 92, 30);
            Box(channelRect, new Color(ChannelColor(_channel).r, ChannelColor(_channel).g, ChannelColor(_channel).b, 0.35f));
            GUI.Label(channelRect, _channel == ChatChannel.Party ? Loc.T("chat.channel_party") : Loc.T("chat.channel_map"), _small.WithCenter());
            GUI.SetNextControlName(InputControl);
            _draft = GUI.TextField(new Rect(channelRect.xMax + 6, rect.yMax - 38, rect.width - 114, 30), _draft, ChatRules.MaxLength, _input);
            if (_focusInput)
            {
                GUI.FocusControl(InputControl);
                _focusInput = false;
            }
            else if (_cursorToEnd && Event.current.type == EventType.Repaint)
            {
                // Abriu com texto pronto ("/s Nome "): o cursor vai para o fim em vez de selecionar tudo.
                var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                editor.text = _draft;
                editor.MoveTextEnd();
                _cursorToEnd = false;
            }
            GUI.Label(new Rect(rect.x + 12, rect.y + 4, rect.width - 24, 22),
                Loc.T("chat.input_hint"), _small);
        }

        private void DrawParty()
        {
            if (_party.Members.Count == 0) return;
            var x = 20f;
            var y = 380f;
            var height = 38f + _party.Members.Count * 62f;
            var panel = new Rect(x, y, 320, height);
            Box(panel, new Color(0, 0, 0, 0.45f));
            AddRect(panel);
            GUI.Label(new Rect(x + 10, y + 4, 300, 26), Loc.T("party.title", _party.Members.Count, PartyRules.MaxMembers), _small);
            if (GUI.Button(new Rect(x + 240, y + 6, 70, 24), Loc.T("party.leave"), _button))
                _session.Send(new PartyCommand { Action = PartyActionKind.Leave });
            y += 36;
            foreach (var member in _party.Members)
            {
                var me = member.Id == _session.PlayerId;
                var leader = member.Id == _party.LeaderId ? "★ " : "";
                var className = AccountRules.ClassDisplayName((CharacterClass)member.Class);
                GUI.color = member.Near || me ? Color.white : new Color(1, 1, 1, 0.55f);
                GUI.Label(new Rect(x + 10, y - 2, 300, 26),
                    Loc.T("party.member_row", leader, Escape(member.Name), me ? " " + Loc.T("party.you") : "", member.Level, className), _small);
                var hp = new Rect(x + 10, y + 26, 300, 16);
                Box(hp, new Color(0, 0, 0, 0.6f));
                Box(new Rect(hp.x, hp.y, hp.width * Mathf.Clamp01(member.Hp / (float)Mathf.Max(1, member.MaxHp)), hp.height), HpColor);
                GUI.Label(new Rect(hp.x, hp.y - 3, hp.width, 22),
                    member.Dead ? Loc.T("party.member_dead") : $"<size=12>{member.Hp}/{member.MaxHp}</size>", _small.WithCenter());
                var mp = new Rect(x + 10, y + 44, 300, 6);
                Box(mp, new Color(0, 0, 0, 0.6f));
                Box(new Rect(mp.x, mp.y, mp.width * Mathf.Clamp01(member.Mp / (float)Mathf.Max(1, member.MaxMp)), mp.height), MpColor);
                GUI.color = Color.white;
                y += 62;
            }
        }

        /// <summary>Janela de amigos (O): adicionar pelo nome, quem está online, sussurrar, convidar, remover e bloqueados.</summary>
        private void DrawFriends()
        {
            var rect = new Rect(460, 140, 500, 440); // à direita da ajuda
            UiTheme.Panel(rect);
            AddRect(rect);
            GUI.Label(new Rect(rect.x + 14, rect.y + 8, rect.width - 28, 30),
                Loc.T("friends.title", _lists.Friends.Count, RelationRules.MaxFriends, KeyBindings.Label(GameAction.Friends)), _label);

            GUI.SetNextControlName(FriendControl);
            _friendName = GUI.TextField(new Rect(rect.x + 14, rect.y + 46, 350, 30), _friendName, AccountRules.MaxCharacterNameLength, _input);
            var fieldRect = new Rect(rect.x + 14, rect.y + 46, 350, 30);
            if (Event.current.type == EventType.MouseDown && !fieldRect.Contains(Event.current.mousePosition)) GUI.FocusControl(null);
            if (GUI.Button(new Rect(rect.x + 372, rect.y + 46, 114, 30), Loc.T("friends.add"), _button)) AddFriendFromField();
            GameInput.FieldFocused = GUI.GetNameOfFocusedControl() == FriendControl;

            var listRect = new Rect(rect.x + 8, rect.y + 86, rect.width - 16, rect.height - 94);
            var rowHeight = 40f;
            var contentHeight = 30 + _lists.Friends.Count * rowHeight + 40 + _lists.Blocked.Count * 32 + 10;
            _friendsScroll = GUI.BeginScrollView(listRect, _friendsScroll, new Rect(0, 0, listRect.width - 20, contentHeight));
            var y = 0f;
            if (_lists.Friends.Count == 0)
            {
                GUI.Label(new Rect(6, y, 400, 26), Loc.T("friends.empty"), _small);
                y += 30;
            }
            foreach (var friend in _lists.Friends)
            {
                Box(new Rect(8, y + 13, 12, 12), friend.Online ? new Color(0.3f, 0.9f, 0.35f) : new Color(0.45f, 0.45f, 0.45f));
                var info = friend.Online
                    ? Loc.T("friends.info", friend.Level, AccountRules.ClassDisplayName((CharacterClass)friend.Class))
                    : Loc.T("friends.offline");
                GUI.color = friend.Online ? Color.white : new Color(1, 1, 1, 0.6f);
                GUI.Label(new Rect(28, y + 6, 200, 28), $"<b>{Escape(friend.Name)}</b> <size=12>{info}</size>", _small);
                GUI.color = Color.white;
                if (friend.Online)
                {
                    if (GUI.Button(new Rect(226, y + 6, 88, 26), Loc.T("friends.whisper"), _button)) StartWhisper(friend.Name);
                    if (GUI.Button(new Rect(318, y + 6, 66, 26), Loc.T("friends.invite"), _button))
                        _session.Send(new PartyCommand { Action = PartyActionKind.Invite, TargetName = friend.Name });
                }
                if (GUI.Button(new Rect(388, y + 6, 76, 26), Loc.T("friends.remove"), _button)) Relation(RelationActionKind.RemoveFriend, friend.Name);
                y += rowHeight;
            }

            y += 8;
            GUI.Label(new Rect(6, y, 400, 26), Loc.T("friends.blocked_title", _lists.Blocked.Count), _small);
            y += 32;
            foreach (var name in _lists.Blocked)
            {
                GUI.Label(new Rect(28, y, 220, 26), Escape(name), _small);
                if (GUI.Button(new Rect(348, y, 116, 26), Loc.T("friends.unblock"), _button)) Relation(RelationActionKind.Unblock, name);
                y += 32;
            }
            GUI.EndScrollView();
        }

        private void AddFriendFromField()
        {
            var name = _friendName.Trim();
            if (name.Length == 0) return;
            Relation(RelationActionKind.AddFriend, name);
            _friendName = "";
        }

        private void Relation(RelationActionKind action, string name, string text = "") =>
            _session.Send(new SocialCommand { Action = action, TargetName = name, Text = text });

        /// <summary>Abre o chat já com "/s Nome " para sussurrar.</summary>
        private void StartWhisper(string name)
        {
            _draft = $"/s {name} ";
            GameInput.Typing = true;
            _focusInput = true;
            _cursorToEnd = true;
        }

        private void DrawInvite()
        {
            var invite = PendingInvite;
            var rect = new Rect(VirtualWidth / 2 - 230, 300, 460, 130);
            UiTheme.Panel(rect);
            AddRect(rect);
            var seconds = Mathf.CeilToInt(_inviteUntil - Time.unscaledTime);
            GUI.Label(new Rect(rect.x, rect.y + 14, rect.width, 30), Loc.T("party.invite_title", Escape(invite.InviterName)), _label.WithCenter());
            GUI.Label(new Rect(rect.x, rect.y + 44, rect.width, 22), Loc.T("party.invite_expires", seconds), _small.WithCenter());
            if (GUI.Button(new Rect(rect.x + 60, rect.y + 76, 150, 36), Loc.T("party.invite_accept"), _button)) Answer(true);
            if (GUI.Button(new Rect(rect.xMax - 210, rect.y + 76, 150, 36), Loc.T("party.invite_decline"), _button)) Answer(false);
        }

        private void Answer(bool accept)
        {
            var invite = PendingInvite;
            if (invite == null) return;
            _session.Send(new PartyCommand { Action = accept ? PartyActionKind.Accept : PartyActionKind.Decline, TargetId = invite.InviterId });
            _invite = null;
            _inviteUntil = 0;
        }

        /// <summary>Convite de amizade: no mesmo lugar do convite de grupo (o de grupo tem a vez, se vierem os dois).</summary>
        private void DrawFriendInvite()
        {
            var invite = PendingFriendInvite;
            var rect = new Rect(VirtualWidth / 2 - 230, 300, 460, 130);
            UiTheme.Panel(rect);
            AddRect(rect);
            var seconds = Mathf.CeilToInt(_friendInviteUntil - Time.unscaledTime);
            GUI.Label(new Rect(rect.x, rect.y + 14, rect.width, 30), Loc.T("friend.invite_title", Escape(invite.InviterName)), _label.WithCenter());
            GUI.Label(new Rect(rect.x, rect.y + 44, rect.width, 22), Loc.T("friend.invite_expires", seconds), _small.WithCenter());
            if (GUI.Button(new Rect(rect.x + 60, rect.y + 76, 150, 36), Loc.T("friend.invite_accept"), _button)) AnswerFriend(true);
            if (GUI.Button(new Rect(rect.xMax - 210, rect.y + 76, 150, 36), Loc.T("friend.invite_decline"), _button)) AnswerFriend(false);
        }

        /// <summary>
        /// Sorteio do item raro do grupo: o item, quanto tempo falta e Preciso/Quero/Passar ("Preciso" só para quem pode
        /// usar o item). Fica acima da barra de atalhos; com vários, um de cada vez.
        /// </summary>
        private void DrawLootRoll()
        {
            _rolls.RemoveAll(r => Time.unscaledTime >= r.Until);
            if (_rolls.Count == 0) return;
            var (roll, until) = _rolls[0];
            var item = Game.Shared.Items.ItemCatalog.Find(roll.ItemId);
            if (item == null)
            {
                _rolls.RemoveAt(0);
                return;
            }
            var rect = new Rect(VirtualWidth / 2 - 250, VirtualHeight - 400, 500, 150);
            UiTheme.Panel(rect);
            AddRect(rect);

            var icon = new Rect(rect.x + 16, rect.y + 16, 56, 56);
            var color = Game.Client.Loot.LootPickup.ColorFor(item.Rarity);
            var texture = ItemIcons.Get(item.Id);
            if (texture != null) GUI.DrawTexture(icon, texture, ScaleMode.ScaleToFit);
            var name = roll.Quantity > 1 ? $"{item.Name} x{roll.Quantity}" : item.Name;
            GUI.Label(new Rect(rect.x + 84, rect.y + 12, rect.width - 100, 26), $"<b>{Loc.T("loot.roll_title")}</b>", _small);
            GUI.Label(new Rect(rect.x + 84, rect.y + 36, rect.width - 100, 26),
                $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{Escape(name)}</b></color>", _label);
            var seconds = Mathf.CeilToInt(until - Time.unscaledTime);
            var hint = roll.CanNeed ? Loc.T("loot.roll_expires", seconds) : Loc.T("loot.need_locked");
            GUI.Label(new Rect(rect.x + 84, rect.y + 62, rect.width - 100, 22), hint, _small);
            if (_rolls.Count > 1)
                GUI.Label(new Rect(rect.xMax - 210, rect.y + 12, 196, 22), Loc.T("loot.more_rolls", _rolls.Count - 1), _small.WithAlignment(TextAnchor.UpperRight));

            var buttons = new Rect(rect.x + 16, rect.yMax - 48, (rect.width - 48) / 3, 36);
            GUI.enabled = roll.CanNeed;
            if (GUI.Button(buttons, Loc.T("loot.need"), _button)) AnswerRoll(LootChoice.Need);
            GUI.enabled = true;
            buttons.x += buttons.width + 8;
            if (GUI.Button(buttons, Loc.T("loot.greed"), _button)) AnswerRoll(LootChoice.Greed);
            buttons.x += buttons.width + 8;
            if (GUI.Button(buttons, Loc.T("loot.pass"), _button)) AnswerRoll(LootChoice.Pass);
        }

        /// <summary>Responde ao sorteio que está na tela (também usado pelo teste automático).</summary>
        public void AnswerRoll(LootChoice choice)
        {
            if (_rolls.Count == 0) return;
            _session.Send(new LootRollChoose { RollId = _rolls[0].Roll.RollId, Choice = choice });
            _rolls.RemoveAt(0);
        }

        /// <summary>Responde ao convite de amizade (também usado pelo teste automático).</summary>
        public void AnswerFriend(bool accept)
        {
            var invite = PendingFriendInvite;
            if (invite == null) return;
            Relation(accept ? RelationActionKind.AcceptFriend : RelationActionKind.DeclineFriend, invite.InviterName);
            _friendInvite = null;
            _friendInviteUntil = 0;
        }

        // ----- Envio -----

        private void SubmitLine(string text)
        {
            var input = ChatParser.Parse(text, _channel, _lastWhisperFrom);
            switch (input.Kind)
            {
                case ChatInputKind.Message:
                    _session.Send(new ChatSend { Channel = input.Channel, Target = input.Target ?? "", Text = input.Text });
                    if (input.Channel != ChatChannel.Whisper) _channel = input.Channel;
                    break;
                case ChatInputKind.PartyInvite:
                    _session.Send(new PartyCommand { Action = PartyActionKind.Invite, TargetName = input.Target });
                    break;
                case ChatInputKind.PartyLeave:
                    _session.Send(new PartyCommand { Action = PartyActionKind.Leave });
                    break;
                case ChatInputKind.PartyKick:
                    _session.Send(new PartyCommand { Action = PartyActionKind.Kick, TargetName = input.Target });
                    break;
                case ChatInputKind.PartyPromote:
                    _session.Send(new PartyCommand { Action = PartyActionKind.Promote, TargetName = input.Target });
                    break;
                case ChatInputKind.FriendAdd:
                    Relation(RelationActionKind.AddFriend, input.Target);
                    break;
                case ChatInputKind.FriendRemove:
                    Relation(RelationActionKind.RemoveFriend, input.Target);
                    break;
                case ChatInputKind.Block:
                    Relation(RelationActionKind.Block, input.Target);
                    break;
                case ChatInputKind.Unblock:
                    Relation(RelationActionKind.Unblock, input.Target);
                    break;
                case ChatInputKind.Report:
                    Relation(RelationActionKind.Report, input.Target, input.Text);
                    break;
                case ChatInputKind.DuelRequest:
                    _session.Send(new DuelCommand { Action = DuelActionKind.Request, TargetName = input.Target });
                    break;
                case ChatInputKind.ArenaQueue:
                    var inQueue = CharacterState.Pvp != null && CharacterState.Pvp.InQueue;
                    _session.Send(new ArenaCommand { Action = inQueue ? ArenaActionKind.LeaveQueue : ArenaActionKind.Join });
                    break;
                case ChatInputKind.TradeRequest:
                    _session.Send(new TradeCommand { Action = TradeActionKind.Request, TargetName = input.Target });
                    break;
                case ChatInputKind.FriendsWindow:
                    _friendsOpen = !_friendsOpen;
                    break;
                case ChatInputKind.Help:
                    AddLine(ChatChannel.System, ChatParser.HelpText);
                    break;
                case ChatInputKind.BossTime:
                {
                    // O horário é fixo (horas pares de Brasília): dá para calcular aqui mesmo.
                    var now = System.DateTimeOffset.UtcNow;
                    var next = Game.Shared.World.WorldBossRules.NextSlot(now);
                    var minutes = System.Math.Max(1, (int)System.Math.Ceiling((next - now).TotalMinutes));
                    AddLine(ChatChannel.System, Loc.T("chat.boss_time", Loc.T("monster.guardiao_das_ruinas"),
                        next.ToOffset(System.TimeSpan.FromHours(-3)).ToString("HH'h'mm"), minutes));
                    break;
                }
                case ChatInputKind.Gm:
                    _session.Send(new GmCommand { Text = input.Text });
                    break;
                case ChatInputKind.Invalid:
                    AddLine(ChatChannel.System, input.Error);
                    break;
            }
        }

        // ----- Utilidades -----

        private static string Prefix(ChatChannel channel) => channel switch
        {
            ChatChannel.Party => Loc.T("chat.prefix_party") + " ",
            ChatChannel.System => "",
            ChatChannel.Whisper => "",
            _ => "",
        };

        private static Color ChannelColor(ChatChannel channel) => channel switch
        {
            ChatChannel.Party => new Color(0.45f, 0.85f, 1f),
            ChatChannel.Whisper => new Color(0.95f, 0.55f, 1f),
            ChatChannel.System => Accent,
            _ => new Color(0.95f, 0.95f, 0.95f),
        };

        /// <summary>O texto de outro jogador não pode virar formatação (rich text) na tela.</summary>
        private static string Escape(string text) => string.IsNullOrEmpty(text) ? "" : text.Replace("<", "‹").Replace(">", "›");

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
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = false };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(_label) { fontSize = 16, clipping = TextClipping.Clip };
            _wrapped = new GUIStyle(_small) { wordWrap = true };
            _input = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true };
        }
    }
}
