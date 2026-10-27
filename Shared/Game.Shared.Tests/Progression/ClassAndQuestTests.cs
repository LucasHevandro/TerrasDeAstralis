using System.Collections.Generic;
using System.Linq;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Dungeon;
using Game.Shared.Items;
using Game.Shared.Progression;
using Game.Shared.World;
using Xunit;

namespace Game.Shared.Tests.Progression;

public class ClassTests
{
    private static bool None(string _) => false;
    private static bool All(string _) => true;

    [Theory]
    [InlineData(CharacterClass.Blade)]
    [InlineData(CharacterClass.Arcanist)]
    [InlineData(CharacterClass.Ranger)]
    public void CadaClasseTemOitoSkillsUnicasEValidas(CharacterClass characterClass)
    {
        var skills = ClassCatalog.SkillsFor(characterClass);
        Assert.Equal(ClassCatalog.HotbarSize, skills.Count);
        Assert.Equal(skills.Count, skills.Select(s => s.Skill.Id).Distinct().Count());
        foreach (var s in skills)
        {
            Assert.True(s.Skill.BaseDamage > 0 && s.Skill.Range > 0 && s.Skill.CooldownSeconds > 0, s.Skill.Id);
            Assert.InRange(s.RequiredLevel, 1, LevelCurve.MaxLevel);
            if (!string.IsNullOrEmpty(s.RequiredQuestId)) Assert.NotNull(QuestCatalog.Find(s.RequiredQuestId));
        }
        // Ordenadas por nível de liberação (é a ordem da barra).
        Assert.Equal(skills.Select(s => s.RequiredLevel).OrderBy(l => l), skills.Select(s => s.RequiredLevel));
    }

    [Fact]
    public void ClassesTemIdentidadePropria()
    {
        var blade = ClassCatalog.SkillsFor(CharacterClass.Blade);
        var arcanist = ClassCatalog.SkillsFor(CharacterClass.Arcanist);
        Assert.True(blade.Count(s => s.Skill.Kind == SkillKind.Physical) > blade.Count / 2);
        Assert.True(arcanist.Count(s => s.Skill.Kind == SkillKind.Magical) > arcanist.Count / 2);
        Assert.True(arcanist.Average(s => s.Skill.Range) > blade.Average(s => s.Skill.Range)); // Arcanista ataca de longe
        Assert.NotEqual(ClassCatalog.RouteFor(CharacterClass.Blade).Name, ClassCatalog.RouteFor(CharacterClass.Arcanist).Name);

        // Ranger: física como a Lâmina, mas de longe como o Arcanista.
        var ranger = ClassCatalog.SkillsFor(CharacterClass.Ranger);
        Assert.True(ranger.Count(s => s.Skill.Kind == SkillKind.Physical) > ranger.Count / 2);
        Assert.True(ranger.Average(s => s.Skill.Range) > blade.Average(s => s.Skill.Range));
        Assert.Equal(3, ClassInfo.All.Select(c => ClassCatalog.RouteFor(c).Name).Distinct().Count());
    }

    [Fact]
    public void Classe_vai_e_volta_pelo_TypeId_e_comeca_com_a_propria_arma()
    {
        foreach (var characterClass in ClassInfo.All)
        {
            Assert.Equal(characterClass, ClassInfo.FromTypeId(ClassInfo.TypeId(characterClass)));
            var weapon = ItemCatalog.Get(ClassInfo.StarterWeapon(characterClass));
            Assert.Equal(characterClass, weapon.RequiredClass);
            Assert.Equal(1, weapon.RequiredLevel);
            Assert.StartsWith(ClassInfo.WeaponPrefix(characterClass), weapon.Id);
        }
    }

    [Fact]
    public void InicioTemAsSkillsDoNivelUm()
    {
        Assert.NotEmpty(ClassCatalog.UnlockedSkills(CharacterClass.Blade, 1, None));
        Assert.NotEmpty(ClassCatalog.UnlockedSkills(CharacterClass.Arcanist, 1, None));
        Assert.NotEmpty(ClassCatalog.UnlockedSkills(CharacterClass.Ranger, 1, None));
    }

    [Fact]
    public void SkillDeRankPedeNivelEMissao()
    {
        Assert.False(ClassCatalog.CanUse(CharacterClass.Blade, "fenda_sismica", 12, None));
        Assert.False(ClassCatalog.CanUse(CharacterClass.Blade, "fenda_sismica", 11, All));
        Assert.True(ClassCatalog.CanUse(CharacterClass.Blade, "fenda_sismica", 12, id => id == ClassCatalog.RankTwoQuestId));
        Assert.False(ClassCatalog.CanUse(CharacterClass.Blade, "faisca", 30, All)); // skill da outra classe
        Assert.Equal(8, ClassCatalog.UnlockedSkills(CharacterClass.Arcanist, 30, All).Count());
    }

    [Fact]
    public void ComboEsperaAMissao()
    {
        Assert.False(ClassCatalog.ComboUnlocked(None));
        Assert.True(ClassCatalog.ComboUnlocked(id => id == ClassCatalog.ComboQuestId));
    }

    [Fact]
    public void RotaDaClasseEntraNoCombo()
    {
        var config = ClassCatalog.ComboConfigFor(CharacterClass.Blade);
        config.Validate();
        Assert.Contains(config.Routes, r => r.Name == ClassCatalog.RouteFor(CharacterClass.Blade).Name);
        Assert.True(config.Routes.Count > ComboRoute.CreateDefaults().Count);
    }

    [Fact]
    public void SkillMagicaUsaPoderMagico()
    {
        var stats = new StatBlock { AttackPower = 100, MagicPower = 300 };
        Assert.Equal(100, ClassCatalog.PowerFor(SkillKind.Physical, stats));
        Assert.Equal(300, ClassCatalog.PowerFor(SkillKind.Magical, stats));
    }

    [Fact]
    public void GuiaDaMissaoApontaOProximoPasso()
    {
        var pele = QuestCatalog.Get("pele_e_presa"); // 12 lobos e 6 peles, entregue ao Ancião
        var lobos = WorldContent.Spawns().Single(s => s.Data.Id == "lobo_cinzento");

        var hunt = QuestGuide.For(pele, new[] { 3, 0 })!.Value;
        Assert.Equal(("lobo_cinzento", lobos.CenterX, lobos.CenterZ), (hunt.MonsterId, hunt.X, hunt.Z));

        var skins = QuestGuide.For(pele, new[] { 12, 2 })!.Value; // as peles caem dos lobos
        Assert.Equal("lobo_cinzento", skins.MonsterId);

        var deliver = QuestGuide.For(pele, new[] { 12, 6 })!.Value;
        Assert.Equal(NpcCatalog.Elder, deliver.NpcId);

        var talk = QuestGuide.For(QuestCatalog.Get("rastros_na_trilha"), new[] { 0 })!.Value;
        Assert.Equal(NpcCatalog.Scout, talk.NpcId);

        // Chefe de dungeon não está no mundo aberto: o caminho é o portal da vila.
        var crypt = QuestGuide.For(QuestCatalog.Get("a_cripta_esquecida"), new int[0])!.Value;
        Assert.Equal(NpcCatalog.DungeonPortal, crypt.NpcId);
    }

    [Theory]
    [InlineData(CharacterClass.Blade)]
    [InlineData(CharacterClass.Arcanist)]
    [InlineData(CharacterClass.Ranger)]
    public void EscalaDePoderCresceComONivelDaSkill(CharacterClass characterClass)
    {
        var skills = ClassCatalog.SkillsFor(characterClass);
        Assert.Equal(1f, skills[0].Skill.PowerScale); // o ataque básico é a referência
        // Entre as que custam MP (de alvo único e de área, separadas), a liberada depois nunca escala menos que a anterior.
        // As sem custo (Giro, Chute, Golpe de Cajado) são golpes de apoio e ficam fora.
        foreach (var area in new[] { false, true })
        {
            var ladder = skills.Skip(1).Where(s => s.Skill.MpCost > 0 && s.Skill.AreaRadius > 0 == area && s.Skill.Kind == skills[0].Skill.Kind).ToList();
            for (var i = 1; i < ladder.Count; i++)
                Assert.True(ladder[i].Skill.PowerScale >= ladder[i - 1].Skill.PowerScale,
                    $"{ladder[i].Skill.Id} ({ladder[i].Skill.PowerScale}) escala menos que {ladder[i - 1].Skill.Id} ({ladder[i - 1].Skill.PowerScale})");
        }
        var stats = new StatBlock { AttackPower = 500, MagicPower = 500 };
        var finale = skills[skills.Count - 1].Skill;
        Assert.Equal((int)Math.Round(500 * finale.PowerScale), ClassCatalog.SkillPower(finale, stats));
    }

    [Fact]
    public void GolpeMagicoDaLaminaUsaOAtaqueQuandoEleEhMaior()
    {
        // A Lâmina tem pouca Inteligência: o Golpe Rúnico não pode render menos que o Corte por usar o poder mágico.
        var stats = new StatBlock { AttackPower = 600, MagicPower = 250 };
        Assert.Equal(600, ClassCatalog.PowerFor(SkillKind.Magical, stats));
    }

    [Fact]
    public void RotaDaClasseQueFechaSoComOBasicoValeMenos()
    {
        Assert.Equal(0.20, ClassCatalog.RouteFor(CharacterClass.Blade).DamageBonus);
        Assert.Equal(0.10, ClassCatalog.RouteFor(CharacterClass.Arcanist).DamageBonus);
        Assert.Equal(0.10, ClassCatalog.RouteFor(CharacterClass.Ranger).DamageBonus);
    }

    [Fact]
    public void DespertarEncheComComboBemFeitoEDuraUmTempo()
    {
        var awakening = new AwakeningState();
        Assert.False(awakening.TryActivate(0));

        awakening.AddFromGrade(ComboGrade.Miss, 0);
        Assert.Equal(0, awakening.Gauge);
        for (var i = 0; i < AwakeningState.MaxGauge / AwakeningState.ExcellentGain; i++)
            awakening.AddFromGrade(ComboGrade.Excellent, 0);
        Assert.True(awakening.IsFull);

        Assert.True(awakening.TryActivate(1));
        Assert.Equal(0, awakening.Gauge);
        Assert.Equal(1 + AwakeningState.DamageBonus, awakening.DamageMultiplier(5));
        awakening.AddFromGrade(ComboGrade.Excellent, 5); // não enche enquanto está ativo
        Assert.Equal(0, awakening.Gauge);
        Assert.Equal(1, awakening.DamageMultiplier(1 + AwakeningState.DurationSeconds));
    }
}

public class QuestTests
{
    private long _uid;
    private long NewUid() => ++_uid;

    [Fact]
    public void CatalogoCobreDoUmAoTrintaEReferenciasExistem()
    {
        var quests = QuestCatalog.All;
        Assert.True(quests.Count >= 25, $"só {quests.Count} missões");
        Assert.Equal(quests.Count, quests.Select(q => q.Id).Distinct().Count());
        Assert.Equal(1, quests.First().MinLevel);
        Assert.Equal(LevelCurve.MaxLevel, quests.Max(q => q.MinLevel));

        var monsters = new HashSet<string>(WorldContent.Spawns().Select(s => s.Data.Id).Concat(DungeonCatalog.All().Select(d => d.BossId)));
        foreach (var quest in quests)
        {
            Assert.NotNull(NpcCatalog.Find(quest.GiverNpcId));
            if (!string.IsNullOrEmpty(quest.PreviousQuestId))
                Assert.True(QuestCatalog.Find(quest.PreviousQuestId)!.MinLevel <= quest.MinLevel, quest.Id);
            foreach (var (itemId, _) in quest.RewardItems) Assert.NotNull(ItemCatalog.Find(itemId));
            foreach (var o in quest.Objectives)
            {
                if (o.Kind == QuestObjectiveKind.Kill) Assert.Contains(o.TargetId, monsters);
                if (o.Kind == QuestObjectiveKind.Collect) Assert.NotNull(ItemCatalog.Find(o.TargetId));
                if (o.Kind == QuestObjectiveKind.Talk) Assert.NotNull(NpcCatalog.Find(o.TargetId));
            }
            // A missão ajuda, mas não substitui caçar: no máximo ~2 níveis de XP.
            Assert.True(quest.RewardXp <= 2 * LevelCurve.XpToNext(quest.MinLevel) || quest.MinLevel >= LevelCurve.MaxLevel, quest.Id);
        }
        Assert.NotNull(QuestCatalog.Find(ClassCatalog.ComboQuestId));
        Assert.NotNull(QuestCatalog.Find(ClassCatalog.RankTwoQuestId));
        Assert.NotNull(QuestCatalog.Find(ClassCatalog.RankThreeQuestId));
    }

    [Fact]
    public void NpcsFicamNaVilaOuNaRegiaoDasMissoes()
    {
        var village = WorldContent.Zones().First();
        foreach (var npc in NpcCatalog.All)
        {
            var inVillage = npc.X * npc.X + npc.Z * npc.Z < village.Radius * village.Radius;
            // Os de fora da vila dão missões e ficam dentro de uma região.
            Assert.True(inVillage || (npc.Role == NpcRole.QuestGiver && WorldContent.Zones().Any(z => (npc.X - z.CenterX) * (npc.X - z.CenterX) + (npc.Z - z.CenterZ) * (npc.Z - z.CenterZ) < z.Radius * z.Radius)), npc.Id);
        }
        Assert.True(NpcCatalog.InRange(NpcCatalog.All[0], NpcCatalog.All[0].X + 3, NpcCatalog.All[0].Z));
        Assert.False(NpcCatalog.InRange(NpcCatalog.All[0], NpcCatalog.All[0].X + 5, NpcCatalog.All[0].Z));
    }

    [Fact]
    public void AceitarRespeitaNivelEOrdem()
    {
        var log = new QuestLog();
        Assert.NotNull(log.Accept(ClassCatalog.ComboQuestId, 5)); // pede "primeiros_passos" antes
        Assert.Null(log.Accept("primeiros_passos", 1));
        Assert.NotNull(log.Accept("primeiros_passos", 1));        // já aceita
        Assert.DoesNotContain(log.AvailableFrom(NpcCatalog.Elder, 30), q => q.Id == "primeiros_passos");
    }

    [Fact]
    public void FalarEEntregarDaRecompensa()
    {
        var log = new QuestLog();
        var bag = new ItemContainer();
        log.Accept("primeiros_passos", 1);
        Assert.NotNull(log.TurnIn("primeiros_passos", bag, NewUid, out _));

        Assert.Single(log.OnTalk(NpcCatalog.Smith));
        Assert.Empty(log.OnTalk(NpcCatalog.Smith)); // já contou
        Assert.Null(log.TurnIn("primeiros_passos", bag, NewUid, out var reward));
        Assert.Equal(300, reward.Xp);
        Assert.Equal(5, bag.Count("pocao_hp_p"));
        Assert.True(log.IsCompleted("primeiros_passos"));
        Assert.NotNull(log.Accept("primeiros_passos", 1)); // não repete
    }

    [Fact]
    public void MatarEColetarContamEConsomem()
    {
        var log = Ready("pele_e_presa");
        var bag = new ItemContainer();
        for (var i = 0; i < 20; i++) log.OnKill("lobo_cinzento");
        Assert.Equal(12, log.ProgressOf("pele_e_presa")!.Counts[0]); // não passa do pedido
        Assert.False(QuestLog.IsReady(log.ProgressOf("pele_e_presa")!, bag));

        // A missão pede a pele de missão, não a de vender.
        bag.TryAdd("pele_lobo", 8, NewUid);
        Assert.False(QuestLog.IsReady(log.ProgressOf("pele_e_presa")!, bag));
        bag.TryAdd("missao_pele_lobo", 8, NewUid);
        Assert.True(QuestLog.IsReady(log.ProgressOf("pele_e_presa")!, bag));
        Assert.Null(log.TurnIn("pele_e_presa", bag, NewUid, out _));
        Assert.Equal(2, bag.Count("missao_pele_lobo"));
        Assert.Equal(8, bag.Count("pele_lobo"));
        Assert.Equal(3, bag.Count(ItemCatalog.CoreLow));
        Assert.Equal(1, bag.Count("pedra_vigor"));
    }

    [Fact]
    public void BolsaCheiaNaoPerdeNada()
    {
        var log = new QuestLog();
        log.Accept("primeiros_passos", 1);
        log.OnTalk(NpcCatalog.Smith);
        var bag = new ItemContainer(1);
        bag.TryAdd("espada_ferro", 1, NewUid);

        Assert.NotNull(log.TurnIn("primeiros_passos", bag, NewUid, out _));
        Assert.True(log.IsActive("primeiros_passos"));
        Assert.Equal(1, bag.Count("espada_ferro"));
        Assert.Equal(0, bag.Count("pocao_hp_p"));
    }

    [Fact]
    public void AbandonarLiberaParaAceitarDeNovo()
    {
        var log = new QuestLog();
        Assert.Null(log.Accept("primeiros_passos", 30));
        Assert.Single(log.Active);
        Assert.True(log.Abandon("primeiros_passos"));
        Assert.Empty(log.Active);
    }

    [Fact]
    public void RestaurarIgnoraMissoesDesconhecidas()
    {
        var log = new QuestLog();
        log.Restore(new[] { "primeiros_passos", "nao_existe" },
            new[] { new QuestProgress(ClassCatalog.ComboQuestId, new[] { 4 }), new QuestProgress("nao_existe") });
        Assert.Equal(new[] { "primeiros_passos" }, log.Completed);
        Assert.Equal(4, log.ProgressOf(ClassCatalog.ComboQuestId)!.Counts[0]);
        Assert.Single(log.Active);
    }

    /// <summary>Diário com as anteriores concluídas e a missão aceita.</summary>
    private static QuestLog Ready(string questId)
    {
        var quest = QuestCatalog.Get(questId);
        var previous = QuestCatalog.All.TakeWhile(q => q.Id != questId).Select(q => q.Id);
        var log = new QuestLog();
        log.Restore(previous, new QuestProgress[0]);
        Assert.Null(log.Accept(questId, quest.MinLevel));
        return log;
    }
}

public class LevelingBalanceTests
{
    [Theory]
    [InlineData(CharacterClass.Blade)]
    [InlineData(CharacterClass.Arcanist)]
    [InlineData(CharacterClass.Ranger)]
    public void Do_um_ao_trinta_leva_de_cinco_a_dez_horas(CharacterClass characterClass)
    {
        var steps = LevelingSimulation.Run(characterClass);
        var hours = LevelingSimulation.TotalHours(steps);

        // Com as missões das regiões (antes eram 8 a 12 h, só com a história principal). Desde 2026-10-25 (skills mais
        // fortes e monstros nas lacunas de nível) o simulador dá ~5,5 h; com disputa e pausas reais fica perto de 7 a 9 h.
        Assert.InRange(hours, 5, 10);
        Assert.Equal(LevelCurve.MaxLevel - 1, steps.Count);
        // Sem "muro": nenhum nível sozinho passa de 8% do tempo total, e todos têm um monstro de nível próximo.
        Assert.All(steps, s => Assert.True(s.Minutes <= hours * 60 * 0.08, $"nível {s.Level}: {s.Minutes:0} min de {hours * 60:0}"));
        Assert.All(steps, s => Assert.InRange(s.MonsterLevel, s.Level - 5, s.Level + 5));
    }

    [Fact]
    public void Jogador_rapido_ou_lento_continua_numa_faixa_razoavel()
    {
        var fast = LevelingSimulation.TotalHours(LevelingSimulation.Run(CharacterClass.Blade,
            new LevelingAssumptions { SecondsBetweenKills = 4, Downtime = 0.08, ComboBonus = 0.3 }));
        var slow = LevelingSimulation.TotalHours(LevelingSimulation.Run(CharacterClass.Blade,
            new LevelingAssumptions { SecondsBetweenKills = 10, Downtime = 0.3, ComboBonus = 0.05 }));
        Assert.InRange(fast, 3.5, 9);
        Assert.InRange(slow, 6, 16); // skills mais fortes e monstros nas lacunas (2026-10-25): o lento caiu para ~6,8 h
    }
}
