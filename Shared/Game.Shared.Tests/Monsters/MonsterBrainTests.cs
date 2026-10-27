using System.Numerics;
using Game.Shared.Combat;
using Game.Shared.Monsters;

namespace Game.Shared.Tests.Monsters;

public class MonsterBrainTests
{
    private static MonsterData Wolf(bool aggressive = true, float wander = 0) => new()
    {
        Id = "lobo",
        AttackRange = 1.5f,
        AttackCooldownSeconds = 2f,
        AttackWindupSeconds = 0.5f,
        Aggressive = aggressive,
        AggroRadius = 6f,
        LeashRadius = 20f,
        WanderRadius = wander,
    };

    private static MonsterBrain Brain(MonsterData data) => new(data, Vector2.Zero, new SystemRandomSource(1));

    [Fact]
    public void Agressivo_persegue_quem_entra_no_raio()
    {
        var brain = Brain(Wolf());

        var action = brain.Tick(0, Vector2.Zero, new Vector2(5, 0));

        Assert.Equal(MonsterState.Chase, brain.State);
        Assert.Equal(MonsterActionKind.MoveTo, action.Kind);
        Assert.Equal(new Vector2(5, 0), action.Point);
    }

    [Fact]
    public void Com_tempo_de_reacao_so_vira_e_ataca_se_o_alvo_ficar_perto()
    {
        var data = Wolf();
        data.NoticeSeconds = 1f;
        var brain = Brain(data);

        // Notou: vira para o alvo, sem sair do lugar.
        Assert.Equal(MonsterActionKind.Face, brain.Tick(0, Vector2.Zero, new Vector2(5, 0)).Kind);
        Assert.Equal(MonsterActionKind.Face, brain.Tick(0.5, Vector2.Zero, new Vector2(5, 0)).Kind);
        Assert.NotEqual(MonsterState.Chase, brain.State);

        // Saiu do raio: esquece. Voltou: conta de novo.
        brain.Tick(0.8, Vector2.Zero, new Vector2(9, 0));
        Assert.Equal(MonsterActionKind.Face, brain.Tick(1.2, Vector2.Zero, new Vector2(5, 0)).Kind);
        Assert.Equal(MonsterActionKind.MoveTo, brain.Tick(2.3, Vector2.Zero, new Vector2(5, 0)).Kind);
        Assert.Equal(MonsterState.Chase, brain.State);

        // Quem bate não espera: provocado persegue na hora.
        var hit = Brain(data);
        hit.Provoke();
        hit.Tick(0, Vector2.Zero, new Vector2(5, 0));
        Assert.Equal(MonsterState.Chase, hit.State);
    }

    [Fact]
    public void Ignora_quem_esta_fora_do_raio()
    {
        var brain = Brain(Wolf());

        var action = brain.Tick(0, Vector2.Zero, new Vector2(10, 0));

        Assert.Equal(MonsterState.Idle, brain.State);
        Assert.Equal(MonsterActionKind.None, action.Kind);
    }

    [Fact]
    public void Passivo_so_ataca_depois_de_provocado()
    {
        var brain = Brain(Wolf(aggressive: false));

        brain.Tick(0, Vector2.Zero, new Vector2(1, 0));
        Assert.Equal(MonsterState.Idle, brain.State);

        brain.Provoke();
        brain.Tick(0.1, Vector2.Zero, new Vector2(15, 0));
        Assert.Equal(MonsterState.Chase, brain.State);
    }

    [Fact]
    public void No_alcance_prepara_o_golpe_e_ele_sai_depois_do_preparo()
    {
        var brain = Brain(Wolf());
        var player = new Vector2(1, 0);

        var start = brain.Tick(10, Vector2.Zero, player);
        Assert.Equal(MonsterActionKind.AttackStarted, start.Kind);
        Assert.Equal(MonsterState.WindUp, brain.State);
        Assert.Equal(10.5, brain.WindupEndsAt, 6);

        Assert.Equal(MonsterActionKind.Face, brain.Tick(10.3, Vector2.Zero, player).Kind);

        var landed = brain.Tick(10.5, Vector2.Zero, player);
        Assert.Equal(MonsterActionKind.AttackLanded, landed.Kind);
        Assert.Equal(MonsterState.Chase, brain.State);
    }

    [Fact]
    public void Respeita_a_recarga_entre_golpes()
    {
        var brain = Brain(Wolf());
        var player = new Vector2(1, 0);
        brain.Tick(10, Vector2.Zero, player);
        brain.Tick(10.5, Vector2.Zero, player); // golpe sai; próximo só em 12,5

        Assert.Equal(MonsterActionKind.Face, brain.Tick(12.0, Vector2.Zero, player).Kind);
        Assert.Equal(MonsterActionKind.AttackStarted, brain.Tick(12.5, Vector2.Zero, player).Kind);
    }

    [Fact]
    public void Longe_demais_de_casa_volta_e_recupera_a_vida()
    {
        var brain = Brain(Wolf());
        brain.Tick(0, Vector2.Zero, new Vector2(5, 0));

        var action = brain.Tick(1, new Vector2(21, 0), new Vector2(23, 0));
        Assert.Equal(MonsterState.Return, brain.State);
        Assert.Equal(MonsterActionKind.MoveTo, action.Kind);
        Assert.Equal(Vector2.Zero, action.Point);

        // Voltando, ignora o jogador mesmo perto.
        Assert.Equal(MonsterActionKind.MoveTo, brain.Tick(2, new Vector2(3, 0), new Vector2(3.5f, 0)).Kind);

        Assert.Equal(MonsterActionKind.ResetHealth, brain.Tick(3, new Vector2(0.1f, 0), new Vector2(10, 0)).Kind);
        Assert.Equal(MonsterState.Idle, brain.State);
    }

    [Fact]
    public void Sem_alvo_desiste_e_volta()
    {
        var brain = Brain(Wolf());
        brain.Tick(0, Vector2.Zero, new Vector2(5, 0));

        brain.Tick(1, new Vector2(4, 0), null);

        Assert.Equal(MonsterState.Return, brain.State);
    }

    [Fact]
    public void Passeia_perto_de_casa()
    {
        var brain = Brain(Wolf(wander: 4));

        brain.Revive(0);
        Assert.Equal(MonsterActionKind.None, brain.Tick(1, Vector2.Zero, null).Kind);

        var action = brain.Tick(2.5, Vector2.Zero, null);
        Assert.Equal(MonsterState.Wander, brain.State);
        Assert.Equal(MonsterActionKind.MoveTo, action.Kind);
        Assert.True(action.Point.Length() <= 4.0001f);
    }

    [Fact]
    public void Morto_nao_faz_nada_e_renasce_parado()
    {
        var brain = Brain(Wolf());
        brain.Kill();

        Assert.Equal(MonsterActionKind.None, brain.Tick(0, Vector2.Zero, new Vector2(1, 0)).Kind);
        brain.Provoke();
        Assert.Equal(MonsterState.Dead, brain.State);

        brain.Revive(5);
        Assert.Equal(MonsterState.Idle, brain.State);
    }

    [Fact]
    public void Dados_invalidos_sao_rejeitados()
    {
        Assert.Throws<ArgumentException>(() => new MonsterData { Id = "" }.Validate());
        Assert.Throws<ArgumentException>(() => new MonsterData { Id = "x", MaxHp = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new MonsterData { Id = "x", LeashRadius = 1, AttackRange = 2 }.Validate());
        Wolf().Validate();
    }
}
