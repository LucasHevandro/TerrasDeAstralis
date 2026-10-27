using System;
using System.Collections.Generic;
using Game.Shared.Progression;

namespace Game.Shared.Social
{
    /// <summary>Regras do grupo (party). Números para discutir no balanceamento.</summary>
    public static class PartyRules
    {
        public const int MaxMembers = 4;

        /// <summary>Quanto tempo um convite fica valendo.</summary>
        public const double InviteSeconds = 30;

        /// <summary>Distância do monstro até onde um membro recebe a parte do XP, do Alz e conta a missão.</summary>
        public const float ShareRadius = 50f;

        /// <summary>
        /// Bônus de grupo: o XP total do monstro cresce 15% por membro a mais e é dividido entre os que estão perto.
        /// Sozinho = 100%; em 4 = 145% dividido por 4. Como o grupo mata bem mais rápido, cada um sobe mais rápido.
        /// </summary>
        public const double BonusPerExtraMember = 0.15;

        /// <summary>XP de cada membro perto, já ajustado pela diferença de nível de cada um.</summary>
        public static int SharedXp(int baseXp, int monsterLevel, int memberLevel, int membersSharing)
        {
            var count = Math.Max(1, membersSharing);
            var pool = baseXp * (1 + BonusPerExtraMember * (count - 1));
            return XpRules.Adjusted((int)Math.Round(pool / count), memberLevel, monsterLevel);
        }

        /// <summary>Divide o Alz em partes iguais; o resto vai para o primeiro (quem matou).</summary>
        public static long[] SplitAlz(long alz, int membersSharing)
        {
            var count = Math.Max(1, membersSharing);
            var parts = new long[count];
            var each = Math.Max(0, alz) / count;
            for (var i = 0; i < count; i++) parts[i] = each;
            parts[0] += Math.Max(0, alz) - each * count;
            return parts;
        }
    }

    /// <summary>Amigos, bloqueados e denúncias.</summary>
    public static class RelationRules
    {
        public const int MaxFriends = 50;

        /// <summary>Tempo para aceitar um convite de amizade.</summary>
        public const float FriendInviteSeconds = 60f;
        public const int MaxBlocked = 100;
        public const int MaxReportReason = 200;

        /// <summary>Quantas mensagens recentes do denunciado vão junto com a denúncia.</summary>
        public const int ReportChatLines = 10;

        /// <summary>Denunciar a mesma pessoa de novo só depois deste tempo.</summary>
        public const double SameTargetCooldownSeconds = 600;

        /// <summary>No máximo esta quantidade de denúncias por hora (evita usar a denúncia como spam).</summary>
        public const int MaxReportsPerHour = 5;
    }

    /// <summary>
    /// Um grupo: membros na ordem em que entraram e o líder. Só a regra; quem convida, avisa e manda mensagens é o servidor.
    /// </summary>
    public sealed class Party
    {
        private readonly List<int> _members = new List<int>();

        public Party(int id, int leaderId)
        {
            Id = id;
            LeaderId = leaderId;
            _members.Add(leaderId);
        }

        public int Id { get; }
        public int LeaderId { get; private set; }
        public IReadOnlyList<int> Members => _members;
        public bool IsFull => _members.Count >= PartyRules.MaxMembers;
        public bool Contains(int playerId) => _members.Contains(playerId);

        public bool TryAdd(int playerId)
        {
            if (IsFull || Contains(playerId)) return false;
            _members.Add(playerId);
            return true;
        }

        /// <summary>Tira o membro. Se era o líder, o próximo mais antigo vira líder. Retorna falso se não estava no grupo.</summary>
        public bool Remove(int playerId)
        {
            if (!_members.Remove(playerId)) return false;
            if (LeaderId == playerId && _members.Count > 0) LeaderId = _members[0];
            return true;
        }

        public bool TryPromote(int playerId)
        {
            if (!Contains(playerId)) return false;
            LeaderId = playerId;
            return true;
        }

        /// <summary>Grupo de um só não existe: o servidor desfaz.</summary>
        public bool ShouldDisband => _members.Count < 2;
    }
}
