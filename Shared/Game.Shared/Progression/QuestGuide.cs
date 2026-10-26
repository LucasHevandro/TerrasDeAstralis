using System.Collections.Generic;
using System.Linq;
using Game.Shared.World;

namespace Game.Shared.Progression
{
    /// <summary>Para onde ir numa missão: o NPC de quem falar ou o lugar onde está o monstro.</summary>
    public readonly struct QuestDestination
    {
        public readonly float X;
        public readonly float Z;

        /// <summary>NPC do destino (vazio quando o destino é um grupo de monstros).</summary>
        public readonly string NpcId;

        /// <summary>Monstro do destino (vazio quando o destino é um NPC).</summary>
        public readonly string MonsterId;

        public QuestDestination(float x, float z, string npcId, string monsterId)
        {
            X = x;
            Z = z;
            NpcId = npcId;
            MonsterId = monsterId;
        }
    }

    /// <summary>
    /// Guia das missões: diz onde fica o próximo passo, para o painel de missões mostrar o destino e a distância.
    /// Sem isso o jogador só via o nome da missão e precisava adivinhar onde caçar ou com quem falar.
    /// </summary>
    public static class QuestGuide
    {
        private static readonly List<SpawnDefinition> Spawns = WorldContent.Spawns();

        /// <summary>
        /// Próximo destino: o NPC que recebe a missão quando tudo está feito; senão, o primeiro objetivo por fazer.
        /// Monstro que não está no mundo aberto (chefe de dungeon) leva ao portal das dungeons. Nulo se não há lugar
        /// conhecido (item que só cai em dungeon, por exemplo).
        /// </summary>
        public static QuestDestination? For(QuestDefinition quest, IReadOnlyList<int> counts)
        {
            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                if (i < counts.Count && counts[i] >= objective.Count) continue;
                switch (objective.Kind)
                {
                    case QuestObjectiveKind.Talk:
                        return AtNpc(objective.TargetId);
                    case QuestObjectiveKind.Kill:
                        var hunt = Spawns.FirstOrDefault(s => s.Data.Id == objective.TargetId);
                        return hunt != null
                            ? new QuestDestination(hunt.CenterX, hunt.CenterZ, "", hunt.Data.Id)
                            : AtNpc(NpcCatalog.DungeonPortal);
                    default:
                        var source = Spawns.FirstOrDefault(s => s.Data.Drops.Entries.Any(e => e.ItemId == objective.TargetId));
                        return source != null ? new QuestDestination(source.CenterX, source.CenterZ, "", source.Data.Id) : (QuestDestination?)null;
                }
            }
            return AtNpc(quest.GiverNpcId);
        }

        private static QuestDestination? AtNpc(string npcId) =>
            NpcCatalog.Find(npcId) is { } npc ? new QuestDestination(npc.X, npc.Z, npc.Id, "") : (QuestDestination?)null;
    }
}
