using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Itens, ferreira, atributos, missões, skills liberadas e Despertar, decididos pelo servidor.</summary>
public class ProgressionWorldTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();

        public List<string> Notices(int playerId) => To<Notice>(playerId).Select(n => n.Text.ToString()).ToList();
    }

    private sealed class FixedRandom : IRandomSource
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public double NextDouble() => _value;
    }

    private long _uid = 1000;
    private long NewUid() => ++_uid;

    private static MonsterData Dummy(string id = "teste", int hp = 100) => new()
    {
        Id = id, Level = 1, MaxHp = hp, AttackDamage = 10, AttackPower = 0, Defense = 0,
        AttackRange = 1.5f, AttackCooldownSeconds = 2f, AttackWindupSeconds = 0.5f, MoveSpeed = 3f, Aggressive = false,
        AggroRadius = 8, LeashRadius = 30, WanderRadius = 0, XpReward = 10, RespawnSeconds = 5,
        Drops = new DropTable(0, 0, new DropEntry("pelo_macio", ItemRarity.Common, 1.0)),
    };

    /// <summary>Mundo com um monstro em (0, 2) e um personagem de verdade (não convidado), na origem.</summary>
    private (GameWorld World, FakeOutbox Outbox, ServerPlayer Player) Setup(
        MonsterData? monster = null, double random = 0.5, CharacterRecord? record = null)
    {
        var outbox = new FakeOutbox();
        var spawns = new[] { new SpawnDefinition { Data = monster ?? Dummy(), CenterX = 0, CenterZ = 2, Count = 1 } };
        var world = new GameWorld(outbox, spawns, new FixedRandom(random), new ItemUidAllocator(5000), eliteChance: 0);
        record ??= AuthService.InitialCharacter(CharacterClass.Blade, NewUid) with { Id = 7, AccountId = 1, Name = "Ana" };
        var player = world.AddPlayer("", 0, null, record);
        player.Movement.Teleport(0, 0, 0);
        return (world, outbox, player);
    }

    private static void GoTo(ServerPlayer player, string npcId)
    {
        var npc = NpcCatalog.Find(npcId)!;
        player.Movement.Teleport(npc.X, npc.Z - 1, 0);
    }

    private static ItemCommand Move(ItemContainerKind from, int fromSlot, ItemContainerKind to, int toSlot) =>
        new() { Action = ItemActionKind.Move, From = from, FromSlot = (ushort)fromSlot, To = to, ToSlot = (ushort)toSlot };

    [Fact]
    public void Personagem_novo_comeca_com_a_arma_da_classe_e_pocoes()
    {
        var (_, _, blade) = Setup();
        Assert.Equal("espada_ferro", blade.Equipment[EquipSlot.Weapon]!.ItemId);
        Assert.Equal(5, blade.Bag.Count("pocao_hp_p"));

        var arcanist = AuthService.InitialCharacter(CharacterClass.Arcanist, NewUid) with { Id = 8, Name = "Bia" };
        var (_, _, mage) = Setup(record: arcanist);
        Assert.Equal("cajado_ferro", mage.Equipment[EquipSlot.Weapon]!.ItemId);
        Assert.True(mage.Stats.MagicPower > mage.Stats.AttackPower);
        Assert.Equal("faisca", mage.Skills[0].Id);
    }

    [Fact]
    public void Skill_de_nivel_alto_e_combo_ficam_trancados()
    {
        var (world, outbox, player) = Setup(Dummy(hp: 5000));
        var monster = world.Monsters.Single();

        world.Handle(player.Id, new UseSkill { Slot = 2, TargetId = monster.Id }, now: 1); // Golpe Rúnico: nível 5
        world.Handle(player.Id, new StartCombo { TargetId = monster.Id }, now: 2);

        Assert.Empty(outbox.To<CombatEvent>(player.Id));
        Assert.Contains(outbox.Notices(player.Id), n => n.Contains("ainda não foi aprendida"));
        Assert.Contains(outbox.Notices(player.Id), n => n.Contains("liberar o combo"));
        Assert.Equal(PlayerRules.StartingSp, player.Sp.Current); // não gastou SP

        world.Tick(2);
        var sheet = outbox.To<CharacterSheet>(player.Id).Last();
        Assert.False(sheet.ComboUnlocked);
        Assert.Equal(new[] { true, false, false }, sheet.SkillUnlocked.Take(3)); // Giro de Lâmina é do nível 3
    }

    [Fact]
    public void Npc_da_regiao_oferece_as_missoes_dele_em_paralelo_com_a_historia()
    {
        var (world, outbox, player) = Setup();
        player.Progress = new CharacterProgress(5, 0);
        GoTo(player, NpcCatalog.Hunter);
        world.Handle(player.Id, new TalkToNpc { NpcId = NpcCatalog.Hunter }, 1);

        var dialog = outbox.To<NpcDialog>(player.Id).Last();
        // Nível 5, sem nada da história feito: as do caçador que não pedem outra antes e, por último, a diária.
        Assert.Equal(new[] { "coelhos_na_horta", "uivos_na_noite", "diaria_coelhos" }, dialog.Offered);

        world.Handle(player.Id, new QuestCommand { Action = QuestActionKind.Accept, QuestId = "uivos_na_noite" }, 2);
        Assert.True(player.Quests.IsActive("uivos_na_noite"));
    }

    [Fact]
    public void Vestir_e_tirar_muda_os_atributos()
    {
        var (world, outbox, player) = Setup();
        player.Bag.TryAdd("luvas_ferro", 1, NewUid);
        var slot = player.Bag.SlotOf(_uid);
        var defense = player.Stats.Defense;

        world.Handle(player.Id, Move(ItemContainerKind.Bag, slot, ItemContainerKind.Equipment, (int)EquipSlot.Gloves), 1);
        Assert.Equal("luvas_ferro", player.Equipment[EquipSlot.Gloves]!.ItemId);
        Assert.True(player.Stats.Defense > defense);

        world.Handle(player.Id, Move(ItemContainerKind.Equipment, (int)EquipSlot.Gloves, ItemContainerKind.Bag, 20), 2);
        Assert.Null(player.Equipment[EquipSlot.Gloves]);
        Assert.Equal("luvas_ferro", player.Bag[20]!.ItemId);
        Assert.Equal(defense, player.Stats.Defense);

        world.Tick(2);
        var inventory = outbox.To<Inventory>(player.Id).Last();
        Assert.Contains(inventory.Items, i => i.Container == ItemContainerKind.Bag && i.Slot == 20 && i.ItemId == "luvas_ferro");
        Assert.Contains(inventory.Items, i => i.Container == ItemContainerKind.Equipment && i.ItemId == "espada_ferro");
    }

    [Fact]
    public void Nivel_e_classe_sao_conferidos_ao_vestir()
    {
        var (world, outbox, player) = Setup();
        player.Bag.TryAdd("armadura_aco_negro", 1, NewUid);
        player.Bag.TryAdd("cajado_ferro", 1, NewUid);

        world.Handle(player.Id, Move(ItemContainerKind.Bag, player.Bag.SlotOf(_uid - 1), ItemContainerKind.Equipment, (int)EquipSlot.Armor), 1);
        world.Handle(player.Id, Move(ItemContainerKind.Bag, player.Bag.SlotOf(_uid), ItemContainerKind.Equipment, (int)EquipSlot.Weapon), 1);

        Assert.Null(player.Equipment[EquipSlot.Armor]);
        Assert.Equal("espada_ferro", player.Equipment[EquipSlot.Weapon]!.ItemId);
        Assert.Contains("Precisa do nível 10.", outbox.Notices(player.Id));
        Assert.Contains(outbox.Notices(player.Id), n => n.StartsWith("Só para"));
    }

    [Fact]
    public void Armazem_so_perto_do_npc()
    {
        var (world, outbox, player) = Setup();
        var potions = player.Bag.SlotOf(player.Bag.Items().First(i => i.Item.ItemId == "pocao_hp_p").Item.Uid);

        world.Handle(player.Id, Move(ItemContainerKind.Bag, potions, ItemContainerKind.Warehouse, 0), 1);
        Assert.Contains("Vá até o armazém da vila.", outbox.Notices(player.Id));
        Assert.Equal(0, player.Warehouse.Count("pocao_hp_p"));

        GoTo(player, NpcCatalog.Warehouse);
        world.Handle(player.Id, Move(ItemContainerKind.Bag, potions, ItemContainerKind.Warehouse, 0), 2);
        Assert.Equal(5, player.Warehouse.Count("pocao_hp_p"));
        Assert.Equal(0, player.Bag.Count("pocao_hp_p"));

        // Guardado vai junto no salvamento.
        var (snapshot, _) = world.CaptureForSave(player);
        Assert.Contains(snapshot.Items, i => i.Container == ItemContainerKind.Warehouse && i.ItemId == "pocao_hp_p");
    }

    [Fact]
    public void Melhoria_na_ferreira_gasta_nucleo_e_alz_e_aumenta_o_ataque()
    {
        var (world, outbox, player) = Setup(random: 0.0); // 0 = sempre sucesso
        player.Bag.TryAdd(ItemCatalog.CoreLow, 5, NewUid);
        player.Alz = 10_000;
        var attack = player.Stats.AttackPower;
        var weapon = new UpgradeItem { Container = ItemContainerKind.Equipment, Slot = (ushort)EquipSlot.Weapon };

        world.Handle(player.Id, weapon, 1); // longe da ferreira
        Assert.Equal((byte)UpgradeOutcome.Refused, outbox.To<UpgradeResult>(player.Id).Last().Outcome);
        Assert.Equal(10_000, player.Alz);

        GoTo(player, NpcCatalog.Smith);
        world.Handle(player.Id, weapon, 2);

        var result = outbox.To<UpgradeResult>(player.Id).Last();
        Assert.Equal(((byte)UpgradeOutcome.Success, (byte)1), (result.Outcome, result.NewLevel));
        Assert.Equal(1, player.Equipment[EquipSlot.Weapon]!.Upgrade);
        Assert.Equal(4, player.Bag.Count(ItemCatalog.CoreLow));
        Assert.Equal(10_000 - UpgradeRules.CostFor(ItemCatalog.Get("espada_ferro"), 0).Alz, player.Alz);
        Assert.True(player.Stats.AttackPower > attack);
        Assert.Contains(world.CaptureForSave(player).Logs, l => l.Kind == "melhoria");
    }

    [Fact]
    public void Pedra_de_opcao_vai_para_o_item()
    {
        var (world, _, player) = Setup();
        player.Bag.TryAdd("pedra_furia", 1, NewUid);
        GoTo(player, NpcCatalog.Smith);
        var attack = player.Stats.AttackPower;

        world.Handle(player.Id, new ApplyOption
        {
            Container = ItemContainerKind.Equipment, Slot = (ushort)EquipSlot.Weapon, StoneSlot = (ushort)player.Bag.SlotOf(_uid),
        }, 1);

        Assert.Equal("pedra_furia", player.Equipment[EquipSlot.Weapon]!.OptionId);
        Assert.Equal(0, player.Bag.Count("pedra_furia"));
        Assert.Equal(attack + 15, player.Stats.AttackPower);
    }

    [Fact]
    public void Pontos_de_atributo_so_os_que_sobram()
    {
        var (world, outbox, player) = Setup();
        player.Progress = new CharacterProgress(3, 0);
        player.RecalculateStats();
        Assert.Equal(10, player.FreeAttributePoints);
        var attack = player.Stats.AttackPower;

        world.Handle(player.Id, new AllocateAttributes { Strength = 11 }, 1);
        Assert.Contains("Só há 10 pontos livres", outbox.Notices(player.Id));

        world.Handle(player.Id, new AllocateAttributes { Strength = 6, Intelligence = 4 }, 2);
        Assert.Equal(0, player.FreeAttributePoints);
        Assert.Equal(attack + 12, player.Stats.AttackPower); // 2 de ataque por ponto de força
        Assert.Equal(16, world.CaptureForSave(player).Snapshot.Attributes.Strength);
    }

    [Fact]
    public void Missao_de_falar_com_npc_completa_e_da_recompensa()
    {
        var (world, outbox, player) = Setup();

        GoTo(player, NpcCatalog.Elder);
        world.Handle(player.Id, new TalkToNpc { NpcId = NpcCatalog.Elder }, 1);
        Assert.Contains("primeiros_passos", outbox.To<NpcDialog>(player.Id).Last().Offered);
        world.Handle(player.Id, new QuestCommand { Action = QuestActionKind.Accept, QuestId = "primeiros_passos" }, 1);
        Assert.True(player.Quests.IsActive("primeiros_passos"));

        world.Handle(player.Id, new TalkToNpc { NpcId = NpcCatalog.Smith }, 2); // longe da ferreira: não conta
        Assert.Contains(outbox.Notices(player.Id), n => n.StartsWith("Chegue perto"));
        GoTo(player, NpcCatalog.Smith);
        world.Handle(player.Id, new TalkToNpc { NpcId = NpcCatalog.Smith }, 3);

        GoTo(player, NpcCatalog.Elder);
        world.Handle(player.Id, new TalkToNpc { NpcId = NpcCatalog.Elder }, 4);
        Assert.Contains("primeiros_passos", outbox.To<NpcDialog>(player.Id).Last().ReadyToTurnIn);
        world.Handle(player.Id, new QuestCommand { Action = QuestActionKind.TurnIn, QuestId = "primeiros_passos" }, 4);

        Assert.True(player.Quests.IsCompleted("primeiros_passos"));
        Assert.Equal(10, player.Bag.Count("pocao_hp_p"));
        Assert.Equal(500, player.Alz);
        Assert.Equal((1, 300), (player.Progress.Level, player.Progress.Xp)); // 300 dos 560 do nível 1
        world.Tick(4);
        Assert.Contains("primeiros_passos", outbox.To<QuestState>(player.Id).Last().Completed);
        var saved = world.CaptureForSave(player).Snapshot;
        Assert.Contains(saved.Quests, q => q.QuestId == "primeiros_passos" && q.Completed);
    }

    [Fact]
    public void Matar_conta_para_a_missao_e_concluir_libera_o_combo()
    {
        var record = AuthService.InitialCharacter(CharacterClass.Blade, NewUid) with
        {
            Id = 7, Name = "Ana", Level = 3,
            Quests = new[] { new QuestRecord("primeiros_passos", true, Array.Empty<int>()), new QuestRecord(ClassCatalog.ComboQuestId, false, new[] { 9 }) },
        };
        var (world, outbox, player) = Setup(Dummy("coelho_saltador"), record: record);
        var monster = world.Monsters.Single();

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 1);
        Assert.True(monster.IsDead);
        Assert.Equal(10, player.Quests.ProgressOf(ClassCatalog.ComboQuestId)!.Counts[0]);

        GoTo(player, NpcCatalog.Elder);
        world.Handle(player.Id, new QuestCommand { Action = QuestActionKind.TurnIn, QuestId = ClassCatalog.ComboQuestId }, 2);
        Assert.True(player.ComboUnlocked);
        world.Tick(2);
        Assert.True(outbox.To<CharacterSheet>(player.Id).Last().ComboUnlocked);
    }

    [Fact]
    public void Item_de_missao_so_cai_para_quem_tem_a_missao_e_so_ele_ve()
    {
        // Ana tem a missão dos pelos; Bia, do mesmo grupo de teste, não.
        var record = AuthService.InitialCharacter(CharacterClass.Blade, NewUid) with
        {
            Id = 7, AccountId = 1, Name = "Ana", Level = 5,
            Quests = new[] { new QuestRecord("pelos_para_o_inverno", false, Array.Empty<int>()) },
        };
        var (world, outbox, ana) = Setup(record: record);
        var bia = world.AddPlayer("", 0, null,
            AuthService.InitialCharacter(CharacterClass.Blade, NewUid) with { Id = 8, AccountId = 2, Name = "Bia" });
        bia.Movement.Teleport(0, 0, 0);

        world.Handle(ana.Id, new UseSkill { Slot = 0, TargetId = world.Monsters.Single().Id }, 1);
        world.Tick(1);

        // O material de venda cai como sempre; o de missão é só da Ana e só ela recebe a mensagem dele.
        var quest = Assert.Single(world.Loot, l => l.Drop.ItemId == "missao_pelo_macio");
        Assert.Equal(ana.Id, quest.OwnerId);
        Assert.Contains(world.Loot, l => l.Drop.ItemId == "pelo_macio");
        Assert.Contains(outbox.To<LootSpawned>(ana.Id), l => l.ItemId == "missao_pelo_macio");
        Assert.DoesNotContain(outbox.To<LootSpawned>(bia.Id), l => l.ItemId == "missao_pelo_macio");
        world.Handle(bia.Id, new PickupLoot { LootId = quest.Id }, 1.1);
        Assert.Equal(0, bia.Bag.Count("missao_pelo_macio"));
        world.Handle(ana.Id, new PickupLoot { LootId = quest.Id }, 1.2);
        Assert.True(ana.Bag.Count("missao_pelo_macio") > 0);

        // Não vende e não vai para o mercado; desistir da missão tira da bolsa.
        GoTo(ana, NpcCatalog.Merchant);
        var slot = ana.Bag.SlotOf(ana.Bag.Items().First(i => i.Item.ItemId == "missao_pelo_macio").Item.Uid);
        world.Handle(ana.Id, new ShopCommand { Action = ShopActionKind.Sell, Slot = (ushort)slot, Quantity = 1 }, 2);
        Assert.True(ana.Bag.Count("missao_pelo_macio") > 0);
        Assert.Contains(outbox.Notices(ana.Id), n => n.StartsWith("Item de missão"));
        world.Handle(ana.Id, new QuestCommand { Action = QuestActionKind.Abandon, QuestId = "pelos_para_o_inverno" }, 3);
        Assert.Equal(0, ana.Bag.Count("missao_pelo_macio"));
    }

    [Fact]
    public void Material_ja_juntado_para_a_missao_vira_item_de_missao_ao_entrar()
    {
        var bag = AuthService.InitialCharacter(CharacterClass.Blade, NewUid).Items.ToList();
        bag.Add(new ItemRecord(NewUid(), ItemContainerKind.Bag, 30, "pelo_macio", 11));
        var record = AuthService.InitialCharacter(CharacterClass.Blade, NewUid) with
        {
            Id = 7, AccountId = 1, Name = "Ana", Level = 5, Items = bag,
            Quests = new[] { new QuestRecord("pelos_para_o_inverno", false, Array.Empty<int>()) },
        };
        var (_, _, ana) = Setup(record: record);

        Assert.Equal(8, ana.Bag.Count("missao_pelo_macio")); // só o que a missão pede
        Assert.Equal(3, ana.Bag.Count("pelo_macio"));
    }

    [Fact]
    public void Bolsa_cheia_deixa_o_item_no_chao()
    {
        var (world, outbox, player) = Setup();
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = world.Monsters.Single().Id }, 1);
        world.Tick(1);
        var loot = world.Loot.Single();
        player.Bag.TryAdd("espada_ferro", player.Bag.FreeSlots, NewUid);

        world.Handle(player.Id, new PickupLoot { LootId = loot.Id }, 1.1);

        Assert.Contains("Bolsa cheia", outbox.Notices(player.Id));
        Assert.Single(world.Loot);
        Assert.Equal(0, player.Bag.Count("pelo_macio"));
    }

    [Fact]
    public void Pocao_cura_e_tem_intervalo()
    {
        var (world, outbox, player) = Setup();
        player.Hp.Add(-player.Hp.Max + 10);
        var slot = player.Bag.Items().First(i => i.Item.ItemId == "pocao_hp_p").Slot;
        var use = new ItemCommand { Action = ItemActionKind.Use, From = ItemContainerKind.Bag, FromSlot = (ushort)slot };

        world.Handle(player.Id, use, 1);
        world.Handle(player.Id, use, 1.2);

        Assert.Equal(Math.Min(player.Hp.Max, 10 + ItemCatalog.Get("pocao_hp_p").HealHp), player.Hp.Current);
        Assert.Equal(4, player.Bag.Count("pocao_hp_p"));
        Assert.Contains(outbox.Notices(player.Id), n => n.StartsWith("Espere"));
    }

    [Fact]
    public void Pocao_de_mana_tem_recarga_propria()
    {
        var (world, outbox, player) = Setup();
        player.Bag.TryAdd("pocao_mp_p", 2, NewUid);
        player.Hp.Add(-player.Hp.Max + 10);
        player.Mp.Add(-player.Mp.Current);
        ItemCommand Use(string id) => new()
        {
            Action = ItemActionKind.Use, From = ItemContainerKind.Bag,
            FromSlot = (ushort)player.Bag.Items().First(i => i.Item.ItemId == id).Slot,
        };

        world.Handle(player.Id, Use("pocao_mp_p"), 1);
        world.Handle(player.Id, Use("pocao_hp_p"), 1.1); // a de vida não espera a de mana
        world.Handle(player.Id, Use("pocao_mp_p"), 1.2); // a de mana, sim

        Assert.Equal(Math.Min(player.Mp.Max, ItemCatalog.Get("pocao_mp_p").HealMp), player.Mp.Current);
        Assert.Equal(1, player.Bag.Count("pocao_mp_p"));
        Assert.Equal(4, player.Bag.Count("pocao_hp_p"));
        Assert.Contains(outbox.Notices(player.Id), n => n.StartsWith("Espere"));
    }

    [Fact]
    public void Sem_mp_bebe_a_pocao_de_mana_sozinho_e_usa_a_skill()
    {
        var mage = AuthService.InitialCharacter(CharacterClass.Arcanist, NewUid) with { Id = 8, AccountId = 1, Name = "Bia" };
        var (world, outbox, player) = Setup(Dummy(hp: 5000), record: mage);
        var monster = world.Monsters.Single();
        player.Mp.Add(-player.Mp.Current);

        // Sem poção: recusa.
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 1);
        Assert.Empty(outbox.To<CombatEvent>(player.Id));

        player.Bag.TryAdd("pocao_mp_m", 1, NewUid);
        player.Bag.TryAdd("pocao_mp_p", 1, NewUid);
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 3);

        Assert.NotEmpty(outbox.To<CombatEvent>(player.Id));
        Assert.Equal(0, player.Bag.Count("pocao_mp_p")); // a menor que cobre a falta
        Assert.Equal(1, player.Bag.Count("pocao_mp_m"));
        Assert.Contains(outbox.Notices(player.Id), n => n.StartsWith("Faltou MP"));
    }

    [Fact]
    public void Vida_volta_devagar_so_fora_de_combate()
    {
        var (world, _, player) = Setup(Dummy(hp: 5000));
        var monster = world.Monsters.Single();
        player.Hp.Add(-player.Hp.Max / 2);
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 1);
        player.Movement.Teleport(200, 200, 1); // longe do monstro, que desiste e volta para casa
        var hit = player.Hp.Current;

        world.Tick(5); // menos de 8 s depois do golpe: nada
        Assert.Equal(hit, player.Hp.Current);

        for (var t = 5.25; t <= 20; t += 0.25) world.Tick(t);
        var expected = (int)(player.Hp.Max * PlayerRules.HpRegenFractionPerSecond * (20 - 1 - PlayerRules.OutOfCombatSeconds));
        Assert.InRange(player.Hp.Current - hit, expected - player.Hp.Max / 100, expected + player.Hp.Max / 100);
    }

    [Fact]
    public void Monstro_prefere_a_lamina_e_so_troca_de_alvo_com_folga()
    {
        var (world, _, blade) = Setup(Dummy(hp: 1_000_000));
        var monster = world.Monsters.Single();
        var mage = world.AddPlayer("", 0, null,
            AuthService.InitialCharacter(CharacterClass.Arcanist, NewUid) with { Id = 8, AccountId = 2, Name = "Bia" });
        mage.Movement.Teleport(0, 0, 0);

        // A Bia bate primeiro: o monstro vai nela.
        world.Handle(mage.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 1);
        world.Tick(1.1);
        Assert.Equal(mage.Id, monster.AggroPlayerId);

        // Um golpe da Lâmina vale o dobro em ameaça: passa a Bia (que vale 0,8) com folga, e o monstro troca.
        world.Handle(blade.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 2);
        world.Tick(2.1);
        Assert.Equal(blade.Id, monster.AggroPlayerId);
        Assert.True(monster.Threat[blade.Id] > monster.Threat[mage.Id]);

        // Um pouco mais de ameaça não basta para tirar o alvo: precisa passar de 110% (perto) ou 130% (longe).
        monster.Threat[mage.Id] = monster.Threat[blade.Id] * 1.05;
        world.Tick(2.2);
        Assert.Equal(blade.Id, monster.AggroPlayerId);
        monster.Threat[mage.Id] = monster.Threat[blade.Id] * 1.2;
        world.Tick(2.3);
        Assert.Equal(mage.Id, monster.AggroPlayerId);
    }

    [Fact]
    public void Giro_de_lamina_provoca_e_puxa_o_monstro()
    {
        var (world, _, blade) = Setup(Dummy(hp: 1_000_000));
        var monster = world.Monsters.Single();
        var mage = world.AddPlayer("", 0, null,
            AuthService.InitialCharacter(CharacterClass.Arcanist, NewUid) with { Id = 8, AccountId = 2, Name = "Bia" });
        mage.Movement.Teleport(0, 0, 0);
        blade.LearnedSkills.Add(ThreatRules.TauntSkillId);
        monster.Threat[mage.Id] = 50_000; // a Bia já irritou muito

        var giro = blade.Skills.Select((s, i) => (s, i)).First(x => x.s.Id == ThreatRules.TauntSkillId).i;
        world.Handle(blade.Id, new UseSkill { Slot = (byte)giro, TargetId = monster.Id }, 1);
        world.Tick(1.1);

        Assert.Equal(blade.Id, monster.AggroPlayerId);
        Assert.True(monster.Threat[blade.Id] >= 50_000 * ThreatRules.TauntMargin);
    }

    [Fact]
    public void Despertar_ativa_com_barra_cheia_e_aumenta_o_dano()
    {
        var (world, outbox, player) = Setup(Dummy(hp: 100_000));
        var monster = world.Monsters.Single();
        world.Handle(player.Id, new ActivateAwakening(), 1);
        Assert.Contains("A barra do Despertar ainda não encheu", outbox.Notices(player.Id));

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 2);
        var normal = outbox.To<CombatEvent>(player.Id).Last().Damage;

        player.Awakening.Restore(AwakeningState.MaxGauge);
        world.Handle(player.Id, new ActivateAwakening(), 3);
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 4);
        var awakened = outbox.To<CombatEvent>(player.Id).Last().Damage;

        Assert.True(player.Awakening.IsActive(4));
        Assert.Equal((int)Math.Round(normal * (1 + AwakeningState.DamageBonus), MidpointRounding.AwayFromZero), awakened);
    }

    [Fact]
    public void Item_salvo_em_lugar_invalido_volta_para_a_bolsa_sem_sumir()
    {
        var record = AuthService.InitialCharacter() with
        {
            Id = 7, Name = "Ana",
            Items = new[]
            {
                new ItemRecord(1, ItemContainerKind.Equipment, (int)EquipSlot.Boots, "espada_ferro", 1), // espada nas botas
                new ItemRecord(2, ItemContainerKind.Bag, 99, "pelo_macio", 3),                          // posição fora da bolsa
                new ItemRecord(3, ItemContainerKind.Bag, 0, "item_que_nao_existe", 1),
                new ItemRecord(4, ItemContainerKind.Bag, 1, "pocao_hp_p", 130),                          // pilha acima do limite
            },
        };
        var (_, _, player) = Setup(record: record);

        Assert.Null(player.Equipment[EquipSlot.Boots]);
        Assert.Equal(1, player.Bag.Count("espada_ferro"));
        Assert.Equal(3, player.Bag.Count("pelo_macio"));
        Assert.Equal(130, player.Bag.Count("pocao_hp_p"));
    }
}
