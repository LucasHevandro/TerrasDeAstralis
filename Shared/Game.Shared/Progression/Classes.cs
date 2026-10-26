using System;
using System.Collections.Generic;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Items;

namespace Game.Shared.Progression
{
    /// <summary>Skill de uma classe com o que é preciso para liberá-la.</summary>
    public sealed class ClassSkill
    {
        public SkillData Skill { get; }
        public int RequiredLevel { get; }

        /// <summary>Missão de rank que libera a skill (vazio = só o nível).</summary>
        public string RequiredQuestId { get; }

        public ClassSkill(SkillData skill, int requiredLevel, string requiredQuestId = "")
        {
            Skill = skill;
            RequiredLevel = requiredLevel;
            RequiredQuestId = requiredQuestId;
        }

        public bool IsUnlocked(int level, Func<string, bool> questCompleted) =>
            level >= RequiredLevel && (string.IsNullOrEmpty(RequiredQuestId) || questCompleted(RequiredQuestId));
    }

    /// <summary>
    /// As três classes: 8 skills cada, liberadas por nível e pelas missões de rank, e uma rota de combo
    /// própria. Números provisórios até a planilha.
    /// </summary>
    public static class ClassCatalog
    {
        /// <summary>Missão que libera o combo (GDD: "rank de skill que libera o combo").</summary>
        public const string ComboQuestId = "arte_do_combo";

        /// <summary>Missões de rank que liberam as skills de nível médio e alto.</summary>
        public const string RankTwoQuestId = "ameaca_na_mata";
        public const string RankThreeQuestId = "as_ruinas";

        public const int HotbarSize = 8;

        /// <summary>
        /// Poder que soma no dano: skills físicas usam o ataque; mágicas usam o maior entre o poder mágico e o ataque.
        /// Sem isso, os golpes rúnicos da Lâmina (que tem pouca Inteligência) rendiam menos que o Corte e nunca valiam a
        /// pena; para o Arcanista nada muda, porque o poder mágico dele é sempre o maior.
        /// </summary>
        public static int PowerFor(SkillKind kind, StatBlock stats) =>
            kind == SkillKind.Magical ? Math.Max(stats.MagicPower, stats.AttackPower) : stats.AttackPower;

        /// <summary>Skills já liberadas, na ordem da barra.</summary>
        public static IEnumerable<SkillData> UnlockedSkills(CharacterClass characterClass, int level, Func<string, bool> questCompleted)
        {
            foreach (var skill in SkillsFor(characterClass))
                if (skill.IsUnlocked(level, questCompleted)) yield return skill.Skill;
        }

        /// <summary>A skill pode ser usada por este personagem agora?</summary>
        public static bool CanUse(CharacterClass characterClass, string skillId, int level, Func<string, bool> questCompleted)
        {
            foreach (var skill in SkillsFor(characterClass))
                if (skill.Skill.Id == skillId) return skill.IsUnlocked(level, questCompleted);
            return false;
        }

        /// <summary>O combo só fica disponível depois da missão de rank que o libera.</summary>
        public static bool ComboUnlocked(Func<string, bool> questCompleted) => questCompleted(ComboQuestId);

        public static IReadOnlyList<ClassSkill> SkillsFor(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Arcanist => Arcanist,
            CharacterClass.Ranger => Ranger,
            _ => Blade,
        };

        /// <summary>
        /// Rota de combo exclusiva da classe (soma-se às rotas gerais). As do Arcanista e da Ranger fecham só com o
        /// ataque básico, então valem menos que a da Lâmina, que pede o Golpe Rúnico no meio.
        /// </summary>
        public static ComboRoute RouteFor(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Arcanist => new ComboRoute("sobrecarga", 0.10, SkillKind.Magical, SkillKind.Magical, SkillKind.Magical),
            CharacterClass.Ranger => new ComboRoute("rajada_certeira", 0.10, SkillKind.Physical, SkillKind.Physical, SkillKind.Physical),
            _ => new ComboRoute("lamina_runica", 0.20, SkillKind.Physical, SkillKind.Magical, SkillKind.Physical),
        };

        /// <summary>Configuração do combo da classe: rotas gerais + a rota da classe.</summary>
        public static ComboConfig ComboConfigFor(CharacterClass characterClass, double graceSeconds = 0)
        {
            var config = new ComboConfig { GraceSeconds = graceSeconds };
            config.Routes.Add(RouteFor(characterClass));
            return config;
        }

        private static readonly IReadOnlyList<ClassSkill> Blade = new List<ClassSkill>
        {
            new ClassSkill(new SkillData("corte", SkillKind.Physical, 120, 2.5f, 0.5f, castSeconds: 0.45f), 1),
            new ClassSkill(new SkillData("giro_de_lamina", SkillKind.Physical, 90, 2.5f, 2.0f, areaRadius: 3.5f, castSeconds: 0.55f), 3),
            new ClassSkill(new SkillData("golpe_runico", SkillKind.Magical, 140, 3f, 1.5f, mpCost: 25, castSeconds: 0.6f), 5),
            new ClassSkill(new SkillData("estocada_dupla", SkillKind.Physical, 200, 2.5f, 3f, mpCost: 20, castSeconds: 0.75f), 8),
            new ClassSkill(new SkillData("fenda_sismica", SkillKind.Physical, 260, 3f, 6f, areaRadius: 4f, mpCost: 40, castSeconds: 0.85f), 12, RankTwoQuestId),
            new ClassSkill(new SkillData("lamina_flamejante", SkillKind.Magical, 300, 3f, 4f, mpCost: 50, castSeconds: 0.55f), 15, RankTwoQuestId),
            new ClassSkill(new SkillData("tempestade_de_aco", SkillKind.Physical, 380, 3f, 10f, areaRadius: 5f, mpCost: 80, castSeconds: 1.25f), 20, RankThreeQuestId),
            new ClassSkill(new SkillData("execucao", SkillKind.Physical, 600, 2.5f, 15f, mpCost: 100, castSeconds: 0.95f), 25, RankThreeQuestId),
        };

        private static readonly IReadOnlyList<ClassSkill> Arcanist = new List<ClassSkill>
        {
            new ClassSkill(new SkillData("faisca", SkillKind.Magical, 110, 12f, 0.5f, mpCost: 8, castSeconds: 0.45f), 1),
            new ClassSkill(new SkillData("lanca_de_forca", SkillKind.Magical, 160, 12f, 1.5f, mpCost: 30, castSeconds: 0.6f), 1),
            new ClassSkill(new SkillData("golpe_de_cajado", SkillKind.Physical, 100, 2.5f, 1f, castSeconds: 0.55f), 3),
            new ClassSkill(new SkillData("explosao_arcana", SkillKind.Magical, 220, 10f, 5f, areaRadius: 4f, mpCost: 80, castSeconds: 0.75f), 5),
            new ClassSkill(new SkillData("corrente_gelida", SkillKind.Magical, 280, 12f, 4f, mpCost: 60, castSeconds: 0.85f), 12, RankTwoQuestId),
            new ClassSkill(new SkillData("meteoro", SkillKind.Magical, 380, 12f, 8f, areaRadius: 5f, mpCost: 120, castSeconds: 1.0f), 15, RankTwoQuestId),
            new ClassSkill(new SkillData("raio_duplo", SkillKind.Magical, 450, 12f, 6f, mpCost: 100, castSeconds: 0.7f), 20, RankThreeQuestId),
            new ClassSkill(new SkillData("cataclismo", SkillKind.Magical, 700, 12f, 20f, areaRadius: 6f, mpCost: 200, castSeconds: 1.5f), 25, RankThreeQuestId),
        };

        /// <summary>Ranger: flechas físicas de longe (o ataque vem do arco e da Força) e um chute para afastar quem chega perto.</summary>
        private static readonly IReadOnlyList<ClassSkill> Ranger = new List<ClassSkill>
        {
            new ClassSkill(new SkillData("tiro_certeiro", SkillKind.Physical, 110, 12f, 0.5f, castSeconds: 0.45f), 1),
            new ClassSkill(new SkillData("flecha_perfurante", SkillKind.Physical, 160, 12f, 1.5f, mpCost: 25, castSeconds: 0.6f), 1),
            new ClassSkill(new SkillData("chute_de_recuo", SkillKind.Physical, 100, 2.5f, 1f, castSeconds: 0.5f), 3),
            new ClassSkill(new SkillData("chuva_de_flechas", SkillKind.Physical, 210, 11f, 5f, areaRadius: 4f, mpCost: 70, castSeconds: 0.8f), 5),
            new ClassSkill(new SkillData("tiro_duplo", SkillKind.Physical, 270, 12f, 4f, mpCost: 55, castSeconds: 0.75f), 12, RankTwoQuestId),
            new ClassSkill(new SkillData("flecha_flamejante", SkillKind.Physical, 360, 12f, 7f, areaRadius: 3f, mpCost: 100, castSeconds: 0.9f), 15, RankTwoQuestId),
            new ClassSkill(new SkillData("saraivada", SkillKind.Physical, 430, 11f, 8f, areaRadius: 5f, mpCost: 110, castSeconds: 1.1f), 20, RankThreeQuestId),
            new ClassSkill(new SkillData("tiro_do_falcao", SkillKind.Physical, 680, 14f, 18f, mpCost: 180, castSeconds: 1.35f), 25, RankThreeQuestId),
        };
    }

    /// <summary>
    /// Modo Despertar (GDD v0.1): uma barra que enche com acertos de combo bem feitos — premia a habilidade, não só
    /// bater — e, cheia, pode ser ativada por alguns segundos de dano extra.
    /// </summary>
    public sealed class AwakeningState
    {
        public const int MaxGauge = 100;
        public const int ExcellentGain = 10;
        public const int GoodGain = 4;
        public const double DurationSeconds = 10;
        public const double DamageBonus = 0.30;

        public int Gauge { get; private set; }
        public double ActiveUntil { get; private set; } = double.NegativeInfinity;

        public bool IsActive(double now) => now < ActiveUntil;
        public bool IsFull => Gauge >= MaxGauge;

        /// <summary>Multiplicador de dano agora (1 = sem bônus).</summary>
        public double DamageMultiplier(double now) => IsActive(now) ? 1 + DamageBonus : 1;

        /// <summary>Soma pelo acerto do combo (nada enche enquanto o Despertar está ativo).</summary>
        public void AddFromGrade(ComboGrade grade, double now)
        {
            if (IsActive(now)) return;
            var gain = grade == ComboGrade.Excellent ? ExcellentGain : grade == ComboGrade.Good ? GoodGain : 0;
            Gauge = Math.Min(MaxGauge, Gauge + gain);
        }

        /// <summary>Ativa se a barra estiver cheia. Retorna falso se ainda não dá.</summary>
        public bool TryActivate(double now)
        {
            if (!IsFull || IsActive(now)) return false;
            Gauge = 0;
            ActiveUntil = now + DurationSeconds;
            return true;
        }

        /// <summary>Restaura do banco.</summary>
        public void Restore(int gauge) => Gauge = Math.Max(0, Math.Min(MaxGauge, gauge));
    }
}
