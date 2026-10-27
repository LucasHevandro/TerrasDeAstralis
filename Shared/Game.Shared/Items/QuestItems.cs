using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Combat;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Progression;

namespace Game.Shared.Items
{
    /// <summary>
    /// Itens de missão: as missões de coleta pedem a versão "de missão" de um material (missao_pele_lobo, e não
    /// pele_lobo). Ela só cai para quem tem missão ativa que ainda precisa dela, cada um recebe a sua (em grupo também),
    /// não vende nem troca, e sai da bolsa quando nenhuma missão ativa pede mais. O material de venda continua caindo
    /// para todos, como antes.
    /// </summary>
    public static class QuestItems
    {
        public const string Prefix = "missao_";

        /// <summary>Materiais que têm versão de missão (os que aparecem em objetivos de coleta).</summary>
        public static readonly IReadOnlyList<string> Materials = new[]
        {
            "pelo_macio", "pele_lobo", "presa_lobo", "couro_javali", "seda", "veneno", "fragmento_pedra", "essencia",
        };

        /// <summary>Chance mínima de cair para quem precisa (o material raro não deixa a missão arrastada).</summary>
        public const double MinChance = 0.35;

        public static string For(string material) => Prefix + material;

        /// <summary>Quanto ainda falta de cada item de missão, somando todas as missões ativas.</summary>
        public static Dictionary<string, int> Needed(QuestLog log, ItemContainer bag)
        {
            var wanted = new Dictionary<string, int>();
            foreach (var progress in log.Active)
            foreach (var objective in QuestCatalog.Get(progress.QuestId).Objectives)
            {
                if (objective.Kind != QuestObjectiveKind.Collect || ItemCatalog.Find(objective.TargetId) is not { IsQuestItem: true }) continue;
                wanted[objective.TargetId] = wanted.TryGetValue(objective.TargetId, out var n) ? n + objective.Count : objective.Count;
            }
            return wanted
                .Select(w => (w.Key, Missing: w.Value - bag.Count(w.Key)))
                .Where(w => w.Missing > 0)
                .ToDictionary(w => w.Key, w => w.Missing);
        }

        /// <summary>
        /// Itens de missão que este monstro derruba para este jogador: os que ele ainda precisa e que o monstro tem na
        /// tabela (pelo material). A quantidade nunca passa do que falta.
        /// </summary>
        public static List<ItemDrop> Roll(MonsterData monster, QuestLog log, ItemContainer bag, IRandomSource random)
        {
            var drops = new List<ItemDrop>();
            foreach (var (itemId, missing) in Needed(log, bag))
            {
                var definition = ItemCatalog.Get(itemId);
                var entry = monster.Drops.Entries.FirstOrDefault(e => e.ItemId == definition.QuestMaterialId);
                if (entry == null || random.NextDouble() >= Math.Max(entry.Chance, MinChance)) continue;
                var quantity = entry.MaxQuantity <= entry.MinQuantity
                    ? entry.MinQuantity
                    : entry.MinQuantity + (int)Math.Floor(random.NextDouble() * (entry.MaxQuantity - entry.MinQuantity + 1));
                quantity = Math.Min(Math.Max(1, quantity), missing);
                drops.Add(new ItemDrop(itemId, definition.Name, definition.Rarity, quantity));
            }
            return drops;
        }

        /// <summary>
        /// Tira da bolsa os itens de missão que nenhuma missão ativa pede mais (depois de entregar ou desistir).
        /// Retorna se mexeu na bolsa.
        /// </summary>
        public static bool Cleanup(QuestLog log, ItemContainer bag)
        {
            var wanted = new Dictionary<string, int>();
            foreach (var progress in log.Active)
            foreach (var objective in QuestCatalog.Get(progress.QuestId).Objectives)
                if (objective.Kind == QuestObjectiveKind.Collect)
                    wanted[objective.TargetId] = (wanted.TryGetValue(objective.TargetId, out var n) ? n : 0) + objective.Count;
            var changed = false;
            foreach (var material in Materials)
            {
                var id = For(material);
                var extra = bag.Count(id) - (wanted.TryGetValue(id, out var keep) ? keep : 0);
                if (extra > 0 && bag.TryRemove(id, extra)) changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Personagem de antes dos itens de missão, no meio de uma coleta: o material que ele já juntou vira item de
        /// missão (até o que a missão pede), para ninguém perder o progresso.
        /// </summary>
        public static bool ConvertMaterials(QuestLog log, ItemContainer bag, Func<long> newUid)
        {
            var changed = false;
            foreach (var (itemId, missing) in Needed(log, bag))
            {
                var material = ItemCatalog.Get(itemId).QuestMaterialId;
                var move = Math.Min(missing, bag.Count(material));
                if (move <= 0 || !bag.TryRemove(material, move)) continue;
                if (!bag.TryAdd(itemId, move, newUid))
                {
                    bag.TryAdd(material, move, newUid); // sem espaço: devolve o material
                    continue;
                }
                changed = true;
            }
            return changed;
        }
    }
}
