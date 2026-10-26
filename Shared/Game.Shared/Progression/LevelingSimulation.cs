using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Items;
using Game.Shared.Monsters;
using Game.Shared.World;

namespace Game.Shared.Progression
{
    /// <summary>Um nível da simulação: onde caçou, quanto tempo levou e o ritmo.</summary>
    public sealed class LevelStep
    {
        public int Level;
        public string MonsterId = "";
        public int MonsterLevel;
        public double SecondsPerKill;
        public int XpPerKill;
        public int Kills;
        public long QuestXp;

        /// <summary>Alz e itens das missões entregues neste nível (para a revisão da economia).</summary>
        public long QuestAlz;
        public List<(string ItemId, int Quantity)> QuestItems = new List<(string, int)>();
        public double Minutes;
    }

    /// <summary>Premissas de um jogador "típico" (nem perfeito, nem perdido). Números para discutir no balanceamento.</summary>
    public sealed class LevelingAssumptions
    {
        /// <summary>Tempo entre um monstro e outro: procurar, andar até ele, pegar o drop.</summary>
        public double SecondsBetweenKills = 7;

        /// <summary>Tempo de uma ação (skill + animação): ninguém aperta skills sem parar.</summary>
        public double SecondsPerAction = 0.9;

        /// <summary>Fração do tempo parado (descansar, vila, inventário, ferreira).</summary>
        public double Downtime = 0.20;

        /// <summary>Bônus médio de dano pelo combo depois de liberado (nem todo combate usa combo).</summary>
        public double ComboBonus = 0.15;

        /// <summary>Melhoria média do equipamento por grau (Ferro, Aço Negro, Astralita).</summary>
        public int[] UpgradeByGrade = { 2, 4, 5 };

        /// <summary>Tempo extra de cada missão (voltar à vila, falar com NPCs) em minutos.</summary>
        public double QuestOverheadMinutes = 3;
    }

    /// <summary>
    /// Simula um personagem subindo do nível 1 ao 30 com as regras reais: atributos no atributo principal da classe,
    /// equipamento do grau do nível, skills liberadas por nível e missão, dano pela fórmula do jogo contra a defesa
    /// do monstro, XP ajustada pela diferença de nível e as missões do catálogo. Escolhe sempre o monstro que dá
    /// mais XP por segundo entre os de nível próximo. Serve para conferir a meta do GDD (1→30 em 8–12 horas).
    /// </summary>
    public static class LevelingSimulation
    {
        public static List<LevelStep> Run(CharacterClass characterClass, LevelingAssumptions? assumptions = null)
        {
            assumptions ??= new LevelingAssumptions();
            var monsters = WorldContent.Spawns().Where(s => !s.IsBoss).Select(s => s.Data).ToList();
            var steps = new List<LevelStep>();
            var completed = new HashSet<string>();

            for (var level = 1; level < LevelCurve.MaxLevel; level++)
            {
                // Missões disponíveis neste nível: a XP delas conta, e os objetivos já entram no tempo de caça.
                long questXp = 0;
                long questAlz = 0;
                var questItems = new List<(string ItemId, int Quantity)>();
                var questMinutes = 0.0;
                foreach (var quest in QuestCatalog.All)
                {
                    // As diárias ficam de fora: são um bônus para quem volta todo dia.
                    if (quest.Daily || completed.Contains(quest.Id) || quest.MinLevel > level) continue;
                    if (!string.IsNullOrEmpty(quest.PreviousQuestId) && !completed.Contains(quest.PreviousQuestId)) continue;
                    completed.Add(quest.Id);
                    questXp += quest.RewardXp;
                    questAlz += quest.RewardAlz;
                    questItems.AddRange(quest.RewardItems);
                    questMinutes += assumptions.QuestOverheadMinutes;
                }

                var stats = StatsAt(characterClass, level, assumptions);
                var dps = DamagePerSecond(characterClass, level, stats, completed, assumptions);
                LevelStep? best = null;
                foreach (var monster in monsters)
                {
                    // Até 5 níveis acima (o limite do bônus de XP) e até 6 abaixo.
                    if (monster.Level < level - 6 || monster.Level > level + 5) continue;
                    var xp = XpRules.Adjusted(monster.XpReward, level, monster.Level);
                    var kill = monster.MaxHp / dps(monster) + assumptions.SecondsBetweenKills;
                    if (best == null || xp / kill > best.XpPerKill / best.SecondsPerKill)
                        best = new LevelStep { MonsterId = monster.Id, MonsterLevel = monster.Level, SecondsPerKill = kill, XpPerKill = xp };
                }
                if (best == null) throw new InvalidOperationException($"Nenhum monstro para o nível {level}.");

                var needed = Math.Max(0, LevelCurve.XpToNext(level) - questXp);
                best.Level = level;
                best.QuestXp = questXp;
                best.QuestAlz = questAlz;
                best.QuestItems = questItems;
                best.Kills = (int)Math.Ceiling(needed / (double)best.XpPerKill);
                best.Minutes = best.Kills * best.SecondsPerKill / 60 / (1 - assumptions.Downtime) + questMinutes;
                steps.Add(best);
            }
            return steps;
        }

        public static double TotalHours(IEnumerable<LevelStep> steps) => steps.Sum(s => s.Minutes) / 60;

        /// <summary>
        /// Dano por segundo de um personagem típico do nível contra um monstro (equipamento do grau, missões do
        /// caminho feitas). Usado também para estimar o tempo da dungeon.
        /// </summary>
        public static double DpsAgainst(CharacterClass characterClass, int level, MonsterData monster, LevelingAssumptions? assumptions = null)
        {
            assumptions ??= new LevelingAssumptions();
            var completed = new HashSet<string>(QuestCatalog.All.Where(q => q.MinLevel <= level).Select(q => q.Id));
            var stats = StatsAt(characterClass, level, assumptions);
            return DamagePerSecond(characterClass, level, stats, completed, assumptions)(monster);
        }

        /// <summary>Atributos finais: pontos no atributo principal e equipamento completo do grau do nível.</summary>
        public static StatBlock StatsAt(CharacterClass characterClass, int level, LevelingAssumptions assumptions)
        {
            var attributes = Attributes.StartingFor(characterClass);
            var points = Attributes.PointsPerLevel * (level - 1);
            if (characterClass == CharacterClass.Arcanist)
            {
                attributes.Intelligence += points * 3 / 5;
                attributes.Dexterity += points - points * 3 / 5;
            }
            else
            {
                attributes.Strength += points * 3 / 5;
                attributes.Dexterity += points - points * 3 / 5;
            }

            var grade = level >= 20 ? 3 : level >= 10 ? 2 : 1;
            var suffix = grade == 3 ? "astralita" : grade == 2 ? "aco_negro" : "ferro";
            var weapon = ClassInfo.WeaponPrefix(characterClass) + suffix;
            var equipment = new Equipment();
            var upgrade = assumptions.UpgradeByGrade[grade - 1];
            long uid = 0;
            foreach (var id in new[] { weapon, "armadura_" + suffix, "luvas_" + suffix, "botas_" + suffix, "anel_" + suffix })
            {
                var definition = ItemCatalog.Get(id);
                equipment.Put(definition.Slot, new ItemInstance { Uid = ++uid, ItemId = id, Upgrade = upgrade });
            }
            return StatCalculator.Compute(level, attributes, equipment);
        }

        /// <summary>
        /// Dano por segundo contra um monstro: a cada ação usa a skill liberada de maior dano que não está em recarga
        /// e que o MP permite. Simula 120 s para pegar o ritmo das recargas.
        /// </summary>
        private static Func<MonsterData, double> DamagePerSecond(CharacterClass characterClass, int level, StatBlock stats,
            HashSet<string> completed, LevelingAssumptions assumptions)
        {
            var skills = ClassCatalog.UnlockedSkills(characterClass, level, completed.Contains).ToList();
            var combo = ClassCatalog.ComboUnlocked(completed.Contains) ? 1 + assumptions.ComboBonus : 1;
            var crit = 1 + Math.Min(0.4, stats.CritPercent / 100.0) * (PlayerRules.CritMultiplier - 1);

            return monster =>
            {
                var mitigation = 1 - DamageFormula.Mitigation(monster.Defense);
                var readyAt = new double[skills.Count];
                var mp = (double)stats.MaxMp;
                var damage = 0.0;
                const double duration = 120;
                for (var t = 0.0; t < duration; t += assumptions.SecondsPerAction)
                {
                    mp = Math.Min(stats.MaxMp, mp + PlayerRules.MpRegenPerSecond * assumptions.SecondsPerAction);
                    var choice = -1;
                    var bestHit = 0.0;
                    for (var i = 0; i < skills.Count; i++)
                    {
                        if (readyAt[i] > t || skills[i].MpCost > mp) continue;
                        var hit = skills[i].BaseDamage + ClassCatalog.SkillPower(skills[i], stats);
                        if (hit > bestHit)
                        {
                            bestHit = hit;
                            choice = i;
                        }
                    }
                    if (choice < 0) continue;
                    readyAt[choice] = t + skills[choice].CooldownSeconds;
                    mp -= skills[choice].MpCost;
                    damage += bestHit;
                }
                return damage / duration * mitigation * crit * combo;
            };
        }
    }
}
