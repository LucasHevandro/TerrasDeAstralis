using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Net;
using Game.Shared.Progression;

namespace Game.Server.Tests;

/// <summary>Mestre de skills: aprender pagando (nível e missão de rank como requisito) e a barra de atalhos arrumada.</summary>
public class SkillTrainingTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));
        public List<string> Notices(int playerId) =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<Notice>().Select(n => n.Text.ToString()).ToList();
    }

    private long _uid = 1000;

    private (GameWorld World, FakeOutbox Outbox, ServerPlayer Player) Setup(CharacterRecord? record = null)
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, Array.Empty<Game.Shared.World.SpawnDefinition>(), null, new ItemUidAllocator(5000));
        record ??= AuthService.InitialCharacter(CharacterClass.Blade, () => ++_uid) with { Id = 7, AccountId = 1, Name = "Ana" };
        var player = world.AddPlayer("", 0, null, record);
        return (world, outbox, player);
    }

    /// <summary>Barra com as primeiras casas dadas e o resto vazio.</summary>
    private static byte[] Bar(params byte[] first) =>
        first.Concat(Enumerable.Repeat(SkillTraining.Empty, SkillTraining.HotbarSize - first.Length)).ToArray();

    private static void GoToTrainer(ServerPlayer player)
    {
        var npc = NpcCatalog.Find(NpcCatalog.Trainer)!;
        player.Movement.Teleport(npc.X, npc.Z - 1, 0);
    }

    [Fact]
    public void Personagem_novo_sabe_so_as_skills_de_nivel_1()
    {
        var (_, _, player) = Setup();
        Assert.Equal(new[] { "corte" }, player.LearnedSkills);
        Assert.Equal(Bar(0), player.Hotbar);

        // Subir de nível não ensina nada: precisa ir ao mestre.
        player.Progress = new CharacterProgress(10, 0);
        Assert.False(player.SkillUnlocked(1));
    }

    [Fact]
    public void Aprende_no_mestre_pagando_e_a_skill_vai_para_a_barra()
    {
        var (world, outbox, player) = Setup();
        player.Progress = new CharacterProgress(3, 0);

        world.Handle(player.Id, new LearnSkill { SkillId = "giro_de_lamina" }, 1);
        Assert.False(player.SkillUnlocked(1)); // longe do mestre

        GoToTrainer(player);
        world.Handle(player.Id, new LearnSkill { SkillId = "giro_de_lamina" }, 2);
        Assert.False(player.SkillUnlocked(1)); // sem Alz
        Assert.Contains(outbox.Notices(player.Id), n => n.Contains("Alz"));

        player.Alz = 1000;
        world.Handle(player.Id, new LearnSkill { SkillId = "giro_de_lamina" }, 3);
        Assert.True(player.SkillUnlocked(1));
        Assert.Equal(1000 - SkillTraining.PriceFor(player.ClassSkills[1]), player.Alz);
        Assert.Equal(1, player.Hotbar[1]);
        Assert.Contains(player.PendingLogs, l => l.Kind == "skill_aprendida" && l.Details == "giro_de_lamina");

        // Golpe Rúnico é do nível 5.
        world.Handle(player.Id, new LearnSkill { SkillId = "golpe_runico" }, 4);
        Assert.False(player.SkillUnlocked(2));
    }

    [Fact]
    public void Skill_de_rank_pede_a_missao()
    {
        var (world, _, player) = Setup();
        player.Progress = new CharacterProgress(12, 0);
        player.Alz = 1_000_000;
        GoToTrainer(player);

        world.Handle(player.Id, new LearnSkill { SkillId = "fenda_sismica" }, 1);
        Assert.False(player.SkillUnlocked(4));

        player.Quests.Restore(new[] { ClassCatalog.RankTwoQuestId }, Array.Empty<QuestProgress>());
        world.Handle(player.Id, new LearnSkill { SkillId = "fenda_sismica" }, 2);
        Assert.True(player.SkillUnlocked(4));
    }

    [Fact]
    public void Barra_aceita_so_skills_aprendidas_sem_repetir()
    {
        var (world, _, player) = Setup();
        player.LearnedSkills.Add("giro_de_lamina");

        // Golpe Rúnico (2) não foi aprendido e o Corte (0) aparece duas vezes.
        world.Handle(player.Id, new SetHotbar { Slots = { 255, 1, 0, 2, 0, 255, 255, 255, 255, 255, 255, 255, 1 } }, 1);
        Assert.Equal(Bar(255, 1, 0), player.Hotbar);
    }

    [Fact]
    public void Skills_e_barra_voltam_do_banco_e_quem_e_de_antes_nao_perde_nada()
    {
        var (world, _, player) = Setup();
        player.LearnedSkills.Add("giro_de_lamina");
        player.Hotbar = Bar(1, 255, 0);
        var (snapshot, _) = world.CaptureForSave(player);
        Assert.Equal(new[] { "corte", "giro_de_lamina" }, snapshot.LearnedSkills);

        var record = AuthService.InitialCharacter(CharacterClass.Blade) with
        {
            Id = 9, Name = "Bia", LearnedSkills = snapshot.LearnedSkills, Hotbar = snapshot.Hotbar,
        };
        var (_, _, back) = Setup(record);
        Assert.Equal(Bar(1, 255, 0), back.Hotbar);

        // De antes do mestre (sem a lista): nível 8 fica com as 4 skills que o nível já liberava.
        var old = AuthService.InitialCharacter(CharacterClass.Blade) with { Id = 10, Name = "Caio", Level = 8 };
        var (_, _, veteran) = Setup(old);
        Assert.Equal(4, veteran.LearnedSkills.Count);
        Assert.Equal(Bar(0, 1, 2, 3), veteran.Hotbar);
    }

    [Fact]
    public void Pocao_de_vida_vai_para_a_barra_e_volta_do_banco()
    {
        var (world, _, player) = Setup();
        var small = SkillTraining.ItemValue("pocao_hp_p");
        var medium = SkillTraining.ItemValue("pocao_hp_m");
        Assert.NotEqual(SkillTraining.Empty, small);

        // Poção repetida sai; valor de item que não existe (230) também.
        world.Handle(player.Id, new SetHotbar { Slots = { 0, small, medium, small, 230 } }, 1);
        Assert.Equal(Bar(0, small, medium), player.Hotbar);

        var (snapshot, _) = world.CaptureForSave(player);
        Assert.Equal("item:pocao_hp_p", snapshot.Hotbar[1]);
        var record = AuthService.InitialCharacter(CharacterClass.Blade) with { Id = 9, Name = "Bia", Hotbar = snapshot.Hotbar };
        var (_, _, back) = Setup(record);
        Assert.Equal(Bar(0, small, medium), back.Hotbar);
    }
}
