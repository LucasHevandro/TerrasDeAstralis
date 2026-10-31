using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

public class CombatTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();

        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();

        public List<string> Notices(int playerId) => To<Notice>(playerId).Select(n => n.Text.ToString()).ToList();
    }

    /// <summary>Sempre o mesmo número: 0,5 = sem variação de dano e sem crítico.</summary>
    private sealed class FixedRandom : IRandomSource
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public double NextDouble() => _value;
    }

    private static MonsterData Dummy(bool aggressive = false, int hp = 5000, int attack = 50, float windup = 0.5f) => new()
    {
        Id = "teste", Level = 1, MaxHp = hp, AttackDamage = attack, AttackPower = 0,
        Defense = 0, AttackRange = 1.5f, AttackCooldownSeconds = 2f, AttackWindupSeconds = windup, MoveSpeed = 3f,
        Aggressive = aggressive, AggroRadius = 8, LeashRadius = 30, WanderRadius = 0, XpReward = 50, RespawnSeconds = 5,
        Drops = new DropTable(10, 20, new DropEntry("pelo_macio", ItemRarity.Common, 0.9)),
    };

    private static (GameWorld World, FakeOutbox Outbox, ServerPlayer Player, ServerMonster Monster) Setup(
        MonsterData data, float monsterZ = 2f, double random = 0.5, bool boss = false, double eliteChance = 0)
    {
        var outbox = new FakeOutbox();
        var spawn = new SpawnDefinition { Data = data, CenterX = 0, CenterZ = monsterZ, Count = 1, Radius = 0, IsBoss = boss };
        var world = new GameWorld(outbox, new[] { spawn }, new FixedRandom(random), eliteChance: eliteChance);
        var player = world.AddPlayer("Ana", 0);
        player.Movement.Teleport(0, 0, 0); // os testes medem distâncias a partir da origem
        return (world, outbox, player, world.Monsters.Single());
    }

    [Fact]
    public void Skill_acerta_o_monstro_e_respeita_a_recarga()
    {
        var (world, outbox, player, monster) = Setup(Dummy());

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 1 }, now: 1);
        // Depois da conjuração (0,45 s) e antes da recarga (0,5 s).
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 1.4 }, now: 1.4);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id));
        Assert.Equal((player.Id, monster.Id, (byte)0), (hit.AttackerId, hit.TargetId, hit.Source));
        // (120 + ataque do personagem: nível, força e espada) × 1, sem defesa, sem variação.
        var expected = 120 + player.Stats.AttackPower;
        Assert.Equal(expected, hit.Damage);
        Assert.Equal(5000 - expected, monster.Hp.Current);
        Assert.Contains("Corte em recarga", outbox.Notices(player.Id));
    }

    [Fact]
    public void Skill_pedida_durante_a_conjuracao_espera_na_fila()
    {
        var (world, outbox, player, monster) = Setup(Dummy());

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 1 }, now: 1);
        // Giro de Lâmina pedido no meio do Corte (0,45 s de conjuração): espera.
        world.Handle(player.Id, new UseSkill { Slot = 1, TargetId = monster.Id, ClientTime = 1.1 }, now: 1.1);
        Assert.Single(outbox.To<CombatEvent>(player.Id));
        Assert.NotNull(player.QueuedSkill);

        world.Tick(1.2);
        Assert.Single(outbox.To<CombatEvent>(player.Id)); // ainda conjurando
        world.Tick(1.45);
        Assert.Equal(new byte[] { 0, 1 }, outbox.To<CombatEvent>(player.Id).Select(e => e.Source).ToArray());
        Assert.Null(player.QueuedSkill);
    }

    [Fact]
    public void Skill_esquecida_na_fila_nao_sai_depois()
    {
        var (world, outbox, player, monster) = Setup(Dummy());
        player.CastUntil = 10; // conjurando algo longo
        world.Handle(player.Id, new UseSkill { Slot = 1, TargetId = monster.Id, ClientTime = 1 }, now: 1);
        world.Tick(10);
        Assert.Empty(outbox.To<CombatEvent>(player.Id));
    }

    [Fact]
    public void Fora_do_alcance_nao_acerta()
    {
        var (world, outbox, player, monster) = Setup(Dummy(), monsterZ: 20);

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, now: 1);

        Assert.Empty(outbox.To<CombatEvent>(player.Id));
        Assert.Contains("Fora de alcance", outbox.Notices(player.Id));
    }

    [Fact]
    public void Magia_gasta_mana()
    {
        var (world, outbox, player, monster) = Setup(Dummy());

        world.Handle(player.Id, new UseSkill { Slot = 2, TargetId = monster.Id }, now: 1); // Golpe Rúnico (mágica)
        var cost = player.Skills[2].MpCost;

        Assert.Equal(player.Mp.Max - cost, player.Mp.Current);
        world.Tick(1.0); // no tick a mana já começa a voltar (20/s) e o cliente é avisado
        Assert.InRange(outbox.To<PlayerStats>(player.Id).Last().Mp, player.Mp.Max - cost, player.Mp.Max - cost + 1);
    }

    [Fact]
    public void Matar_da_XP_Alz_e_drop_do_dono_e_o_monstro_renasce()
    {
        var (world, outbox, player, monster) = Setup(Dummy(hp: 100), random: 0.0);

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, now: 1);
        world.Tick(1.0);

        Assert.True(monster.IsDead);
        var flags = outbox.To<Snapshot>(player.Id).Last().Entities.Single(e => e.Id == monster.Id).Flags;
        Assert.NotEqual(0, flags & EntityState.FlagDead);
        var stats = outbox.To<PlayerStats>(player.Id).Last();
        Assert.Equal(50, stats.Xp);  // monstro do mesmo nível: XP cheia
        Assert.Equal(10, stats.Alz); // sorteio 0 = mínimo da faixa
        var loot = Assert.Single(outbox.To<LootSpawned>(player.Id));
        Assert.Equal("pelo_macio", loot.ItemId);

        world.Tick(1.0 + monster.Data.RespawnSeconds + 0.1);
        Assert.False(monster.IsDead);
        Assert.Equal(monster.Hp.Max, monster.Hp.Current);
    }

    [Fact]
    public void Ranger_nasce_com_arco_e_acerta_de_longe()
    {
        var outbox = new FakeOutbox();
        var spawn = new SpawnDefinition { Data = Dummy(), CenterX = 0, CenterZ = 10, Count = 1, Radius = 0 };
        var world = new GameWorld(outbox, new[] { spawn }, new FixedRandom(0.5), eliteChance: 0) { GuestClass = CharacterClass.Ranger };
        var player = world.AddPlayer("Lia", 0);
        player.Movement.Teleport(0, 0, 0);
        var monster = world.Monsters.Single();

        Assert.Equal(CharacterClass.Ranger, player.Class);
        Assert.Equal("arco_ferro", player.Equipment[EquipSlot.Weapon]?.ItemId);
        Assert.Equal("tiro_certeiro", player.Skills[0].Id);
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 1 }, now: 1);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id));
        Assert.Equal(110 + player.Stats.AttackPower, hit.Damage); // flecha física: ataque do arco e da Força
        world.Tick(1.1);
        Assert.Equal("ranger", outbox.To<EntitySpawned>(player.Id).Single(s => s.Id == player.Id).TypeId);
    }

    [Fact]
    public void Guardiao_nasce_com_machado_e_apanha_menos_dos_monstros()
    {
        int Taken(CharacterClass characterClass)
        {
            var outbox = new FakeOutbox();
            var spawn = new SpawnDefinition { Data = Dummy(aggressive: true, attack: 1000), CenterX = 0, CenterZ = 1.5f, Count = 1, Radius = 0 };
            var world = new GameWorld(outbox, new[] { spawn }, new FixedRandom(0.5), eliteChance: 0) { GuestClass = characterClass };
            var player = world.AddPlayer("Ana", 0);
            player.Movement.Teleport(0, 0, 0);
            if (characterClass == CharacterClass.Guardian)
            {
                Assert.Equal("machado_ferro", player.Equipment[EquipSlot.Weapon]?.ItemId);
                Assert.Equal("golpe_de_machado", player.Skills[0].Id);
            }
            for (var t = 0.05; t <= 0.7; t += 0.05) world.Tick(t);
            return player.Hp.Max - player.Hp.Current;
        }

        var blade = Taken(CharacterClass.Blade);
        var guardian = Taken(CharacterClass.Guardian);
        Assert.True(blade > 0);
        Assert.True(guardian <= blade * 0.86, $"Guardião levou {guardian}, Lâmina {blade}");
    }

    [Fact]
    public void Quem_ajudou_a_matar_sem_grupo_tambem_ganha_xp_mas_nao_o_alz()
    {
        var (world, outbox, ana, monster) = Setup(Dummy(hp: 100_000));
        var bia = world.AddPlayer("Bia", 0);
        bia.Movement.Teleport(0.5f, 0, 0);
        var caio = world.AddPlayer("Caio", 0);
        caio.Movement.Teleport(-0.5f, 0, 0);

        world.Handle(ana.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 1 }, now: 1);
        world.Handle(bia.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 1 }, now: 1);
        monster.DamageBy[caio.Id] = 1; // encostou de leve: bem menos de 15% do dano
        monster.Hp.Add(-(monster.Hp.Current - 1));
        world.Handle(ana.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 2 }, now: 2);
        world.Tick(2.1);

        Assert.True(monster.IsDead);
        Assert.Equal(50, outbox.To<PlayerStats>(ana.Id).Last().Xp);
        Assert.Equal(50, outbox.To<PlayerStats>(bia.Id).Last().Xp); // ajudou: XP inteiro
        Assert.Equal(0, bia.Alz); // o Alz e o drop ficam com quem matou
        Assert.True(ana.Alz > 0);
        Assert.Equal(1, bia.Session.Kills);
        Assert.Equal(0, caio.Session.Kills);
    }

    [Fact]
    public void Elite_tem_quatro_vezes_a_vida_e_paga_mais()
    {
        var (world, outbox, player, monster) = Setup(Dummy(hp: 100), random: 0.0, eliteChance: 1);
        Assert.True(monster.IsElite);
        Assert.Equal(400, monster.Hp.Max);
        Assert.Equal(400, monster.Hp.Current); // nasce com a vida cheia, não com a vida do monstro comum
        world.Tick(0.1);
        Assert.True(outbox.To<EntitySpawned>(player.Id).Single(s => s.Id == monster.Id).Elite);

        monster.Hp.Add(-399);
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, now: 1);
        world.Tick(1.0);

        Assert.True(monster.IsDead);
        var stats = outbox.To<PlayerStats>(player.Id).Last();
        Assert.Equal(200, stats.Xp); // XP ×4
        Assert.Equal(30, stats.Alz); // Alz ×3
        Assert.Equal(2, outbox.To<LootSpawned>(player.Id).Count); // uma rolagem de drop a mais
    }

    [Fact]
    public void Elite_bate_uma_vez_e_meia_mais_forte()
    {
        int FirstHit(double eliteChance)
        {
            var (world, outbox, player, monster) = Setup(Dummy(aggressive: true, attack: 200), monsterZ: 1f, eliteChance: eliteChance);
            for (var t = 0.1; t < 5; t += 0.1)
            {
                world.Tick(t);
                var hit = outbox.To<CombatEvent>(player.Id).FirstOrDefault(e => e.AttackerId == monster.Id);
                if (hit != null) return hit.Damage;
            }
            throw new InvalidOperationException("O monstro não atacou.");
        }

        var normal = FirstHit(0);
        Assert.Equal((int)Math.Round(normal * 1.5), FirstHit(1));
    }

    [Fact]
    public void Chefe_nunca_e_elite()
    {
        var (_, _, _, monster) = Setup(Dummy(), boss: true, eliteChance: 1);

        Assert.False(monster.IsElite);
        Assert.Equal(5000, monster.Hp.Max);
    }

    [Fact]
    public void Pegar_item_vai_para_o_inventario_e_some_para_todos()
    {
        var (world, outbox, player, monster) = Setup(Dummy(hp: 100), random: 0.0);
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, now: 1);
        world.Tick(1.0);
        var loot = world.Loot.Single();

        world.Handle(player.Id, new PickupLoot { LootId = loot.Id }, now: 1.1);

        Assert.Equal(1, player.Bag.Count("pelo_macio"));
        Assert.Equal(loot.Id, Assert.Single(outbox.To<LootRemoved>(player.Id)).LootId);
        Assert.Empty(world.Loot);
    }

    [Fact]
    public void Combo_julga_pelo_horario_do_aperto_compensando_a_latencia()
    {
        var (world, outbox, player, monster) = Setup(Dummy());
        world.Handle(player.Id, new StartCombo { TargetId = monster.Id, ClientTime = 10 }, now: 10);

        // Apertou no centro da janela (10 + 0,75 × 1,2 = 10,9), mas a mensagem chegou 0,2 s depois:
        // sem compensação, 11,1 já passou do fim da janela.
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 10.9 }, now: 11.1);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id));
        Assert.Equal((byte)ComboGrade.Excellent, hit.Grade);
        Assert.Equal((int)Math.Round((120 + player.Stats.AttackPower) * 1.2), hit.Damage); // excelente: +20%
        Assert.Equal(1, player.Combo.HitCount);
        Assert.Equal(PlayerRulesStartingSpMinusCombo(), player.Sp.Current);
    }

    private static int PlayerRulesStartingSpMinusCombo() => PlayerRules.StartingSp - ComboConfig.Default.ActivationSpCost;

    [Theory]
    [InlineData(50.0, 10.0, 10.0)]   // do futuro: vira agora
    [InlineData(9.0, 10.0, 9.65)]    // antigo demais: limitado a 0,35 s
    [InlineData(9.8, 10.0, 9.8)]     // dentro do limite: aceito
    public void Horario_do_aperto_tem_limites(double clientTime, double now, double expected)
    {
        Assert.Equal(expected, GameWorld.PressTime(clientTime, now), 6);
    }

    [Fact]
    public void Monstro_agressivo_ataca_depois_do_preparo()
    {
        var (world, outbox, player, monster) = Setup(Dummy(aggressive: true), monsterZ: 1.5f);

        for (var t = 0.05; t <= 0.7; t += 0.05) world.Tick(t);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id), e => e.AttackerId == monster.Id);
        Assert.Equal(CombatSource.MonsterAttack, hit.Source);
        Assert.True(player.Hp.Current < player.Hp.Max);
    }

    [Fact]
    public void Esquiva_na_hora_certa_evita_o_golpe()
    {
        var (world, outbox, player, monster) = Setup(Dummy(aggressive: true), monsterZ: 1.5f);
        world.Tick(0.05); // começa o preparo; o golpe sai em 0,55

        world.Handle(player.Id, new MoveInput { Sequence = 1, X = 0, Z = 0, Dodging = true }, now: 0.45);
        for (var t = 0.1; t <= 0.7; t += 0.05) world.Tick(t);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id), e => e.AttackerId == monster.Id);
        Assert.True(hit.Dodged);
        Assert.Equal(player.Hp.Max, player.Hp.Current);
    }

    [Fact]
    public void Jogador_morre_e_renasce_na_vila()
    {
        var (world, outbox, player, monster) = Setup(Dummy(aggressive: true, attack: 999_999), monsterZ: 1.5f);

        for (var t = 0.05; t <= 0.7; t += 0.05) world.Tick(t);
        Assert.True(player.IsDead);
        Assert.Contains("Você morreu", outbox.Notices(player.Id));

        world.Tick(0.7 + PlayerRules.RespawnSeconds);
        Assert.False(player.IsDead);
        Assert.Equal(player.Hp.Max, player.Hp.Current);
        Assert.NotEmpty(outbox.To<PositionCorrection>(player.Id));
    }

    [Fact]
    public void Quem_morre_nas_ruinas_renasce_na_fogueira_mais_perto()
    {
        // Monstro forte perto da Erudita Veska, nas Ruínas, e o jogador ao lado dele.
        var spawn = new SpawnDefinition { Data = Dummy(aggressive: true, attack: 999_999), CenterX = -30, CenterZ = 109.5f, Count = 1 };
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, new[] { spawn }, new FixedRandom(0.5));
        var player = world.AddPlayer("Ana", 0);
        player.Movement.Teleport(-30, 108, 0);
        var camp = WorldContent.RespawnPoints.Single(p => p.Id == "acampamento_ruinas");

        for (var t = 0.05; t <= 0.7; t += 0.05) world.Tick(t);
        Assert.True(player.IsDead);
        world.Tick(0.7 + PlayerRules.RespawnSeconds);

        Assert.False(player.IsDead);
        Assert.InRange(MathF.Sqrt((player.X - camp.X) * (player.X - camp.X) + (player.Z - camp.Z) * (player.Z - camp.Z)), 2f, 4.5f);
        Assert.Contains(outbox.Notices(player.Id), n => n.Contains(camp.Name));
    }

    [Fact]
    public void So_ve_o_que_esta_perto()
    {
        var near = new SpawnDefinition { Data = Dummy(), CenterX = 0, CenterZ = 30, Count = 1 };
        var farData = Dummy();
        farData.Id = "longe";
        var far = new SpawnDefinition { Data = farData, CenterX = 0, CenterZ = 200, Count = 1 };
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, new[] { near, far }, new FixedRandom(0.5));
        var player = world.AddPlayer("Ana", 0);
        player.Movement.Teleport(0, 0, 0);

        world.Tick(0.05);

        var spawned = outbox.To<EntitySpawned>(player.Id).Where(e => e.Kind == EntityKind.Monster).ToList();
        Assert.Equal("teste", Assert.Single(spawned).TypeId);
        Assert.DoesNotContain(outbox.To<Snapshot>(player.Id).Last().Entities,
            e => e.Id == world.Monsters.Single(m => m.Data.Id == "longe").Id);
    }

    [Fact]
    public void Chefe_entra_na_fase_2_e_marca_area_que_acerta_quem_ficou()
    {
        var data = Dummy(aggressive: true, hp: 1000);
        data.AttackDamage = 1; // o golpe normal quase não machuca; o teste é a área
        var (world, outbox, player, boss) = Setup(data, monsterZ: 6, boss: true);

        boss.Hp.Add(-600);
        boss.AggroPlayerId = player.Id;
        boss.Brain.Provoke();
        double t = 0;
        while (t < 2.0) world.Tick(t += 0.05);

        Assert.Contains(outbox.To<Notice>(player.Id), n => n.Announcement && n.Text.ToString().Contains("furioso"));
        var warning = Assert.Single(outbox.To<AreaWarning>(player.Id));
        Assert.Equal(BossRules.AreaRadius, warning.Radius);

        while (t < 2.0 + BossRules.AreaWarningSeconds + 0.2) world.Tick(t += 0.05);
        Assert.Contains(outbox.To<CombatEvent>(player.Id), e => e.Source == CombatSource.BossArea && e.Damage > 0);
    }
}
