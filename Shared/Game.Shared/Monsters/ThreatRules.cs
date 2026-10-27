using System;
using Game.Shared.Accounts;

namespace Game.Shared.Monsters
{
    /// <summary>
    /// Ameaça: cada monstro guarda quanto cada jogador o irritou e persegue quem tem mais. O dano conta vezes o fator da
    /// classe (a Lâmina, que aguenta apanhar, puxa o dobro), e o monstro só troca de alvo quando alguém passa bem à frente
    /// do alvo atual: assim ele não fica indo e voltando entre dois jogadores. A provocação (Giro de Lâmina) põe a Lâmina
    /// acima de todos de uma vez. A tabela zera quando o monstro volta para casa ou morre.
    /// </summary>
    public static class ThreatRules
    {
        /// <summary>Quanto o dano de cada classe vira ameaça.</summary>
        public static double ClassFactor(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Blade => 2.0,
            CharacterClass.Arcanist => 0.8,
            _ => 1.0,
        };

        /// <summary>Quem está perto do monstro troca o alvo ao passar 110% da ameaça dele; quem está longe, 130%.</summary>
        public const double MeleeSwitch = 1.1;
        public const double RangedSwitch = 1.3;

        /// <summary>Até onde (do monstro) conta como "perto" para a troca de alvo.</summary>
        public const float MeleeRange = 4f;

        /// <summary>Skill de provocação da Lâmina: fica acima do maior da tabela por esta margem.</summary>
        public const string TauntSkillId = "giro_de_lamina";
        public const double TauntMargin = 1.2;

        /// <summary>Ameaça de quem puxou a matilha (os lobos chamados começam irritados com ele).</summary>
        public const double PackPullThreat = 1;

        /// <summary>Ameaça de um golpe (mesmo um golpe de 0, no escudo do chefe, irrita um pouco).</summary>
        public static double ForHit(int damage, CharacterClass characterClass) => Math.Max(1, damage) * ClassFactor(characterClass);

        /// <summary>
        /// O desafiante tira o alvo atual? Só se a ameaça dele passar da do atual vezes a margem (menor se ele estiver
        /// perto do monstro).
        /// </summary>
        public static bool Overtakes(double challenger, double current, bool challengerInMelee) =>
            challenger > current * (challengerInMelee ? MeleeSwitch : RangedSwitch);
    }
}
