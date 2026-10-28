using System.Collections.Generic;
using Game.Client.Combat;
using Game.Client.Fx;
using Game.Client.Monsters;
using Game.Client.Net;
using Game.Client.Player;
using Game.Client.UI;
using Game.Shared.Combat;
using Game.Shared.Loot;
using Game.Shared.Text;
using UnityEngine;
using Msg = Game.Shared.Net;
using UnityEngine.InputSystem;

namespace Game.Client.Loot
{
    /// <summary>
    /// Recompensas do jogador no modo offline: ao matar um monstro, ganha XP, Alz e sorteia o drop
    /// (no jogo online isso será decidido pelo servidor). Itens caem no chão e são coletados com a tecla (E): o mais
    /// perto, um por aperto. Passar por cima não pega mais (pegava sem querer, inclusive o que não interessava).
    /// </summary>
    [RequireComponent(typeof(PlayerProgression))]
    public sealed class PlayerLoot : MonoBehaviour
    {
        [SerializeField] private float keyPickupRadius = 3.5f;
        [SerializeField] private Material lootMaterial;

        /// <summary>Uma pilha de itens iguais no inventário provisório.</summary>
        public sealed class Stack
        {
            public string ItemId;
            public string Name;
            public ItemRarity Rarity;
            public int Quantity;
        }

        private readonly List<Stack> _inventory = new List<Stack>();
        private readonly IRandomSource _random = new SystemRandomSource();
        private PlayerProgression _progression;

        public long Alz { get; private set; }

        /// <summary>Itens no chão vindos do servidor, por id (modo online).</summary>
        private readonly Dictionary<int, LootPickup> _netPickups = new Dictionary<int, LootPickup>();

        /// <summary>Quando foi pedido por último cada item (para não repetir o pedido a cada quadro).</summary>
        private readonly Dictionary<int, float> _requestedAt = new Dictionary<int, float>();
        public IReadOnlyList<Stack> Inventory => _inventory;
        public int MonstersKilled { get; private set; }

        public void SetLootMaterial(Material material) => lootMaterial = material;

        private void Awake() => _progression = GetComponent<PlayerProgression>();

        private void OnEnable() => MonsterController.AnyKilled += OnMonsterKilled;
        private void OnDisable() => MonsterController.AnyKilled -= OnMonsterKilled;

        private void Update()
        {
            var player = PlayerCombat.Local;
            if (player != null && player.IsDead) return;

            if (UI.KeyBindings.Pressed(UI.GameAction.Pickup)) PickupNearest();
        }

        /// <summary>O item no chão mais perto, ao alcance da tecla de pegar (nulo se não houver).</summary>
        public LootPickup NearestPickup
        {
            get
            {
                LootPickup nearest = null;
                var best = keyPickupRadius;
                foreach (var pickup in LootPickup.All)
                {
                    var offset = pickup.transform.position - transform.position;
                    offset.y = 0;
                    if (offset.magnitude > best) continue;
                    best = offset.magnitude;
                    nearest = pickup;
                }
                return nearest;
            }
        }

        /// <summary>Pega o item mais perto (a tecla, e o teste automático).</summary>
        public void PickupNearest()
        {
            var pickup = NearestPickup;
            if (pickup == null) return;
            if (pickup.NetworkId != 0) RequestPickup(pickup);
            else Collect(pickup);
        }

        private void OnMonsterKilled(MonsterController monster)
        {
            MonstersKilled++;
            _progression.GainMonsterXp(monster.Data.XpReward, monster.Data.Level);

            var loot = monster.Data.Drops.Roll(_random);
            if (loot.Alz > 0)
            {
                Alz += loot.Alz;
                DamageNumbers.Spawn(monster.transform.position + Vector3.up * 1.6f, Loc.T("fx.alz_gain", loot.Alz), new Color(1f, 0.85f, 0.3f), 22f);
            }

            for (var i = 0; i < loot.Items.Count; i++)
            {
                var angle = i * 2.4f;
                var position = monster.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (0.6f + 0.25f * i);
                SpawnPickup(loot.Items[i], position);
            }
        }

        // ===== Modo online =====

        /// <summary>Alz decidido pelo servidor (mostra quanto ganhou).</summary>
        public void SetServerAlz(long alz)
        {
            var gained = alz - Alz;
            Alz = alz;
            if (gained > 0)
                DamageNumbers.Spawn(transform.position + Vector3.up * 2.9f, Loc.T("fx.alz_gain", gained), new Color(1f, 0.85f, 0.3f), 22f);
        }

        /// <summary>Inventário completo vindo do servidor (substitui o local: o servidor é a fonte da verdade).</summary>
        public void SetServerInventory(Msg.Inventory message)
        {
            _inventory.Clear();
            foreach (var item in message.Items)
            {
                _inventory.Add(new Stack { ItemId = item.ItemId, Name = item.Name, Rarity = (ItemRarity)item.Rarity, Quantity = item.Quantity });
            }
        }

        public void OnServerLootSpawned(Msg.LootSpawned message)
        {
            if (_netPickups.ContainsKey(message.LootId)) return;
            var drop = new ItemDrop(message.ItemId, message.Name, (ItemRarity)message.Rarity, message.Quantity);
            var pickup = SpawnPickup(drop, Game.Client.World.Ground.Point(message.X, message.Z));
            pickup.NetworkId = message.LootId;
            _netPickups.Add(message.LootId, pickup);
        }

        public void OnServerLootRemoved(Msg.LootRemoved message, int myPlayerId)
        {
            _requestedAt.Remove(message.LootId);
            if (!_netPickups.TryGetValue(message.LootId, out var pickup)) return;
            _netPickups.Remove(message.LootId);
            if (message.PickedById == myPlayerId)
            {
                AddToInventory(pickup.Drop);
                VisualAnimator.Trigger(this, "PickUp");
            }
            Destroy(pickup.gameObject);
        }

        private void RequestPickup(LootPickup pickup)
        {
            var session = NetworkSession.Instance;
            if (session == null) return;
            if (_requestedAt.TryGetValue(pickup.NetworkId, out var at) && Time.unscaledTime - at < 0.5f) return;
            _requestedAt[pickup.NetworkId] = Time.unscaledTime;
            session.Send(new Msg.PickupLoot { LootId = pickup.NetworkId });
        }

        /// <summary>Só para o teste automático: um item de enfeite no chão (não existe no servidor).</summary>
        public LootPickup SpawnPreview(ItemDrop drop, Vector3 position) => SpawnPickup(drop, position);

        private LootPickup SpawnPickup(ItemDrop drop, Vector3 position)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Drop: {drop.Name}";
            Destroy(cube.GetComponent<Collider>());
            cube.transform.position = Game.Client.World.Ground.Snap(position);
            cube.transform.localScale = Vector3.one * (drop.Rarity >= ItemRarity.Rare ? 0.4f : 0.3f);

            var renderer = cube.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (lootMaterial != null)
            {
                renderer.sharedMaterial = lootMaterial;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", LootPickup.ColorFor(drop.Rarity));
                renderer.SetPropertyBlock(block);
            }

            // Modelo do item (Visuals/item/<id>), se existir; o cubo colorido fica escondido.
            var model = Fx.VisualCatalog.Spawn(Fx.VisualCatalog.Item, drop.ItemId, cube.transform);
            if (model != null)
            {
                model.transform.localScale = Vector3.one / cube.transform.localScale.x;
                renderer.enabled = false;
            }

            Fx.ItemFx.AttachRarityGlow(cube.transform, drop.Rarity, LootPickup.ColorFor(drop.Rarity));
            Fx.ItemFx.RareDrop(cube.transform.position, drop.Rarity, LootPickup.ColorFor(drop.Rarity));

            var pickup = cube.AddComponent<LootPickup>();
            pickup.Initialize(drop);
            if (drop.Rarity >= ItemRarity.Rare)
                SoundPlayer.Play(SoundId.Excellent, 0.8f, 0f);
            return pickup;
        }

        private void Collect(LootPickup pickup)
        {
            AddToInventory(pickup.Drop);
            VisualAnimator.Trigger(this, "PickUp");
            Destroy(pickup.gameObject);
        }

        private void AddToInventory(ItemDrop drop)
        {
            var stack = _inventory.Find(s => s.ItemId == drop.ItemId);
            if (stack == null)
            {
                stack = new Stack { ItemId = drop.ItemId, Name = drop.Name, Rarity = drop.Rarity };
                _inventory.Add(stack);
            }
            stack.Quantity += drop.Quantity;

            DamageNumbers.Spawn(transform.position + Vector3.up * 2.4f, Loc.T("loot.picked", drop.Name, drop.Quantity), LootPickup.ColorFor(drop.Rarity), 20f);
            SoundPlayer.Play(SoundId.Good, 0.5f);
        }
    }
}
