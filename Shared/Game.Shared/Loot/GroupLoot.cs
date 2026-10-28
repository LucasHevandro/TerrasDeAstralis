using System.Collections.Generic;
using System.Linq;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Items;

namespace Game.Shared.Loot
{
    /// <summary>Resposta de um membro no sorteio do item do grupo.</summary>
    public enum LootChoice : byte
    {
        Pass = 0,

        /// <summary>Quero: qualquer um pode.</summary>
        Greed = 1,

        /// <summary>Preciso: só quem pode usar o item (vence qualquer "Quero").</summary>
        Need = 2,
    }

    /// <summary>
    /// Loot em grupo, no mundo e nas dungeons. Item comum vai para um membro por vez (rodízio entre os vivos perto do
    /// monstro), já no nome dele: ninguém corre para pegar. Raro ou melhor vai a sorteio: cada um escolhe Preciso, Quero
    /// ou Passar; Preciso vence Quero e, dentro da mesma escolha, ganha o dado mais alto (1 a 100). Todos passando, o item
    /// segue o rodízio. O baú do fim da dungeon continua com os sorteios de cada um (não passa por aqui).
    /// </summary>
    public static class GroupLoot
    {
        /// <summary>Raridade a partir da qual o item vai a sorteio.</summary>
        public const ItemRarity RollRarity = ItemRarity.Rare;

        /// <summary>Tempo para escolher (quem não responde passa).</summary>
        public const float RollSeconds = 20f;

        public static bool NeedsRoll(ItemDefinition item) => !item.IsQuestItem && item.Rarity >= RollRarity;

        /// <summary>Pode escolher "Preciso": arma da própria classe, ou item que serve para todos.</summary>
        public static bool CanNeed(ItemDefinition item, CharacterClass characterClass) =>
            item.RequiredClass == null || item.RequiredClass == characterClass;

        /// <summary>
        /// Resolve o sorteio. Quem não respondeu conta como Passar. Nulo se todos passaram. Os dados de quem participou
        /// vão em <see cref="RollResult.Rolls"/> (para o chat).
        /// </summary>
        public static RollResult? Resolve(IReadOnlyDictionary<int, LootChoice> answers, IRandomSource random)
        {
            var playing = answers.Where(a => a.Value != LootChoice.Pass).ToList();
            if (playing.Count == 0) return null;
            var rolls = playing.Select(a => (PlayerId: a.Key, Choice: a.Value, Roll: 1 + (int)(random.NextDouble() * 100))).ToList();
            var best = rolls.Max(r => r.Choice);
            var top = rolls.Where(r => r.Choice == best).Max(r => r.Roll);
            var tied = rolls.Where(r => r.Choice == best && r.Roll == top).ToList();
            var winner = tied[tied.Count == 1 ? 0 : (int)(random.NextDouble() * tied.Count) % tied.Count]; // empate: sorteia
            return new RollResult(winner.PlayerId, winner.Choice, winner.Roll, rolls);
        }
    }

    public sealed class RollResult
    {
        public int WinnerId { get; }
        public LootChoice Choice { get; }
        public int Roll { get; }
        public IReadOnlyList<(int PlayerId, LootChoice Choice, int Roll)> Rolls { get; }

        public RollResult(int winnerId, LootChoice choice, int roll, IReadOnlyList<(int PlayerId, LootChoice Choice, int Roll)> rolls)
        {
            WinnerId = winnerId;
            Choice = choice;
            Roll = roll;
            Rolls = rolls;
        }
    }
}
