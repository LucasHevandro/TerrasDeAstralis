using Game.Server.World;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

public class GameWorldTests
{
    private static readonly SpawnDefinition[] NoMonsters = Array.Empty<SpawnDefinition>();

    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();

        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();
    }

    [Fact]
    public void Quem_entra_recebe_boas_vindas()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);

        var player = world.AddPlayer("Ana", now: 5);

        var welcome = Assert.Single(outbox.To<Welcome>(player.Id));
        Assert.Equal(player.Id, welcome.PlayerId);
        Assert.Equal(GameWorld.TickRate, welcome.TickRate);
        Assert.Equal(5, welcome.ServerTime);
    }

    [Fact]
    public void Jogadores_veem_uns_aos_outros()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        var bia = world.AddPlayer("Bia", 1);

        world.Tick(1.05);

        // Cada um conhece a si mesmo e ao outro.
        Assert.Contains(outbox.To<EntitySpawned>(bia.Id), e => e.Name == "Ana");
        Assert.Contains(outbox.To<EntitySpawned>(ana.Id), e => e.Name == "Bia");
    }

    [Fact]
    public void Jogadores_veem_uns_aos_outros_so_uma_vez()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        var bia = world.AddPlayer("Bia", 1);
        world.Tick(1.05);
        world.Tick(1.10);

        Assert.Single(outbox.To<EntitySpawned>(bia.Id), e => e.Name == "Ana");
        Assert.Single(outbox.To<EntitySpawned>(ana.Id), e => e.Name == "Bia");
    }

    [Fact]
    public void Id_eh_informado_antes_das_boas_vindas()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var sentBeforeCallback = -1;

        world.AddPlayer("Ana", 0, _ => sentBeforeCallback = outbox.Sent.Count);

        Assert.Equal(0, sentBeforeCallback);
    }

    [Fact]
    public void Movimento_valido_aparece_no_snapshot()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);

        world.Handle(ana.Id, new MoveInput { Sequence = 1, X = 3, Z = 0, Yaw = 90 }, now: 1);
        world.Tick(1.05);

        var state = Assert.Single(outbox.To<Snapshot>(ana.Id).Last().Entities);
        Assert.Equal((3f, 0f, 90f), (state.X, state.Z, state.Yaw));
        Assert.Empty(outbox.To<PositionCorrection>(ana.Id));
    }

    [Fact]
    public void Teleporte_eh_recusado_e_corrigido()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        var (startX, startZ) = (ana.Movement.X, ana.Movement.Z);

        world.Handle(ana.Id, new MoveInput { Sequence = 1, X = 100, Z = 0 }, now: 0.1);

        var correction = Assert.Single(outbox.To<PositionCorrection>(ana.Id));
        Assert.Equal((startX, startZ), (correction.X, correction.Z));
        Assert.Equal(startX, ana.Movement.X);
    }

    [Fact]
    public void Movimento_antigo_eh_ignorado()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        world.Handle(ana.Id, new MoveInput { Sequence = 5, X = 2, Z = 0 }, now: 1);

        world.Handle(ana.Id, new MoveInput { Sequence = 4, X = 1, Z = 0 }, now: 1.1);

        Assert.Equal(2f, ana.Movement.X);
    }

    [Fact]
    public void Quem_sai_some_para_os_outros()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        var bia = world.AddPlayer("Bia", 0);
        world.Tick(0.05);

        world.RemovePlayer(ana.Id);

        Assert.Equal(ana.Id, Assert.Single(outbox.To<EntityDespawned>(bia.Id)).Id);
        Assert.Single(world.Players);
    }

    [Fact]
    public void Ping_responde_com_o_horario_do_servidor()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);

        world.Handle(ana.Id, new Ping { ClientTime = 1.25 }, now: 9.5);

        var pong = Assert.Single(outbox.To<Pong>(ana.Id));
        Assert.Equal((1.25, 9.5), (pong.ClientTime, pong.ServerTime));
    }

    [Fact]
    public void Jogadores_nascem_espalhados_em_volta_da_fogueira_da_vila()
    {
        var points = Enumerable.Range(1, 6).Select(GameWorld.SpawnPointFor).ToList();

        Assert.Equal(points.Count, points.Distinct().Count());
        foreach (var (x, z) in points)
        {
            // Fora do fogo e perto o bastante para já poder sentar.
            Assert.InRange(MathF.Sqrt(x * x + z * z), RestRules.FireRadius + 0.5f, RestRules.Radius);
            Assert.True(RestRules.InRange(x, z));
        }
    }

    [Fact]
    public void Borda_do_mapa_segura_o_jogador_no_mundo_aberto()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var player = world.AddPlayer("Ana", 0);
        player.Movement.Teleport(WorldBounds.MaxX - 0.5f, 0, 0);

        world.Handle(player.Id, new MoveInput { Sequence = 1, X = WorldBounds.MaxX + 2f, Z = 0 }, 1);

        Assert.Equal(WorldBounds.MaxX, player.X, 3);
        Assert.Contains(outbox.Sent, s => s.PlayerId == player.Id && s.Message is PositionCorrection);
        Assert.All(WorldContent.Spawns(), s => Assert.True(WorldBounds.Inside(s.CenterX, s.CenterZ), s.Data.Id));
    }

    [Fact]
    public void Sentado_na_fogueira_recupera_tudo_e_levanta_ao_andar()
    {
        var world = new GameWorld(new FakeOutbox(), NoMonsters);
        var player = world.AddPlayer("Ana", 0);
        player.Hp.Add(-player.Hp.Max / 2);
        player.Mp.Add(-player.Mp.Current);
        player.Sp.Add(-player.Sp.Current);
        var (hp, mp) = (player.Hp.Current, player.Mp.Current);

        world.Handle(player.Id, new SetResting { Resting = true }, 1);
        Assert.True(player.Resting);
        for (var t = 1.25; t <= 3; t += 0.25) world.Tick(t);

        Assert.True(player.Hp.Current - hp >= player.Hp.Max * RestRules.HpFractionPerSecond * 1.5f);
        Assert.True(player.Mp.Current - mp >= player.Mp.Max * RestRules.MpFractionPerSecond * 1.5f);
        Assert.True(player.Sp.Current >= RestRules.SpPerSecond * 1.5f);

        world.Handle(player.Id, new MoveInput { Sequence = 1, X = player.X + 1, Z = player.Z }, 3.5);
        Assert.False(player.Resting);

        // Longe da fogueira, não senta.
        player.Movement.Teleport(30, 30, 4);
        world.Handle(player.Id, new SetResting { Resting = true }, 4);
        Assert.False(player.Resting);
    }

    [Fact]
    public void Nome_vazio_ou_longo_eh_ajustado()
    {
        var world = new GameWorld(new FakeOutbox(), NoMonsters);

        Assert.Equal("Aventureiro", world.AddPlayer("   ", 0).Name);
        Assert.Equal(20, world.AddPlayer(new string('x', 50), 0).Name.Length);
    }
}
