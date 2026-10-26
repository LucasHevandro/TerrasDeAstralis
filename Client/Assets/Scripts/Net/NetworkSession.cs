using System;
using System.Collections.Generic;
using Game.Client.Combat;
using Game.Client.Fx;
using Game.Client.Monsters;
using Game.Client.Player;
using Game.Client.UI;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;
using Game.Shared.World;
using Ping = Game.Shared.Net.Ping;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Modo online: conecta ao servidor, sincroniza o relógio, envia a posição do jogador e transforma o que chega
    /// em objetos na cena (monstros e outros jogadores). As mensagens de combate são repassadas por
    /// <see cref="MessageReceived"/> para quem cuida delas.
    /// </summary>
    public sealed class NetworkSession : MonoBehaviour
    {
        private const float MoveSendInterval = 0.05f;
        private const float PingInterval = 1f;

        [Header("Materiais (o construtor da cena preenche)")]
        [SerializeField] private Material litTemplate;
        [SerializeField] private Material telegraphMaterial;
        [SerializeField] private Material ringMaterial;

        private readonly Dictionary<int, NetEntityView> _views = new Dictionary<int, NetEntityView>();
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private readonly Dictionary<string, MonsterData> _monsterData = new Dictionary<string, MonsterData>();
        private NetClient _client;
        private PlayerMotor _motor;
        private PlayerCombat _combat;
        private bool _helloSent;
        private uint _moveSequence;
        private float _nextMoveAt;
        private float _nextPingAt;
        private int _lastDodgeCount;

        public static NetworkSession Instance { get; private set; }

        public ServerClock Clock { get; } = new ServerClock();
        public int PlayerId { get; private set; }
        public bool IsWelcomed => PlayerId != 0;
        public NetClient.Status State => _client?.State ?? NetClient.Status.Idle;
        public string Error { get; private set; } = "";
        public IReadOnlyDictionary<int, NetEntityView> Views => _views;

        /// <summary>Horário estimado do servidor agora.</summary>
        public double ServerNow => Clock.ServerNow(LocalNow);

        public static double LocalNow => Time.realtimeSinceStartupAsDouble;

        /// <summary>Mensagens que esta classe não trata sozinha (combate, status, drops).</summary>
        public event Action<INetMessage> MessageReceived;

        public void SetMaterials(Material lit, Material telegraph, Material ring)
        {
            litTemplate = lit;
            telegraphMaterial = telegraph;
            ringMaterial = ring;
        }

        public NetEntityView FindView(int id) => _views.TryGetValue(id, out var view) ? view : null;

        public void Send(INetMessage message) => _client?.Send(message);

        /// <summary>Material colorido com o mesmo modelo dos monstros (cache por nome), para o mapa da dungeon.</summary>
        public Material Material(string key, Color color) => MaterialFor(key, color);

        private void Awake()
        {
            if (!GameMode.IsOnline)
            {
                enabled = false;
                return;
            }
            Instance = this;
            gameObject.AddComponent<ClientMetrics>();
            CharacterState.Reset();
            // Online o jogo não pode pausar ao perder o foco: pararia de ler o servidor e "congelaria" o mundo.
            Application.runInBackground = true;
            foreach (var spawn in WorldContent.Spawns()) _monsterData[spawn.Data.Id] = spawn.Data;
            foreach (var dungeon in Game.Shared.Dungeon.DungeonCatalog.All())
            {
                foreach (var room in dungeon.Rooms)
                foreach (var spawn in room.Spawns)
                    _monsterData.TryAdd(spawn.Data.Id, spawn.Data);
                var servant = dungeon.Boss.Servant();
                _monsterData.TryAdd(servant.Id, servant);
            }
        }

        private void Start()
        {
            _combat = PlayerCombat.Local != null ? PlayerCombat.Local : FindAnyObjectByType<PlayerCombat>();
            if (_combat != null) _motor = _combat.GetComponent<PlayerMotor>();
            _client = new NetClient();
            _client.Connect(GameMode.Host, GameMode.Port);
            CreateNpcs();
        }

        /// <summary>NPCs da vila: fixos, iguais para todos (o servidor confere a distância ao falar).</summary>
        private void CreateNpcs()
        {
            foreach (var npc in NpcCatalog.All)
            {
                var root = new GameObject($"NPC: {npc.Name}");
                root.transform.position = Game.Client.World.Ground.Point(npc.X, npc.Z);
                var color = npc.Role switch
                {
                    NpcRole.Smith => new Color(0.85f, 0.45f, 0.2f),
                    NpcRole.Warehouse => new Color(0.55f, 0.45f, 0.3f),
                    NpcRole.Merchant => new Color(0.35f, 0.7f, 0.45f),
                    NpcRole.DungeonPortal => new Color(0.45f, 0.35f, 0.8f),
                    NpcRole.Trainer => new Color(0.3f, 0.55f, 0.9f),
                    NpcRole.Market => new Color(0.9f, 0.75f, 0.3f),
                    _ => new Color(0.85f, 0.8f, 0.5f),
                };
                var parts = MonsterVisuals.Build(root.transform, MonsterShape.Capsule, new Vector3(0.9f, 1.1f, 0.9f),
                    MaterialFor("npc_" + npc.Id, color), telegraphMaterial, ringMaterial, VisualCatalog.Npc, npc.Id);
                if (parts.Ring != null) parts.Ring.SetActive(false);
                // Vira para a praça (ponto de nascimento).
                root.transform.rotation = Quaternion.LookRotation(new Vector3(-npc.X, 0, -npc.Z).normalized + Vector3.forward * 0.001f);
                root.AddComponent<Game.Client.World.NpcLife>().NpcId = npc.Id;
                NpcViews[npc.Id] = root.transform;
            }
        }

        /// <summary>Onde está cada NPC na cena (para o nome em cima e o clique).</summary>
        public Dictionary<string, Transform> NpcViews { get; } = new Dictionary<string, Transform>();

        private void OnDestroy()
        {
            _client?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_client == null) return;

            if (_client.State == NetClient.Status.Connected && !_helloSent)
            {
                _helloSent = true;
                _client.Send(new Hello { Name = GameMode.PlayerName, Token = GameMode.Token, CharacterId = GameMode.CharacterId });
            }

            while (_client.TryReceive(out var message)) Dispatch(message);

            if (_client.State == NetClient.Status.Disconnected && string.IsNullOrEmpty(Error))
                Error = string.IsNullOrEmpty(_client.Error) ? Loc.T("net.disconnected") : _client.Error;

            if (!IsWelcomed || _client.State != NetClient.Status.Connected) return;

            if (Time.unscaledTime >= _nextPingAt)
            {
                _nextPingAt = Time.unscaledTime + PingInterval;
                _client.Send(new Ping { ClientTime = LocalNow });
            }
            SendMovement();
        }

        private void SendMovement()
        {
            if (_motor == null) return;
            var dodged = _motor.DodgeCount != _lastDodgeCount;
            if (!dodged && Time.unscaledTime < _nextMoveAt) return;
            _lastDodgeCount = _motor.DodgeCount;
            _nextMoveAt = Time.unscaledTime + MoveSendInterval;

            var position = _motor.transform.position;
            _client.Send(new MoveInput
            {
                Sequence = ++_moveSequence, X = position.x, Z = position.z, Yaw = _motor.transform.eulerAngles.y, Dodging = dodged,
            });
        }

        private void Dispatch(INetMessage message)
        {
            switch (message)
            {
                case Welcome welcome:
                    PlayerId = welcome.PlayerId;
                    Clock.Seed(welcome.ServerTime, LocalNow);
                    if (_motor != null) _motor.Teleport(Game.Client.World.Ground.Point(welcome.SpawnX, welcome.SpawnZ));
                    _client.Send(new Ping { ClientTime = LocalNow });
                    break;
                case Reject reject:
                    Error = reject.Reason.ToString();
                    _client.Disconnect();
                    break;
                case Pong pong:
                    Clock.AddSample(pong.ClientTime, pong.ServerTime, LocalNow);
                    break;
                case EntitySpawned spawned:
                    if (spawned.Id != PlayerId) CreateView(spawned);
                    else if (_combat != null) _combat.ApplyLook(spawned.WeaponId, spawned.WeaponUpgrade, GearOf(spawned), StyleOf(spawned));
                    break;
                case EntityLook look:
                    // Trocou de arma ou de armadura, ou melhorou: o modelo, o brilho e o metal acompanham (eu ou quem eu vejo).
                    var gear = new Fx.CharacterPaint.Gear(look.ArmorGrade, look.GlovesGrade, look.BootsGrade);
                    if (look.Id == PlayerId && _combat != null) _combat.ApplyLook(look.WeaponId, look.WeaponUpgrade, gear, hideHelmet: look.HideHelmet);
                    else if (_views.TryGetValue(look.Id, out var looked))
                    {
                        Fx.WeaponFx.SetLook(looked, look.WeaponId, look.WeaponUpgrade);
                        looked.SetGear(gear, look.HideHelmet);
                    }
                    break;
                case EntityDespawned despawned:
                    if (_views.TryGetValue(despawned.Id, out var view))
                    {
                        _views.Remove(despawned.Id);
                        Destroy(view.gameObject);
                    }
                    break;
                case Snapshot snapshot:
                    foreach (var state in snapshot.Entities)
                    {
                        if (_views.TryGetValue(state.Id, out var entity)) entity.AddSample(snapshot.ServerTime, state);
                    }
                    MessageReceived?.Invoke(snapshot);
                    break;
                case PositionCorrection correction:
                    if (_motor != null) _motor.Correct(Game.Client.World.Ground.Point(correction.X, correction.Z));
                    break;
                case Notice notice:
                    if (notice.Announcement) CombatHud.Announce(notice.Text.ToString());
                    else if (_combat != null) _combat.ShowNotice(notice.Text.ToString());
                    break;
                default:
                    MessageReceived?.Invoke(message);
                    break;
            }
        }

        private void CreateView(EntitySpawned spawned)
        {
            if (_views.ContainsKey(spawned.Id)) return;

            var root = new GameObject(spawned.Kind == EntityKind.Player ? $"Jogador: {spawned.Name}" : Loc.T("ui.name_level", spawned.Name, spawned.Level));
            root.transform.position = Game.Client.World.Ground.Point(spawned.X, spawned.Z);

            MonsterParts parts;
            MonsterData data = null;
            if (spawned.Kind == EntityKind.Monster)
            {
                var look = MonsterVisuals.LookFor(spawned.TypeId);
                parts = MonsterVisuals.Build(root.transform, look.Shape, look.Scale, MaterialFor(spawned.TypeId, look.Color),
                    telegraphMaterial, ringMaterial, VisualCatalog.Monster, spawned.TypeId, spawned.Elite);
                _monsterData.TryGetValue(spawned.TypeId, out data);
            }
            else
            {
                parts = MonsterVisuals.Build(root.transform, MonsterShape.Capsule, new Vector3(0.8f, 1f, 0.8f),
                    MaterialFor("outro_jogador", new Color(0.3f, 0.75f, 0.45f)), telegraphMaterial, ringMaterial,
                    VisualCatalog.Class, spawned.TypeId); // o servidor manda a classe do jogador no TypeId
            }

            // Colisor só para o clique de seleção (gatilho: não empurra o jogador).
            var collider = root.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.height = parts.Height;
            collider.radius = parts.Radius;
            collider.center = new Vector3(0, parts.Height / 2f, 0);

            var target = root.AddComponent<Targetable>();
            target.Configure(spawned.Kind == EntityKind.Monster ? "monster." + spawned.TypeId : spawned.Name, Mathf.Max(1, spawned.MaxHp), parts.Height + 0.3f, parts.Radius, parts.Ring,
                data?.Defense ?? 0);
            target.Level = spawned.Level;
            target.Elite = spawned.Elite;
            target.NetworkId = spawned.Id;
            target.IsSelectable = spawned.Kind == EntityKind.Monster;

            var view = root.AddComponent<NetEntityView>();
            view.Initialize(spawned, parts.Body, parts.Ring, parts.Telegraph, target, data);
            root.AddComponent<Fx.FootDust>().Scale = Mathf.Clamp(parts.Radius / 0.5f, 0.8f, 2.5f);
            if (spawned.Kind == EntityKind.Player)
            {
                Fx.WeaponFx.SetLook(root.transform, spawned.WeaponId, spawned.WeaponUpgrade);
                Fx.CharacterPaint.Apply(root.transform, GearOf(spawned), StyleOf(spawned));
            }
            _views.Add(spawned.Id, view);
        }

        private static Fx.CharacterPaint.Gear GearOf(EntitySpawned spawned) =>
            new Fx.CharacterPaint.Gear(spawned.ArmorGrade, spawned.GlovesGrade, spawned.BootsGrade);

        private static Fx.CharacterPaint.Style StyleOf(EntitySpawned spawned) =>
            new Fx.CharacterPaint.Style(spawned.Hair, spawned.Cloth, spawned.Head, spawned.Skin, spawned.HideHelmet);

        private Material MaterialFor(string key, Color color)
        {
            if (_materials.TryGetValue(key, out var material)) return material;
            material = litTemplate != null ? MonsterVisuals.Tinted(litTemplate, color) : null;
            _materials[key] = material;
            return material;
        }
    }
}
