using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Combat;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.Social;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Grupo (convite, líder, XP e drop divididos) e chat (canais, filtro, limite), decididos pelo servidor.</summary>
public class SocialWorldTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();

        public List<string> System(int playerId) =>
            To<ChatMessage>(playerId).Where(m => m.Channel == ChatChannel.System).Select(m => m.Display).ToList();
    }

    private sealed class FixedRandom : IRandomSource
    {
        public double NextDouble() => 0.5;
    }

    private static MonsterData Dummy(int hp = 1) => new()
    {
        Id = "teste", Level = 1, MaxHp = hp, AttackDamage = 10, AttackPower = 0, Defense = 0,
        AttackRange = 1.5f, AttackCooldownSeconds = 2f, AttackWindupSeconds = 0.5f, MoveSpeed = 3f, Aggressive = false,
        AggroRadius = 8, LeashRadius = 30, WanderRadius = 0, XpReward = 100, RespawnSeconds = 5,
        Drops = new DropTable(90, 90, new DropEntry("pelo_macio", ItemRarity.Common, 1.0)),
    };

    /// <summary>Mundo com um monstro em (0, 2) e convidados na origem.</summary>
    private static (GameWorld World, FakeOutbox Outbox, ServerPlayer[] Players) Setup(params string[] names)
    {
        var outbox = new FakeOutbox();
        var spawns = new[] { new SpawnDefinition { Data = Dummy(), CenterX = 0, CenterZ = 2, Count = 1 } };
        var world = new GameWorld(outbox, spawns, new FixedRandom(), new ItemUidAllocator(5000));
        var players = names.Select(n =>
        {
            var p = world.AddPlayer(n, 0);
            p.Movement.Teleport(0, 0, 0);
            return p;
        }).ToArray();
        return (world, outbox, players);
    }

    private static void Group(GameWorld world, ServerPlayer leader, params ServerPlayer[] members)
    {
        foreach (var member in members)
        {
            world.Handle(leader.Id, new PartyCommand { Action = PartyActionKind.Invite, TargetName = member.Name }, 1);
            world.Handle(member.Id, new PartyCommand { Action = PartyActionKind.Accept, TargetId = leader.Id }, 1);
        }
    }

    [Fact]
    public void Convite_aceito_forma_grupo_com_o_lider_e_manda_o_estado()
    {
        var (world, outbox, p) = Setup("Ana", "Bia");
        world.Handle(p[0].Id, new PartyCommand { Action = PartyActionKind.Invite, TargetName = "bia" }, 1);
        var invite = Assert.Single(outbox.To<PartyInvite>(p[1].Id));
        Assert.Equal("Ana", invite.InviterName);

        world.Handle(p[1].Id, new PartyCommand { Action = PartyActionKind.Accept, TargetId = p[0].Id }, 2);
        Assert.NotEqual(0, p[0].PartyId);
        Assert.Equal(p[0].PartyId, p[1].PartyId);
        Assert.Equal(p[0].Id, world.FindParty(p[0].PartyId)!.LeaderId);

        world.Tick(2.05);
        var state = outbox.To<PartyState>(p[1].Id).Last();
        Assert.Equal(p[0].Id, state.LeaderId);
        Assert.Equal(new[] { "Ana", "Bia" }, state.Members.Select(m => m.Name));
        Assert.Contains("Bia entrou no grupo", outbox.System(p[0].Id));

        // Sem mudança, não reenvia.
        var count = outbox.To<PartyState>(p[1].Id).Count;
        world.Tick(2.10);
        Assert.Equal(count, outbox.To<PartyState>(p[1].Id).Count);
        // A vida de um membro mudou: reenvia.
        p[0].Hp.Add(-10);
        world.Tick(2.15);
        Assert.Equal(count + 1, outbox.To<PartyState>(p[1].Id).Count);
    }

    [Fact]
    public void Convite_expira_e_so_o_lider_convida_ate_quatro()
    {
        var (world, outbox, p) = Setup("Ana", "Bia", "Caio", "Davi", "Eva");
        world.Handle(p[0].Id, new PartyCommand { Action = PartyActionKind.Invite, TargetName = "Bia" }, 1);
        world.Handle(p[1].Id, new PartyCommand { Action = PartyActionKind.Accept, TargetId = p[0].Id }, 1 + PartyRules.InviteSeconds + 1);
        Assert.Equal(0, p[1].PartyId);
        Assert.Contains("O convite expirou", outbox.System(p[1].Id));

        Group(world, p[0], p[1]);
        world.Handle(p[1].Id, new PartyCommand { Action = PartyActionKind.Invite, TargetName = "Caio" }, 50);
        Assert.Contains("Só o líder pode convidar", outbox.System(p[1].Id));

        Group(world, p[0], p[2], p[3]);
        world.Handle(p[0].Id, new PartyCommand { Action = PartyActionKind.Invite, TargetName = "Eva" }, 50);
        Assert.Contains(outbox.System(p[0].Id), t => t.StartsWith("O grupo está cheio"));
        Assert.Equal(4, world.FindParty(p[0].PartyId)!.Members.Count);

        world.Handle(p[0].Id, new PartyCommand { Action = PartyActionKind.Invite, TargetName = "Ninguem" }, 50);
        Assert.Contains("Ninguem não está online", outbox.System(p[0].Id));
    }

    [Fact]
    public void Lider_sai_passa_a_lideranca_e_grupo_de_um_e_desfeito()
    {
        var (world, outbox, p) = Setup("Ana", "Bia", "Caio");
        Group(world, p[0], p[1], p[2]);
        var party = world.FindParty(p[0].PartyId)!;

        world.Handle(p[0].Id, new PartyCommand { Action = PartyActionKind.Leave }, 2);
        Assert.Equal(0, p[0].PartyId);
        Assert.Equal(p[1].Id, party.LeaderId);

        world.Handle(p[1].Id, new PartyCommand { Action = PartyActionKind.Kick, TargetName = "Caio" }, 3);
        Assert.Contains("Você foi expulso do grupo", outbox.System(p[2].Id));
        Assert.Equal(0, p[1].PartyId);
        Assert.Equal(0, p[2].PartyId);
        Assert.Null(world.FindParty(party.Id));

        world.Tick(3.05);
        Assert.Empty(outbox.To<PartyState>(p[2].Id).Last().Members);
    }

    [Fact]
    public void Desconectar_tira_do_grupo()
    {
        var (world, outbox, p) = Setup("Ana", "Bia", "Caio");
        Group(world, p[0], p[1], p[2]);
        world.RemovePlayer(p[0].Id);
        var party = world.FindParty(p[1].PartyId)!;
        Assert.Equal(new[] { p[1].Id, p[2].Id }, party.Members);
        Assert.Equal(p[1].Id, party.LeaderId);
        Assert.Contains("Ana saiu do jogo e deixou o grupo", outbox.System(p[2].Id));
    }

    [Fact]
    public void Xp_alz_e_missao_sao_divididos_com_quem_esta_perto()
    {
        var (world, outbox, p) = Setup("Ana", "Bia", "Caio");
        Group(world, p[0], p[1], p[2]);
        p[2].Movement.Teleport(0, 120, 0); // longe demais
        var monster = world.Monsters.Single();

        world.Handle(p[0].Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 5);

        var shared = PartyRules.SharedXp(100, 1, 1, 2);
        Assert.Equal(shared, p[0].Progress.Xp);
        Assert.Equal(shared, p[1].Progress.Xp);
        Assert.Equal(0, p[2].Progress.Xp);
        Assert.Equal(45, p[0].Alz);
        Assert.Equal(45, p[1].Alz);
        Assert.Equal(0, p[2].Alz);

        // O drop vai pela vez do grupo (a primeira é de quem matou): só a dona pega, mesmo do mesmo grupo.
        var loot = world.Loot.Single();
        Assert.Equal(p[0].Id, loot.OwnerId);
        Assert.False(loot.CanBePickedBy(p[1].Id, p[1].PartyId, 6));
        Assert.False(loot.CanBePickedBy(99, 0, 6 + ServerLoot.OwnerSeconds));
        world.Handle(p[0].Id, new PickupLoot { LootId = loot.Id }, 6);
        Assert.Equal(1, p[0].Bag.Count("pelo_macio"));
    }

    [Fact]
    public void Dano_do_grupo_soma_para_decidir_quem_leva()
    {
        var (world, _, p) = Setup("Ana", "Bia", "Solo");
        Group(world, p[0], p[1]);
        var monster = world.Monsters.Single();
        monster.DamageBy[p[2].Id] = 500;
        monster.DamageBy[p[1].Id] = 300;
        monster.DamageBy[p[0].Id] = 299;

        world.Handle(p[0].Id, new UseSkill { Slot = 0, TargetId = monster.Id }, 5);
        // O solo perdeu para a soma do grupo: ajudou bastante, então leva o XP, mas nem o Alz nem o drop.
        Assert.True(p[2].Progress.Xp > 0);
        Assert.Equal(0, p[2].Alz);
        Assert.True(p[0].Progress.Xp > 0);
        Assert.True(p[1].Progress.Xp > 0);
        // Dentro do grupo, quem bateu mais é o dono do drop.
        Assert.Equal(p[1].Id, world.Loot.Single().OwnerId);
    }

    [Fact]
    public void Chat_de_mapa_grupo_e_sussurro_chegam_so_a_quem_deve()
    {
        var (world, outbox, p) = Setup("Ana", "Bia", "Caio");
        Group(world, p[0], p[1]);

        world.Handle(p[0].Id, new ChatSend { Channel = ChatChannel.Map, Text = "  bom dia  " }, 10);
        Assert.All(p, player => Assert.Contains(outbox.To<ChatMessage>(player.Id), m => m.Channel == ChatChannel.Map && m.Text == "bom dia"));

        world.Handle(p[1].Id, new ChatSend { Channel = ChatChannel.Party, Text = "bora pro chefe" }, 10);
        Assert.Contains(outbox.To<ChatMessage>(p[0].Id), m => m.Channel == ChatChannel.Party && m.FromName == "Bia");
        Assert.DoesNotContain(outbox.To<ChatMessage>(p[2].Id), m => m.Channel == ChatChannel.Party);

        world.Handle(p[2].Id, new ChatSend { Channel = ChatChannel.Whisper, Target = "ANA", Text = "que merda" }, 10);
        var received = outbox.To<ChatMessage>(p[0].Id).Single(m => m.Channel == ChatChannel.Whisper);
        Assert.Equal(("Caio", "que *****"), (received.FromName, received.Text));
        var copy = outbox.To<ChatMessage>(p[2].Id).Single(m => m.Channel == ChatChannel.Whisper);
        Assert.Equal("Ana", copy.ToName);
        Assert.DoesNotContain(outbox.To<ChatMessage>(p[1].Id), m => m.Channel == ChatChannel.Whisper);

        world.Handle(p[2].Id, new ChatSend { Channel = ChatChannel.Party, Text = "oi" }, 10);
        Assert.Contains("Você não está em um grupo", outbox.System(p[2].Id));
        world.Handle(p[2].Id, new ChatSend { Channel = ChatChannel.System, Text = "falso aviso" }, 10);
        Assert.DoesNotContain(outbox.To<ChatMessage>(p[0].Id), m => m.Text == "falso aviso");
    }

    [Fact]
    public void Flood_no_chat_e_barrado()
    {
        var (world, outbox, p) = Setup("Ana", "Bia");
        for (var i = 0; i < ChatRules.Burst + 3; i++)
            world.Handle(p[0].Id, new ChatSend { Channel = ChatChannel.Map, Text = $"msg {i}" }, 20);
        Assert.Equal(ChatRules.Burst, outbox.To<ChatMessage>(p[1].Id).Count(m => m.Channel == ChatChannel.Map));
        Assert.Contains(outbox.System(p[0].Id), t => t.StartsWith("Calma!"));
    }
}
