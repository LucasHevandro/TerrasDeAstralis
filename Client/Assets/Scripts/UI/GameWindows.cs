using System.Collections.Generic;
using System.Linq;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Progression;
using Game.Shared.Text;
using Game.Shared.World;
using UnityEngine;
using Msg = Game.Shared.Net;
using UnityEngine.InputSystem;

namespace Game.Client.UI
{
    /// <summary>
    /// Janelas do jogo online (IMGUI, mesma tela virtual de 1920x1080 do HUD): inventário com arrastar e soltar,
    /// equipamento, tooltip com comparação, armazém, ferreira, conversa de missões, atributos e rastreador de missões.
    /// Só mostra o que o servidor mandou (<see cref="CharacterState"/>) e envia pedidos; o servidor decide tudo.
    /// </summary>
    public sealed partial class GameWindows : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;
        private const float SlotSize = 50f;
        private const float SlotGap = 4f;
        private const int Columns = 8;

        private static readonly Color Panel = new Color(0.03f, 0.03f, 0.05f, 0.82f);
        private static readonly Color SlotBack = new Color(0.16f, 0.16f, 0.19f, 0.95f);
        private static readonly Color SlotHover = new Color(0.3f, 0.3f, 0.36f, 0.95f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.35f);
        private static readonly Color Good = new Color(0.45f, 0.95f, 0.45f);
        private static readonly Color Bad = new Color(1f, 0.45f, 0.4f);

        private static readonly (EquipSlot Slot, string LabelKey)[] EquipSlots =
        {
            (EquipSlot.Weapon, "slot.weapon"), (EquipSlot.Armor, "slot.armor"), (EquipSlot.Gloves, "slot.gloves"),
            (EquipSlot.Boots, "slot.boots"), (EquipSlot.Accessory, "slot.accessory"),
        };

        /// <summary>Retângulos (tela virtual) das janelas abertas no último quadro: o clique ali não vai para o mundo.</summary>
        private static readonly List<Rect> OpenRects = new List<Rect>();

        private static float _scale = 1f;
        private static Vector2 _offset;

        private GUIStyle _label;
        private GUIStyle _small;
        private GUIStyle _tiny;
        private GUIStyle _button;

        private bool _inventoryOpen;
        private bool _attributesOpen;
        private bool _questLogOpen;
        private Vector2 _questScroll;

        // Arrastar
        private Msg.ItemContainerKind _dragFrom;
        private int _dragSlot = -1;
        private Vector2 _dragStart;
        private bool _dragging;

        private long _discardUid;
        private int _discardSlot = -1;

        // Item da bolsa esperando a confirmação para ser largado no chão.
        private long _groundUid;
        private int _groundSlot = -1;

        // Ferreira: item e pedra escolhidos (pelo id único, que sobrevive às atualizações do inventário)
        private long _smithItemUid;
        private long _smithStoneUid;

        // Atributos ainda não confirmados
        private int _addStr, _addDex, _addInt;

        // Tooltip do quadro atual
        private ItemInstance _hoverItem;
        private bool _hoverWorn;

        /// <summary>O ponteiro está sobre alguma janela (o clique não deve selecionar alvo nem girar a câmera).</summary>
        public static bool PointerOverUi
        {
            get
            {
                var mouse = Mouse.current;
                if (mouse == null) return false;
                if (OptionsWindow.IsOpen || ConnectMenu.IsOpen) return true;
                var screen = mouse.position.ReadValue();
                var point = new Vector2((screen.x - _offset.x) / _scale, (Screen.height - screen.y - _offset.y) / _scale);
                foreach (var rect in OpenRects) if (rect.Contains(point)) return true;
                return SocialWindows.Contains(point);
            }
        }

        private static PlayerCombat Player => PlayerCombat.Local;

        public static GameWindows Instance { get; private set; }

        /// <summary>Alguma janela do lado direito (inventário, loja, troca...) está aberta: o minimapa sai da frente.</summary>
        public static bool RightPanelOpen { get; private set; }

        // Usados pelo teste automático (DevTools/OnlineSmokeTest) para abrir janelas sem teclado.
        public void SetInventoryOpen(bool open) => _inventoryOpen = open;
        public void SetAttributesOpen(bool open) => _attributesOpen = open;
        public void SetQuestLogOpen(bool open) => _questLogOpen = open;
        public void SelectForSmith(long uid) => _smithItemUid = uid;
        public void TalkToNearestNpc() => TalkToNearest();

        private void Awake()
        {
            if (!GameMode.IsOnline)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            OpenRects.Clear();
            RightPanelOpen = false;
            HideCharacterPreview();
        }

        private void Update()
        {
            var keyboard = GameInput.Keyboard;
            if (keyboard == null || ConnectMenu.IsOpen) return;

            if (KeyBindings.Pressed(GameAction.Inventory)) Fx.UiSounds.Toggle(_inventoryOpen = !_inventoryOpen);
            if (KeyBindings.Pressed(GameAction.Character))
            {
                // P: abre o personagem; aberto (sozinho ou com o inventário), fecha os dois.
                var open = !(_characterOpen || _inventoryOpen);
                _characterOpen = open;
                if (!open) _inventoryOpen = false;
                Fx.UiSounds.Toggle(open);
            }
            if (KeyBindings.Pressed(GameAction.Attributes)) Fx.UiSounds.Toggle(_attributesOpen = !_attributesOpen);
            if (KeyBindings.Pressed(GameAction.QuestLog)) Fx.UiSounds.Toggle(_questLogOpen = !_questLogOpen);
            if (KeyBindings.Pressed(GameAction.Skills)) Fx.UiSounds.Toggle(_skillsOpen = !_skillsOpen);
            if (KeyBindings.Pressed(GameAction.Progress)) Fx.UiSounds.Toggle(_progressOpen = !_progressOpen);
            if (KeyBindings.Pressed(GameAction.Talk)) TalkToNearest();
            if (keyboard.escapeKey.wasPressedThisFrame && !OptionsWindow.EscapeTaken) CloseAll();

            // Afastou-se do NPC: a conversa fecha.
            var dialog = CharacterState.Dialog;
            if (dialog != null && Player != null)
            {
                var npc = NpcCatalog.Find(dialog.NpcId);
                if (npc == null || !NearNpc(npc, NpcCatalog.InteractRange + 1.5f)) CharacterState.CloseDialog();
            }
        }

        /// <summary>Alguma janela que o Esc fecha está aberta (aí o Esc fecha ela em vez de abrir as opções).</summary>
        public static bool AnyOpen => Instance != null && Instance.isActiveAndEnabled &&
            (Instance._inventoryOpen || Instance._attributesOpen || Instance._questLogOpen || Instance._skillsOpen || Instance._progressOpen || Instance._characterOpen ||
             Instance._discardSlot >= 0 || Instance._groundSlot >= 0 || CharacterState.Dialog != null);

        private void CloseAll()
        {
            _inventoryOpen = _attributesOpen = _questLogOpen = _skillsOpen = _progressOpen = _characterOpen = false;
            _discardSlot = -1;
            _groundSlot = -1;
            CharacterState.CloseDialog();
        }

        private void TalkToNearest()
        {
            var npc = NearestNpc();
            if (npc == null)
            {
                if (Player != null) Player.ShowNotice(Loc.T("npc.none_near"));
                return;
            }
            Send(new Msg.TalkToNpc { NpcId = npc.Id });
        }

        private static NpcDefinition NearestNpc()
        {
            if (Player == null) return null;
            NpcDefinition best = null;
            var bestDistance = float.MaxValue;
            foreach (var npc in NpcCatalog.All)
            {
                var distance = Vector2.Distance(new Vector2(npc.X, npc.Z), new Vector2(Player.transform.position.x, Player.transform.position.z));
                if (distance > NpcCatalog.InteractRange || distance >= bestDistance) continue;
                best = npc;
                bestDistance = distance;
            }
            return best;
        }

        private static bool NearNpc(NpcDefinition npc, float range)
        {
            var p = Player.transform.position;
            return (new Vector2(npc.X - p.x, npc.Z - p.z)).sqrMagnitude <= range * range;
        }

        private static void Send(Msg.INetMessage message)
        {
            if (NetworkSession.Instance != null) NetworkSession.Instance.Send(message);
        }

        // ===== Desenho =====

        // Só GUI (sem GUILayout): sem a passada de layout, o OnGUI roda menos vezes por quadro.
        private void OnEnable() => useGUILayout = false;

        private void OnGUI()
        {
            // Com as opções abertas, nada do jogo é desenhado por cima ou através da janela (o GUI.depth não basta).
            if (Player == null || ConnectMenu.IsOpen || OptionsWindow.IsOpen) return;
            GUI.depth = -10; // por cima do HUD (plaquinhas dos monstros e barras)
            EnsureStyles();
            Fx.UiSounds.ClickIn(OpenRects, _scale, _offset); // janelas do quadro anterior, antes de limpar a lista
            OpenRects.Clear();
            _hoverItem = null;

            DrawNpcLabels();

            _scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            _offset = new Vector2((Screen.width - VirtualWidth * _scale) / 2, (Screen.height - VirtualHeight * _scale) / 2);
            GUI.matrix = Matrix4x4.TRS(new Vector3(_offset.x, _offset.y, 0), Quaternion.identity, new Vector3(_scale, _scale, 1));

            var dialog = CharacterState.Dialog;
            var npc = dialog != null ? NpcCatalog.Find(dialog.NpcId) : null;
            var warehouseOpen = npc != null && npc.Role == NpcRole.Warehouse;
            var smithOpen = npc != null && npc.Role == NpcRole.Smith;
            var shopOpen = npc != null && npc.Role == NpcRole.Merchant && !Trading;
            var marketOpen = npc != null && npc.Role == NpcRole.Market && !Trading;
            var inventoryShown = _inventoryOpen || warehouseOpen || smithOpen || shopOpen || marketOpen || Trading;
            RightPanelOpen = inventoryShown;
            // O personagem abre junto com o inventário (ou sozinho, na P), à esquerda da janela de NPC que estiver aberta.
            var characterShown = inventoryShown || _characterOpen;
            var npcWindowX = warehouseOpen ? WarehouseOrigin.x - 14 : smithOpen ? SmithRect.x : marketOpen ? MarketRect.x
                : Trading ? TradeRect.x : shopOpen ? ShopRect.x : 0f;
            _characterX = LeftOfOpenWindows(warehouseOpen || smithOpen || marketOpen || Trading || shopOpen, npcWindowX);
            if (characterShown) DrawCharacter();
            else HideCharacterPreview();

            var pvp = CharacterState.Pvp;
            var pvpBusy = pvp != null && (pvp.InQueue || pvp.Mode == Msg.PvpMode.Arena);
            if (!inventoryShown && !_questLogOpen && Dungeon == null && !pvpBusy) DrawQuestTracker();
            if (inventoryShown) DrawInventory();
            if (warehouseOpen) DrawWarehouse();
            if (smithOpen) DrawSmith(npc);
            if (shopOpen) DrawShop(npc);
            if (marketOpen) DrawMarket(npc);
            if (Trading) DrawTrade();
            DrawTradeInvite();
            if (npc != null && npc.Role == NpcRole.QuestGiver) DrawQuestDialog(npc, dialog);
            if (npc != null && npc.Role == NpcRole.DungeonPortal) DrawPortal(npc);
            DrawDungeonHud();
            DrawPvp();
            if (_attributesOpen) DrawAttributes();
            if (_questLogOpen) DrawQuestLog();
            DrawSkills(npc);
            if (_progressOpen) DrawProgress();
            DrawTalkPrompt();
            if (_discardSlot >= 0) DrawDiscardConfirm();
            if (_groundSlot >= 0) DrawGroundConfirm();

            HandleDrop(inventoryShown, characterShown, warehouseOpen, smithOpen, shopOpen);
            if (_dragging) DrawDragged();
            else if (_hoverItem != null) DrawTooltip(_hoverItem, _hoverWorn);

            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
        }

        private Rect Window(Rect rect, string title)
        {
            OpenRects.Add(rect);
            UiTheme.Panel(rect);
            // Título na fonte serifada; a dica entre parênteses (o <size=14>) fica clara e discreta.
            var styled = title.Replace("<size=14>", "<size=14><color=#c9c1b2>").Replace("</size>", "</color></size>");
            GUI.Label(new Rect(rect.x + 14, rect.y + 6, rect.width - 28, 30), styled, UiArt.Title(22));
            UiTheme.Divider(new Rect(rect.x + 10, rect.y + 40, rect.width - 20, 2));
            return rect;
        }

        /// <summary>
        /// X das janelas da coluna da esquerda (mestre de skills, skills, atributos). Em grupo, o painel do grupo ocupa
        /// esse canto (x 20 a 340): a janela abre logo ao lado, sem ficar por baixo dele.
        /// </summary>
        private static float LeftColumnX =>
            SocialWindows.Instance != null && SocialWindows.Instance.Party != null && SocialWindows.Instance.Party.Members.Count > 0 ? 356f : 20f;

        // ----- Inventário e equipamento -----

        private Rect InventoryRect => new Rect(VirtualWidth - 480, 120, 460, 400);

        /// <summary>Primeira casa da bolsa no inventário.</summary>
        private Vector2 BagOrigin => new Vector2(InventoryRect.x + 14, InventoryRect.y + 50);

        private void DrawInventory()
        {
            var rect = Window(InventoryRect, Loc.T("inv.title", KeyBindings.Label(GameAction.Inventory)));

            // Bolsa (o equipamento vestido fica na janela do personagem, ao lado).
            DrawGrid(CharacterState.Bag, Msg.ItemContainerKind.Bag, BagOrigin);

            var footer = rect.yMax - 50;
            var alz = Player.GetComponent<PlayerLoot>();
            GUI.Label(new Rect(rect.x + 14, footer + 8, 260, 24), Loc.T("inv.alz", (alz != null ? alz.Alz : 0)), _small);

            // Lixeira: soltar um item aqui pede confirmação.
            var trash = TrashRect;
            Box(trash, new Color(0.35f, 0.08f, 0.08f, _dragging ? 0.9f : 0.5f));
            GUI.Label(trash, Loc.T("inv.discard"), _tiny.WithCenter());
        }

        private Rect TrashRect => new Rect(InventoryRect.xMax - 110, InventoryRect.yMax - 50, 96, 40);

        private void DrawGrid(ItemContainer container, Msg.ItemContainerKind kind, Vector2 origin)
        {
            for (var slot = 0; slot < container.Size; slot++)
            {
                var rect = SlotRect(origin, slot);
                DrawSlot(rect, container[slot], kind, slot, worn: false);
            }
        }

        private static Rect SlotRect(Vector2 origin, int slot) =>
            new Rect(origin.x + slot % Columns * (SlotSize + SlotGap), origin.y + slot / Columns * (SlotSize + SlotGap), SlotSize, SlotSize);

        private void DrawSlot(Rect rect, ItemInstance item, Msg.ItemContainerKind kind, int slot, bool worn)
        {
            var e = Event.current;
            var hover = rect.Contains(e.mousePosition);
            var dragged = _dragging && _dragFrom == kind && _dragSlot == slot;
            UiTheme.Slot(rect, hover);
            if (item != null && !dragged) DrawItemIcon(rect, item);
            else if (kind == Msg.ItemContainerKind.Equipment && Event.current.type == EventType.Repaint)
            {
                // Casa de equipamento vazia: a silhueta da peça que vai ali.
                var size = Mathf.Min(rect.width, rect.height) * 0.72f;
                GUI.color = new Color(0.85f, 0.78f, 0.62f, 0.22f);
                GUI.DrawTexture(new Rect(rect.center.x - size / 2, rect.center.y - size / 2, size, size), UiArt.Silhouette((EquipSlot)slot));
                GUI.color = Color.white;
            }
            if (item != null && item.Uid == _smithItemUid && _smithItemUid != 0) Outline(rect, Accent);

            if (!hover) return;
            if (item != null && !_dragging)
            {
                _hoverItem = item;
                _hoverWorn = worn;
            }

            if (e.type == EventType.MouseDown && item != null)
            {
                if (e.button == 0)
                {
                    _dragFrom = kind;
                    _dragSlot = slot;
                    _dragStart = e.mousePosition;
                    _dragging = false;
                    e.Use();
                }
                else if (e.button == 1)
                {
                    QuickAction(item, kind, slot);
                    e.Use();
                }
            }
        }

        private void DrawItemIcon(Rect rect, ItemInstance item)
        {
            var definition = ItemCatalog.Find(item.ItemId);
            if (definition == null) return;
            var color = LootPickup.ColorFor(definition.Rarity);
            var inner = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
            var icon = ItemIcons.Get(definition.Id);
            if (icon != null)
            {
                // Foto do modelo sobre um fundo na cor da raridade.
                Box(inner, new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 0.95f));
                GUI.DrawTexture(inner, icon, ScaleMode.ScaleToFit);
                Outline(inner, new Color(color.r, color.g, color.b, 0.85f));
            }
            else
            {
                Box(inner, new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 1f));
                Outline(inner, color);
                GUI.Label(new Rect(rect.x + 2, rect.y + 4, rect.width - 4, rect.height - 18), ShortName(definition), _tiny.WithCenter());
            }
            if (item.Upgrade > 0) GUI.Label(new Rect(rect.x + 4, rect.yMax - 20, rect.width, 18), $"<b>+{item.Upgrade}</b>", _tiny);
            if (item.Quantity > 1)
                GUI.Label(new Rect(rect.x, rect.yMax - 20, rect.width - 5, 18), item.Quantity.ToString(), _tiny.WithAlignment(TextAnchor.UpperRight));
            if (!string.IsNullOrEmpty(item.OptionId)) Box(new Rect(rect.xMax - 10, rect.y + 5, 5, 5), Accent);
        }

        private static string ShortName(ItemDefinition definition)
        {
            var word = definition.Name.Split(' ')[0];
            return word.Length > 7 ? word.Substring(0, 6) + "." : word;
        }

        /// <summary>Botão direito: veste, tira, usa poção; com armazém aberto, guarda/retira; na ferreira, escolhe.</summary>
        private void QuickAction(ItemInstance item, Msg.ItemContainerKind kind, int slot)
        {
            var definition = ItemCatalog.Get(item.ItemId);
            var dialog = CharacterState.Dialog;
            var npc = dialog != null ? NpcCatalog.Find(dialog.NpcId) : null;

            if (Trading && kind == Msg.ItemContainerKind.Bag)
            {
                AddToOffer(slot);
                return;
            }
            if (npc != null && npc.Role == NpcRole.Merchant && kind == Msg.ItemContainerKind.Bag)
            {
                _sellUid = item.Uid;
                return;
            }
            if (npc != null && npc.Role == NpcRole.Market && kind == Msg.ItemContainerKind.Bag)
            {
                // Preço sugerido: o dobro do que a mercadora paga (o jogador ajusta).
                SelectForMarket(item.Uid, System.Math.Max(1, Game.Shared.Economy.EconomyRules.SellPrice(definition, item.Upgrade) * 2));
                return;
            }
            if (npc != null && npc.Role == NpcRole.Smith && kind != Msg.ItemContainerKind.Warehouse)
            {
                if (definition.IsOptionStone) _smithStoneUid = item.Uid;
                else if (definition.Kind == ItemKind.Equipment) _smithItemUid = item.Uid;
                return;
            }
            if (npc != null && npc.Role == NpcRole.Warehouse && kind != Msg.ItemContainerKind.Equipment)
            {
                var to = kind == Msg.ItemContainerKind.Bag ? Msg.ItemContainerKind.Warehouse : Msg.ItemContainerKind.Bag;
                var target = to == Msg.ItemContainerKind.Bag ? CharacterState.Bag : CharacterState.Warehouse;
                var free = FirstFree(target);
                if (free >= 0) SendMove(kind, slot, to, free);
                return;
            }

            switch (kind)
            {
                case Msg.ItemContainerKind.Equipment:
                    var bagSlot = FirstFree(CharacterState.Bag);
                    if (bagSlot >= 0) SendMove(kind, slot, Msg.ItemContainerKind.Bag, bagSlot);
                    else Player.ShowNotice(Loc.T("bag.full_short"));
                    break;
                case Msg.ItemContainerKind.Bag when definition.Kind == ItemKind.Equipment:
                    SendMove(kind, slot, Msg.ItemContainerKind.Equipment, (int)definition.Slot);
                    break;
                case Msg.ItemContainerKind.Bag when definition.Kind == ItemKind.Consumable:
                    Send(new Msg.ItemCommand { Action = Msg.ItemActionKind.Use, From = kind, FromSlot = (ushort)slot });
                    break;
            }
        }

        private static int FirstFree(ItemContainer container)
        {
            for (var i = 0; i < container.Size; i++) if (container[i] == null) return i;
            return -1;
        }

        private static void SendMove(Msg.ItemContainerKind from, int fromSlot, Msg.ItemContainerKind to, int toSlot) =>
            Send(new Msg.ItemCommand { Action = Msg.ItemActionKind.Move, From = from, FromSlot = (ushort)fromSlot, To = to, ToSlot = (ushort)toSlot });

        /// <summary>Solta o item arrastado: em outra posição, no equipamento, no armazém, na ferreira ou na lixeira.</summary>
        private void HandleDrop(bool inventoryShown, bool characterShown, bool warehouseOpen, bool smithOpen, bool shopOpen)
        {
            var e = Event.current;
            if (_dragSlot < 0) return;
            if (e.type == EventType.MouseDrag && !_dragging && (e.mousePosition - _dragStart).sqrMagnitude > 36) _dragging = true;
            if (e.type != EventType.MouseUp || e.button != 0) return;

            var from = _dragFrom;
            var fromSlot = _dragSlot;
            var wasDragging = _dragging;
            _dragSlot = -1;
            _dragging = false;
            e.Use();
            if (!wasDragging) return;

            var point = e.mousePosition;
            var item = ItemAtSlot(from, fromSlot);
            if (item == null) return;

            if (characterShown && DropOnCharacter(point, from, fromSlot, item)) return;
            if (from == Msg.ItemContainerKind.Bag && DropItemOnHotbar(point, item)) return;
            if (inventoryShown)
            {
                for (var slot = 0; slot < CharacterState.Bag.Size; slot++)
                {
                    if (!SlotRect(BagOrigin, slot).Contains(point)) continue;
                    if (from != Msg.ItemContainerKind.Bag || slot != fromSlot) SendMove(from, fromSlot, Msg.ItemContainerKind.Bag, slot);
                    return;
                }
                if (TrashRect.Contains(point) && from == Msg.ItemContainerKind.Bag)
                {
                    _discardSlot = fromSlot;
                    _discardUid = item.Uid;
                    return;
                }
            }
            if (warehouseOpen && from != Msg.ItemContainerKind.Equipment)
            {
                for (var slot = 0; slot < CharacterState.Warehouse.Size; slot++)
                {
                    if (!SlotRect(WarehouseOrigin, slot).Contains(point)) continue;
                    if (from != Msg.ItemContainerKind.Warehouse || slot != fromSlot) SendMove(from, fromSlot, Msg.ItemContainerKind.Warehouse, slot);
                    return;
                }
            }
            if (Trading && TradeRect.Contains(point) && from == Msg.ItemContainerKind.Bag)
            {
                AddToOffer(fromSlot);
                return;
            }
            if (shopOpen && ShopRect.Contains(point) && from == Msg.ItemContainerKind.Bag)
            {
                _sellUid = item.Uid;
                return;
            }
            if (smithOpen && SmithRect.Contains(point) && from != Msg.ItemContainerKind.Warehouse)
            {
                var definition = ItemCatalog.Get(item.ItemId);
                if (definition.IsOptionStone) _smithStoneUid = item.Uid;
                else if (definition.Kind == ItemKind.Equipment) _smithItemUid = item.Uid;
                return;
            }
            // Solto fora de qualquer janela: largar no chão para outro pegar (pede confirmação; item de missão, não).
            if (from == Msg.ItemContainerKind.Bag && !OpenRects.Exists(r => r.Contains(point)) && !SocialWindows.Contains(point))
            {
                if (ItemCatalog.Get(item.ItemId).IsQuestItem)
                {
                    Player.ShowNotice(Loc.T("item.quest_no_drop"));
                    return;
                }
                _groundSlot = fromSlot;
                _groundUid = item.Uid;
            }
        }

        private void DrawGroundConfirm()
        {
            var item = CharacterState.Bag[_groundSlot];
            if (item == null || item.Uid != _groundUid)
            {
                _groundSlot = -1;
                return;
            }
            var definition = ItemCatalog.Get(item.ItemId);
            var rect = Window(new Rect(VirtualWidth / 2 - 220, VirtualHeight / 2 - 80, 440, 150), Loc.T("inv.drop_confirm"));
            var name = item.Upgrade > 0 ? Loc.T("item.with_upgrade", definition.Name, item.Upgrade) : definition.Name;
            if (item.Quantity > 1) name = $"{name} x{item.Quantity}";
            GUI.Label(new Rect(rect.x + 14, rect.y + 44, rect.width - 28, 50), Loc.T("inv.drop_hint", name), _small);
            if (GUI.Button(new Rect(rect.x + 60, rect.yMax - 50, 140, 36), Loc.T("inv.drop_yes"), _button))
            {
                Send(new Msg.ItemCommand { Action = Msg.ItemActionKind.Drop, From = Msg.ItemContainerKind.Bag, FromSlot = (ushort)_groundSlot });
                _groundSlot = -1;
            }
            if (GUI.Button(new Rect(rect.xMax - 200, rect.yMax - 50, 140, 36), Loc.T("inv.drop_no"), _button)) _groundSlot = -1;
        }

        private static ItemInstance ItemAtSlot(Msg.ItemContainerKind kind, int slot) => kind switch
        {
            Msg.ItemContainerKind.Bag => CharacterState.Bag[slot],
            Msg.ItemContainerKind.Warehouse => CharacterState.Warehouse[slot],
            _ => CharacterState.Equipment[(EquipSlot)slot],
        };

        private void DrawDragged()
        {
            var item = ItemAtSlot(_dragFrom, _dragSlot);
            if (item == null) return;
            var p = Event.current.mousePosition;
            DrawItemIcon(new Rect(p.x - SlotSize / 2, p.y - SlotSize / 2, SlotSize, SlotSize), item);
        }

        private void DrawDiscardConfirm()
        {
            var item = CharacterState.Bag[_discardSlot];
            if (item == null || item.Uid != _discardUid)
            {
                _discardSlot = -1;
                return;
            }
            var definition = ItemCatalog.Get(item.ItemId);
            var rect = Window(new Rect(VirtualWidth / 2 - 220, VirtualHeight / 2 - 80, 440, 150), Loc.T("inv.discard_confirm"));
            var name = item.Upgrade > 0 ? Loc.T("item.with_upgrade", definition.Name, item.Upgrade) : definition.Name;
            GUI.Label(new Rect(rect.x + 14, rect.y + 44, rect.width - 28, 50),
                item.Quantity > 1 ? Loc.T("inv.discard_text_many", name, item.Quantity) : Loc.T("inv.discard_text", name), _small);
            if (GUI.Button(new Rect(rect.x + 60, rect.yMax - 50, 140, 36), Loc.T("inv.discard"), _button))
            {
                Send(new Msg.ItemCommand { Action = Msg.ItemActionKind.Discard, From = Msg.ItemContainerKind.Bag, FromSlot = (ushort)_discardSlot });
                _discardSlot = -1;
            }
            if (GUI.Button(new Rect(rect.xMax - 200, rect.yMax - 50, 140, 36), Loc.T("ui.cancel"), _button)) _discardSlot = -1;
        }

        // ----- Tooltip -----

        private void DrawTooltip(ItemInstance item, bool worn)
        {
            var definition = ItemCatalog.Find(item.ItemId);
            if (definition == null) return;
            var lines = new List<string>();
            var color = ColorUtility.ToHtmlStringRGB(LootPickup.ColorFor(definition.Rarity));
            var title = item.Upgrade > 0 ? Loc.T("item.with_upgrade", definition.Name, item.Upgrade) : definition.Name;
            lines.Add($"<b><color=#{color}>{title}</color></b>");

            if (definition.Kind == ItemKind.Equipment)
            {
                var sheetLevel = Player.Level;
                lines.Add($"{ItemCatalog.GradeName(definition.Grade)} · {SlotName(definition.Slot)}");
                var levelColor = sheetLevel >= definition.RequiredLevel ? "ffffff" : "ff7766";
                var levelText = $"<color=#{levelColor}>{Loc.T("tip.level", definition.RequiredLevel)}</color>";
                if (definition.RequiredClass.HasValue)
                {
                    var classColor = definition.RequiredClass.Value == CharacterState.Class ? "ffffff" : "ff7766";
                    levelText += $" · <color=#{classColor}>{AccountRules.ClassDisplayName(definition.RequiredClass.Value)}</color>";
                }
                lines.Add(levelText);
                lines.Add(StatCalculator.ItemStats(item).Describe());
                if (!string.IsNullOrEmpty(item.OptionId))
                    lines.Add(Loc.T("tip.option", ItemCatalog.Find(item.OptionId)?.Name));

                // Comparação com o que está vestido na mesma posição.
                var current = CharacterState.Equipment[definition.Slot];
                if (!worn && current != null && current.Uid != item.Uid)
                {
                    var delta = StatCalculator.ItemStats(item) + StatCalculator.ItemStats(current) * -1;
                    lines.Add("");
                    lines.Add(Loc.T("tip.compared"));
                    lines.Add(delta.IsEmpty ? Loc.T("tip.equal") : ColoredDelta(delta));
                }
                else if (!worn && current == null)
                {
                    lines.Add("");
                    lines.Add(Loc.T("tip.empty_slot"));
                }
            }
            else if (definition.IsOptionStone)
            {
                lines.Add(Loc.T("tip.option_stone", definition.OptionStats.Describe()));
                lines.Add(Loc.T("tip.option_stone_hint"));
            }
            else if (!string.IsNullOrEmpty(definition.Description))
            {
                lines.Add(definition.Description);
            }
            if (item.Quantity > 1) lines.Add(Loc.T("tip.quantity", item.Quantity));

            var text = string.Join("\n", lines);
            var size = _small.CalcSize(new GUIContent(text.Replace("<b>", "").Replace("</b>", "")));
            var width = Mathf.Clamp(size.x + 30, 220, 460);
            var height = _small.CalcHeight(new GUIContent(text), width - 24) + 20;
            var p = Event.current.mousePosition;
            var rect = new Rect(p.x + 18, p.y + 18, width, height);
            if (rect.xMax > VirtualWidth) rect.x = p.x - width - 12;
            if (rect.yMax > VirtualHeight) rect.y = VirtualHeight - height - 4;
            Box(rect, new Color(0.02f, 0.02f, 0.03f, 0.95f));
            Outline(rect, LootPickup.ColorFor(definition.Rarity));
            GUI.Label(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 16), text, _small);
        }

        /// <summary>Clarão no quadro do item logo depois da melhoria: dourado no sucesso, cinza ou vermelho na falha.</summary>
        private static void DrawUpgradeFlash(Rect itemRect)
        {
            var result = CharacterState.LastUpgrade;
            var age = Time.unscaledTime - CharacterState.LastUpgradeAt;
            if (result == null || age > 1.2f || result.Outcome == (byte)UpgradeOutcome.Refused) return;
            var color = result.Outcome == (byte)UpgradeOutcome.Success ? new Color(1f, 0.85f, 0.3f)
                : result.Outcome == (byte)UpgradeOutcome.FailedDowngraded ? new Color(1f, 0.3f, 0.25f) : new Color(0.6f, 0.6f, 0.65f);
            var alpha = 1f - age / 1.2f;
            var grow = (1f - alpha) * 14f;
            var glow = new Rect(itemRect.x - grow, itemRect.y - grow, itemRect.width + grow * 2, itemRect.height + grow * 2);
            Box(itemRect, new Color(color.r, color.g, color.b, 0.35f * alpha));
            Outline(glow, new Color(color.r, color.g, color.b, alpha));
        }

        private static string ColoredDelta(StatBlock delta)
        {
            var parts = delta.Describe().Split(new[] { ", " }, System.StringSplitOptions.RemoveEmptyEntries);
            return string.Join(", ", parts.Select(p => p.StartsWith("-") ? $"<color=#ff7766>{p}</color>" : $"<color=#77ee77>{p}</color>"));
        }

        private static string SlotName(EquipSlot slot) => Loc.T(EquipSlots.First(s => s.Slot == slot).LabelKey);

        // ----- Armazém -----

        private Vector2 WarehouseOrigin => new Vector2(InventoryRect.x - 470 + 14, InventoryRect.y + 46);

        private void DrawWarehouse()
        {
            Window(new Rect(InventoryRect.x - 470, InventoryRect.y, 460, 340), Loc.T("storage.title"));
            DrawGrid(CharacterState.Warehouse, Msg.ItemContainerKind.Warehouse, WarehouseOrigin);
        }

        // ----- Ferreira -----

        private Rect SmithRect => new Rect(InventoryRect.x - 470, InventoryRect.y, 460, 470);

        private void DrawSmith(NpcDefinition npc)
        {
            var rect = Window(SmithRect, Loc.T("smith.title", npc.Name));
            var x = rect.x + 14;
            var y = rect.y + 44;
            GUI.Label(new Rect(x, y, rect.width - 28, 40), Loc.T("smith.drag_hint"), _tiny);

            var (item, container, slot) = FindByUid(_smithItemUid);
            var itemRect = new Rect(x, y + 30, SlotSize + 10, SlotSize + 10);
            Box(itemRect, SlotBack);
            DrawUpgradeFlash(itemRect);
            if (item == null)
            {
                _smithItemUid = 0;
                GUI.Label(new Rect(x + 74, y + 40, 300, 40), Loc.T("smith.no_item"), _small);
            }
            else
            {
                DrawItemIcon(itemRect, item);
                if (itemRect.Contains(Event.current.mousePosition))
                {
                    _hoverItem = item;
                    _hoverWorn = container == Msg.ItemContainerKind.Equipment;
                }
                DrawUpgradeInfo(item, container, slot, new Rect(x + 74, y + 28, rect.width - 102, 260));
            }

            // Pedra de opção
            var stoneY = rect.yMax - 120;
            Box(new Rect(x, stoneY - 8, rect.width - 28, 1), new Color(1, 1, 1, 0.2f));
            var (stone, _, stoneSlot) = FindByUid(_smithStoneUid);
            if (stone == null) _smithStoneUid = 0;
            GUI.Label(new Rect(x, stoneY, rect.width - 28, 24), Loc.T("smith.option_title"), _small);
            GUI.Label(new Rect(x, stoneY + 26, 300, 24),
                stone == null ? Loc.T("smith.pick_stone_hint") : Loc.T("smith.stone_info", ItemCatalog.Get(stone.ItemId).Name, ItemCatalog.Get(stone.ItemId).OptionStats.Describe()), _tiny);
            GUI.enabled = stone != null && item != null && string.IsNullOrEmpty(item.OptionId);
            if (GUI.Button(new Rect(rect.xMax - 164, stoneY + 52, 150, 36), Loc.T("smith.apply_stone"), _button) && item != null)
            {
                Send(new Msg.ApplyOption { Container = container, Slot = (ushort)slot, StoneSlot = (ushort)stoneSlot });
                _smithStoneUid = 0;
            }
            GUI.enabled = true;
        }

        private void DrawUpgradeInfo(ItemInstance item, Msg.ItemContainerKind container, int slot, Rect area)
        {
            var definition = ItemCatalog.Get(item.ItemId);
            var y = area.y;
            GUI.Label(new Rect(area.x, y, area.width, 26), Loc.T("item.with_upgrade_bold", definition.Name, item.Upgrade), _label);
            y += 30;
            if (item.Upgrade >= UpgradeRules.MaxLevel)
            {
                GUI.Label(new Rect(area.x, y, area.width, 24), Loc.T("smith.max"), _small);
                return;
            }

            var guaranteed = UpgradeRules.IsGuaranteed(item);
            var chance = guaranteed ? 1 : UpgradeRules.SuccessChance(item.Upgrade);
            GUI.Label(new Rect(area.x, y, area.width, 24),
                $"+{item.Upgrade} → +{item.Upgrade + 1}: <b>{chance * 100:0}%</b>" + (guaranteed ? " <color=#77ee77>" + Loc.T("smith.guaranteed") + "</color>" : ""), _small);
            y += 24;
            GUI.Label(new Rect(area.x, y, area.width, 24),
                item.Upgrade >= UpgradeRules.DowngradeFrom ? Loc.T("smith.fail_downgrade") : Loc.T("smith.fail_keep"), _tiny);
            y += 22;

            // Barra de proteção contra azar
            var limit = UpgradeRules.PityLimit(item.Upgrade);
            if (limit > 0)
            {
                var progress = UpgradeRules.PityProgress(item);
                var bar = new Rect(area.x, y + 4, area.width, 12);
                Box(bar, SlotBack);
                Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(progress / (float)limit), bar.height), Accent);
                GUI.Label(new Rect(area.x, y + 16, area.width, 20), Loc.T("smith.pity", progress, limit), _tiny);
                y += 40;
            }

            // Custo
            var cost = UpgradeRules.CostFor(definition, item.Upgrade);
            var have = CharacterState.Bag.Count(cost.CoreId);
            var alz = Player.GetComponent<PlayerLoot>()?.Alz ?? 0;
            GUI.Label(new Rect(area.x, y, area.width, 22),
                $"<color=#{(have >= cost.Cores ? "ffffff" : "ff7766")}>{Loc.T("smith.cores", cost.Cores, ItemCatalog.Get(cost.CoreId).Name, have)}</color>", _tiny);
            y += 20;
            GUI.Label(new Rect(area.x, y, area.width, 22),
                $"<color=#{(alz >= cost.Alz ? "ffffff" : "ff7766")}>{cost.Alz:N0} Alz</color>", _tiny);
            y += 28;

            var next = item.Clone();
            next.Upgrade++;
            GUI.Label(new Rect(area.x, y, area.width, 40), Loc.T("smith.next_level", StatCalculator.ItemStats(next).Describe()), _tiny);
            y += 40;

            GUI.enabled = have >= cost.Cores && alz >= cost.Alz;
            if (GUI.Button(new Rect(area.x, y, 160, 38), Loc.T("smith.upgrade"), _button))
                Send(new Msg.UpgradeItem { Container = container, Slot = (ushort)slot });
            GUI.enabled = true;

            var result = CharacterState.LastUpgrade;
            var age = Time.unscaledTime - CharacterState.LastUpgradeAt;
            if (result != null && age < 4f && (result.Uid == item.Uid || result.Uid == 0))
            {
                var resultColor = result.Outcome == (byte)UpgradeOutcome.Success ? Good : result.Outcome == (byte)UpgradeOutcome.Refused ? Color.white : Bad;
                GUI.color = new Color(resultColor.r, resultColor.g, resultColor.b, 1f - Mathf.Clamp01((age - 3f)));
                GUI.Label(new Rect(area.x + 170, y - 4, area.width - 170, 48), $"<b>{result.Message}</b>", _small);
                GUI.color = Color.white;
            }
        }

        private static (ItemInstance Item, Msg.ItemContainerKind Container, int Slot) FindByUid(long uid)
        {
            if (uid == 0) return (null, Msg.ItemContainerKind.Bag, -1);
            var bagSlot = CharacterState.Bag.SlotOf(uid);
            if (bagSlot >= 0) return (CharacterState.Bag[bagSlot], Msg.ItemContainerKind.Bag, bagSlot);
            foreach (var worn in CharacterState.Equipment.Worn)
                if (worn.Value.Uid == uid) return (worn.Value, Msg.ItemContainerKind.Equipment, (int)worn.Key);
            return (null, Msg.ItemContainerKind.Bag, -1);
        }

        // ----- Missões -----

        private void DrawQuestDialog(NpcDefinition npc, Msg.NpcDialog dialog)
        {
            // A altura acompanha o conteúdo (medida no quadro anterior).
            var rect = Window(new Rect(480, 160, 560, Mathf.Clamp(_dialogHeight, 160, 760)), npc.Name);
            var y = rect.y + 46;
            var x = rect.x + 14;
            var width = rect.width - 28;

            if (dialog.Offered.Count == 0 && dialog.ReadyToTurnIn.Count == 0)
            {
                var next = QuestCatalog.All.Where(q => q.GiverNpcId == npc.Id && !q.Daily && !CharacterState.QuestCompleted(q.Id)
                    && CharacterState.CountsOf(q.Id) == null).OrderBy(q => q.MinLevel).FirstOrDefault();
                // "Termine o que pedi" só se a missão aberta é deste NPC.
                var active = CharacterState.Quests.Active.Any(q => QuestCatalog.Find(q.QuestId)?.GiverNpcId == npc.Id);
                var text = active ? Loc.T("dialog.finish_first")
                    : next != null ? Loc.T("dialog.come_back_level", next.MinLevel)
                    : Loc.T("dialog.all_done");
                GUI.Label(new Rect(x, y, width, 60), text, _small);
                y += 50;
            }

            foreach (var questId in dialog.ReadyToTurnIn)
            {
                var quest = QuestCatalog.Find(questId);
                if (quest == null) continue;
                y = QuestBlock(quest, x, y, width, ready: true);
                if (GUI.Button(new Rect(x, y, 180, 34), Loc.T("dialog.turn_in"), _button))
                    Send(new Msg.QuestCommand { Action = Msg.QuestActionKind.TurnIn, QuestId = questId });
                y += 48;
            }
            foreach (var questId in dialog.Offered)
            {
                var quest = QuestCatalog.Find(questId);
                if (quest == null) continue;
                y = QuestBlock(quest, x, y, width, ready: false);
                if (GUI.Button(new Rect(x, y, 180, 34), Loc.T("dialog.accept"), _button))
                    Send(new Msg.QuestCommand { Action = Msg.QuestActionKind.Accept, QuestId = questId });
                y += 48;
            }

            if (GUI.Button(new Rect(rect.xMax - 134, rect.yMax - 48, 120, 34), Loc.T("ui.close"), _button)) CharacterState.CloseDialog();
            if (Event.current.type == EventType.Repaint) _dialogHeight = y - rect.y + 60;
        }

        private float _dialogHeight = 300;

        private float QuestBlock(QuestDefinition quest, float x, float y, float width, bool ready)
        {
            GUI.Label(new Rect(x, y, width, 26), Loc.T("quest.header", quest.Name, quest.MinLevel), _label);
            y += 28;
            var description = _small.CalcHeight(new GUIContent(quest.Description), width);
            GUI.Label(new Rect(x, y, width, description), quest.Description, _small);
            y += description + 4;
            var counts = CharacterState.CountsOf(quest.Id);
            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var done = counts != null && i < counts.Count ? counts[i] : 0;
                GUI.Label(new Rect(x + 10, y, width - 10, 22), "· " + ObjectiveText(quest.Objectives[i], done, counts != null), _tiny);
                y += 20;
            }
            GUI.Label(new Rect(x, y, width, 22), Loc.T("quest.reward", RewardText(quest)), _tiny);
            y += 22;
            if (!string.IsNullOrEmpty(quest.Unlocks))
            {
                GUI.Label(new Rect(x, y, width, 22), $"<color=#77ee77>{quest.Unlocks}</color>", _tiny);
                y += 22;
            }
            return y + 4;
        }

        private string ObjectiveText(QuestObjective objective, int done, bool showProgress)
        {
            var progress = showProgress ? $" {Mathf.Min(done, objective.Count)}/{objective.Count}" : (objective.Count > 1 ? $" x{objective.Count}" : "");
            var text = objective.Kind switch
            {
                QuestObjectiveKind.Kill => Loc.T("objective.kill", Loc.T("monster." + objective.TargetId)),
                QuestObjectiveKind.Collect => Loc.T("objective.collect", Loc.T("item." + objective.TargetId)),
                _ => Loc.T("objective.talk", Loc.T("npc." + objective.TargetId)),
            };
            var complete = showProgress && done >= objective.Count;
            return complete ? $"<color=#77ee77>{text}{progress} {Loc.T("objective.ok")}</color>" : text + progress;
        }

        private static string RewardText(QuestDefinition quest)
        {
            var parts = new List<string>();
            if (quest.RewardXp > 0) parts.Add(Loc.T("quest.reward_xp", quest.RewardXp));
            if (quest.RewardAlz > 0) parts.Add(Loc.T("quest.reward_alz", quest.RewardAlz));
            parts.AddRange(quest.RewardItems.Select(i => Loc.T("quest.reward_item", Loc.T("item." + i.ItemId), i.Quantity)));
            return string.Join(", ", parts);
        }

        /// <summary>Missões aceitas no canto da tela, com o andamento.</summary>
        private void DrawQuestTracker()
        {
            var active = CharacterState.Quests.Active;
            if (active.Count == 0) return;
            var x = VirtualWidth - 470f;
            var y = Minimap.PanelsTop;
            var height = 40f + active.Sum(q => 48f + 20f * (QuestCatalog.Find(q.QuestId)?.Objectives.Count ?? 0));
            Box(new Rect(x, y, 450, height), new Color(0, 0, 0, 0.45f));
            GUI.Label(new Rect(x + 12, y + 6, 420, 24), Loc.T("tracker.title", KeyBindings.Label(GameAction.QuestLog)), _small);
            y += 34;
            foreach (var entry in active)
            {
                var quest = QuestCatalog.Find(entry.QuestId);
                if (quest == null) continue;
                GUI.Label(new Rect(x + 12, y, 420, 24), $"<color=#ffd25a>{quest.Name}</color>", _small);
                y += 24;
                for (var i = 0; i < quest.Objectives.Count; i++)
                {
                    var done = i < entry.Counts.Count ? entry.Counts[i] : 0;
                    GUI.Label(new Rect(x + 22, y, 410, 20), ObjectiveText(quest.Objectives[i], done, true), _tiny);
                    y += 20;
                }
                GUI.Label(new Rect(x + 22, y, 410, 20), $"<color=#9fd0ff>{DestinationText(quest, entry.Counts)}</color>", _tiny);
                y += 24;
            }
        }

        /// <summary>"→ Batedora Nara, Mata Sombria (80 m)": para onde ir no próximo passo da missão.</summary>
        private static string DestinationText(QuestDefinition quest, IReadOnlyList<int> counts)
        {
            if (!(QuestGuide.For(quest, counts) is { } destination) || Player == null) return "";
            var name = destination.NpcId != "" ? NpcCatalog.Find(destination.NpcId)?.Name : Loc.T(Loc.Key("monster.", destination.MonsterId));
            var position = new Vector3(destination.X, 0, destination.Z);
            var here = Player.transform.position;
            var distance = Vector2.Distance(new Vector2(here.x, here.z), new Vector2(destination.X, destination.Z));
            var zone = Game.Client.World.ZoneArea.At(position);
            return zone != null
                ? Loc.T("tracker.go_to_zone", name, zone.DisplayName, distance)
                : Loc.T("tracker.go_to", name, distance);
        }

        private void DrawQuestLog()
        {
            var rect = Window(new Rect(480, 160, 560, 640), Loc.T("questlog.title", KeyBindings.Label(GameAction.QuestLog)));
            var x = rect.x + 14;
            var width = rect.width - 44;
            var active = CharacterState.Quests.Active;
            var contentHeight = 60f + active.Count * 200f;
            _questScroll = GUI.BeginScrollView(new Rect(x, rect.y + 44, rect.width - 20, rect.height - 100), _questScroll,
                new Rect(0, 0, width, contentHeight));
            var y = 0f;
            if (active.Count == 0)
            {
                var next = QuestCatalog.All.FirstOrDefault(q => !q.Daily && !CharacterState.QuestCompleted(q.Id));
                GUI.Label(new Rect(0, y, width, 60),
                    next == null ? Loc.T("questlog.all_done") : Loc.T("questlog.none_active", Loc.T("npc." + next.GiverNpcId), KeyBindings.Label(GameAction.Talk)), _small);
            }
            foreach (var entry in active)
            {
                var quest = QuestCatalog.Find(entry.QuestId);
                if (quest == null) continue;
                y = QuestBlock(quest, 0, y, width, ready: false);
                if (GUI.Button(new Rect(0, y, 140, 30), Loc.T("questlog.abandon"), _button))
                    Send(new Msg.QuestCommand { Action = Msg.QuestActionKind.Abandon, QuestId = quest.Id });
                y += 44;
            }
            GUI.EndScrollView();
            GUI.Label(new Rect(x, rect.yMax - 44, width, 30),
                Loc.T("questlog.completed", CharacterState.Quests.Completed.Count(id => QuestCatalog.Find(id) is { Daily: false }),
                    QuestCatalog.All.Count(q => !q.Daily)), _small);
        }

        // ----- Atributos -----

        private void DrawAttributes()
        {
            var sheet = CharacterState.Sheet;
            var rect = Window(new Rect(LeftColumnX, 340, 420, 400), Loc.T("attr.title", KeyBindings.Label(GameAction.Attributes)));
            if (sheet == null) return;
            var x = rect.x + 14;
            var y = rect.y + 46;
            var pending = _addStr + _addDex + _addInt;
            var free = sheet.FreePoints - pending;

            GUI.Label(new Rect(x, y, 390, 24), Loc.T("attr.class_level", AccountRules.ClassDisplayName((CharacterClass)sheet.Class), Player.Level), _small);
            y += 28;
            GUI.Label(new Rect(x, y, 390, 24), Loc.T("attr.free", free), _small);
            y += 34;
            y = AttributeRow(x, y, Loc.T("attr.str"), sheet.Strength, ref _addStr, free, Loc.T("attr.str_hint"));
            y = AttributeRow(x, y, Loc.T("attr.dex"), sheet.Dexterity, ref _addDex, free, Loc.T("attr.dex_hint"));
            y = AttributeRow(x, y, Loc.T("attr.int"), sheet.Intelligence, ref _addInt, free, Loc.T("attr.int_hint"));

            GUI.enabled = pending > 0;
            if (GUI.Button(new Rect(x, y + 6, 160, 34), Loc.T("attr.confirm"), _button))
            {
                Send(new Msg.AllocateAttributes { Strength = (ushort)_addStr, Dexterity = (ushort)_addDex, Intelligence = (ushort)_addInt });
                _addStr = _addDex = _addInt = 0;
            }
            if (GUI.Button(new Rect(x + 172, y + 6, 120, 34), Loc.T("attr.clear"), _button)) _addStr = _addDex = _addInt = 0;
            GUI.enabled = true;
            y += 54;

            GUI.Label(new Rect(x, y, 390, 90),
                Loc.T("attr.sheet_1", sheet.AttackPower, sheet.MagicPower) +
                Loc.T("attr.sheet_2", sheet.Defense, sheet.CritPercent) +
                Loc.T("attr.sheet_3", Player.Hp.Max, Player.Mp.Max), _small);
        }

        private float AttributeRow(float x, float y, string name, int value, ref int add, int free, string hint)
        {
            GUI.Label(new Rect(x, y, 140, 26), name, _small);
            GUI.Label(new Rect(x + 130, y, 90, 26), add > 0 ? $"<b>{value + add}</b> <color=#77ee77>(+{add})</color>" : $"<b>{value}</b>", _small);
            GUI.enabled = free > 0;
            if (GUI.Button(new Rect(x + 250, y, 36, 28), "+", _button)) add++;
            GUI.enabled = add > 0;
            if (GUI.Button(new Rect(x + 292, y, 36, 28), "−", _button)) add--;
            GUI.enabled = true;
            GUI.Label(new Rect(x + 10, y + 26, 380, 20), hint, _tiny);
            return y + 52;
        }

        // ----- NPCs no mundo -----

        private void DrawTalkPrompt()
        {
            if (CharacterState.Dialog != null) return;
            var npc = NearestNpc();
            if (npc == null) return;
            var hasQuest = npc.Role == NpcRole.QuestGiver;
            GUI.Label(new Rect(0, VirtualHeight - 340, VirtualWidth, 30),
                hasQuest || npc.Role != NpcRole.QuestGiver
                    ? Loc.T("npc.talk_prompt_role", npc.Name, Loc.T(RoleHintKey(npc.Role)), KeyBindings.Label(GameAction.Talk))
                    : Loc.T("npc.talk_prompt", npc.Name, KeyBindings.Label(GameAction.Talk)),
                _label.WithCenter());
        }

        /// <summary>O ponto (tela virtual) está sob um painel do HUD ou sob uma janela aberta no quadro anterior.</summary>
        private static bool CoveredByPanel(Vector2 point)
        {
            foreach (var rect in CombatHud.PanelRects) if (rect.Contains(point)) return true;
            foreach (var rect in OpenRects) if (rect.Contains(point)) return true;
            return false;
        }

        /// <summary>O que o NPC faz, no aviso "falar com" (ex.: melhoria).</summary>
        private static string RoleHintKey(NpcRole role) => role switch
        {
            NpcRole.Smith => "npc.hint_smith", NpcRole.Warehouse => "npc.hint_warehouse", NpcRole.Merchant => "npc.hint_merchant",
            NpcRole.DungeonPortal => "npc.hint_dungeon", NpcRole.Trainer => "npc.hint_trainer", NpcRole.Market => "npc.hint_market",
            _ => "npc.hint_quests",
        };

        /// <summary>Plaquinha do NPC no mundo (ex.: Ferreira).</summary>
        private static string RoleLabelKey(NpcRole role) => role switch
        {
            NpcRole.Smith => "npc.role_smith", NpcRole.Warehouse => "npc.role_warehouse", NpcRole.Merchant => "npc.role_merchant",
            NpcRole.DungeonPortal => "npc.role_dungeon", NpcRole.Trainer => "npc.role_trainer", NpcRole.Market => "npc.role_market",
            _ => "npc.role_quests",
        };

        /// <summary>Nome em cima de cada NPC (coordenadas de tela, antes da escala).</summary>
        private void DrawNpcLabels()
        {
            var cam = Camera.main;
            var session = NetworkSession.Instance;
            if (cam == null || session == null) return;
            foreach (var npc in NpcCatalog.All)
            {
                if (!session.NpcViews.TryGetValue(npc.Id, out var view) || view == null) continue;
                if ((view.position - Player.transform.position).sqrMagnitude > 40f * 40f) continue;
                var screen = cam.WorldToScreenPoint(view.position + Vector3.up * 2.6f);
                if (screen.z <= 0) continue;
                // Não escreve por cima das janelas do chat e de amigos (coordenadas virtuais do quadro anterior).
                var point = new Vector2((screen.x - _offset.x) / _scale, (Screen.height - screen.y - _offset.y) / _scale);
                if (SocialWindows.Contains(point) || CoveredByPanel(point)) continue;
                var role = Loc.T(RoleLabelKey(npc.Role));
                var rect = new Rect(screen.x - 150, Screen.height - screen.y - 20, 300, 40);
                GUI.color = new Color(0, 0, 0, 0.8f);
                GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), $"<b>{npc.Name}</b>\n<size=13>{role}</size>", _small.WithCenter());
                GUI.color = Accent;
                GUI.Label(rect, $"<b>{npc.Name}</b>\n<size=13>{role}</size>", _small.WithCenter());
                if (QuestMarker(npc) is { } marker)
                {
                    // Flutua de leve em cima do nome, com sombra.
                    var bob = Mathf.Sin(Time.unscaledTime * 3f + npc.X) * 4f;
                    var markRect = new Rect(screen.x - 30, rect.y - 50 + bob, 60, 50);
                    GUI.color = new Color(0, 0, 0, 0.75f);
                    GUI.Label(new Rect(markRect.x + 2, markRect.y + 2, markRect.width, markRect.height), $"<size=40><b>{marker.Text}</b></size>", _small.WithCenter());
                    GUI.color = marker.Color;
                    GUI.Label(markRect, $"<size=40><b>{marker.Text}</b></size>", _small.WithCenter());
                }
                GUI.color = Color.white;
            }
        }

        private static readonly Color MarkerGold = new Color(1f, 0.85f, 0.2f);
        private static readonly Color MarkerGray = new Color(0.75f, 0.75f, 0.78f);
        private static readonly Color MarkerDaily = new Color(0.45f, 0.78f, 1f);

        /// <summary>
        /// Marcador em cima de quem dá missões: "?" amarelo = tem missão dele pronta para entregar; "!" amarelo = tem missão
        /// nova para o nível do jogador; "?" cinza = missão dele em andamento. Nulo = nada a mostrar.
        /// </summary>
        internal static (string Text, Color Color)? QuestMarker(NpcDefinition npc)
        {
            if (npc.Role != NpcRole.QuestGiver || Player == null) return null;
            var active = false;
            foreach (var progress in CharacterState.Quests.Active)
            {
                var quest = QuestCatalog.Find(progress.QuestId);
                if (quest == null || quest.GiverNpcId != npc.Id) continue;
                active = true;
                var ready = true;
                for (var i = 0; i < quest.Objectives.Count; i++)
                    if (i >= progress.Counts.Count || progress.Counts[i] < quest.Objectives[i].Count) ready = false;
                if (ready) return ("?", MarkerGold);
            }
            var level = Player.Level;
            var daily = false;
            foreach (var quest in QuestCatalog.All)
            {
                if (quest.GiverNpcId != npc.Id || quest.MinLevel > level || CharacterState.QuestCompleted(quest.Id) || CharacterState.CountsOf(quest.Id) != null)
                    continue;
                if (!string.IsNullOrEmpty(quest.PreviousQuestId) && !CharacterState.QuestCompleted(quest.PreviousQuestId)) continue;
                if (!quest.Daily) return ("!", MarkerGold);
                daily = true;
            }
            // Só diária disponível: "!" azul (a história, em dourado, tem prioridade).
            if (daily) return ("!", MarkerDaily);
            return active ? ("?", MarkerGray) : ((string, Color)?)null;
        }

        // ----- Utilidades -----

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

        private void EnsureStyles()
        {
            if (_label != null) return;
            UiTheme.Ensure();
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = true };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(_label) { fontSize = 16 };
            _tiny = new GUIStyle(_label) { fontSize = 13 };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 16, richText = true };
        }
    }
}
