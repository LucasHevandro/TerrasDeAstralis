using System;
using Game.Shared.Loot;
using Game.Shared.Text;

namespace Game.Shared.Monsters
{
    /// <summary>Jeito de lutar de um tipo de monstro (a IA está no <see cref="MonsterBrain"/>; o servidor cuida da matilha e do golpe em área).</summary>
    public enum MonsterBehavior
    {
        /// <summary>Persegue e bate.</summary>
        Melee,

        /// <summary>Foge com pouca vida e volta depois (coelho).</summary>
        Skittish,

        /// <summary>Quando apanha, chama os da mesma espécie que estão perto (lobo).</summary>
        Pack,

        /// <summary>De longe, prepara e dispara uma investida em linha reta (touro).</summary>
        Charger,

        /// <summary>Bate e recua (aranha).</summary>
        HitAndRun,

        /// <summary>O golpe acerta todos em volta dele, com aviso no chão (golem).</summary>
        AreaSlam,

        /// <summary>Some e reaparece atrás do alvo quando ele se afasta (espectro).</summary>
        Blink,

        /// <summary>Com pouca vida fica furioso: bate e anda mais rápido (cavaleiro caído).</summary>
        Berserk,
    }

    /// <summary>Números dos comportamentos (iguais no cliente offline e no servidor).</summary>
    public static class MonsterBehaviors
    {
        /// <summary>Respiro no começo de cada luta antes da investida e do sumiço.</summary>
        public const float OpeningSeconds = 1.5f;

        public const float FleeAtHp = 0.3f;
        public const float FleeSeconds = 3.5f;
        public const float FleeDistance = 7f;
        public const float FleeSpeed = 1.3f;

        /// <summary>Lobos que entram na briga quando um apanha (era 12 m e puxava o bando inteiro).</summary>
        public const float PackCallRadius = 8f;

        /// <summary>Tempo de reação dos agressivos do mundo aberto (<see cref="MonsterData.NoticeSeconds"/>).</summary>
        public const float WorldNoticeSeconds = 1.0f;

        public const float ChargeMinDistance = 4.5f;
        public const float ChargeMaxDistance = 11f;
        public const float ChargeWindupSeconds = 0.9f;
        public const float ChargeSpeed = 3.2f;
        public const float ChargeOvershoot = 2.5f;
        public const float ChargeMaxSeconds = 1.4f;
        public const float ChargeCooldownSeconds = 7f;

        public const float RetreatSeconds = 1.1f;
        public const float RetreatDistance = 3.5f;
        public const float RetreatSpeed = 1.2f;

        public const float SlamRadius = 3.2f;

        public const float BlinkMinDistance = 6f;
        public const float BlinkLandDistance = 1.8f;
        public const float BlinkCooldownSeconds = 8f;

        public const float BerserkAtHp = 0.35f;
        public const float BerserkCooldownFactor = 0.55f;
        public const float BerserkSpeed = 1.25f;
    }

    /// <summary>Dados de um tipo de monstro. Campos públicos para Unity e JSON.</summary>
    [Serializable]
    public sealed class MonsterData
    {
        public string Id = "";

        /// <summary>Nome no idioma do jogador (chave <c>monster.&lt;id&gt;</c>).</summary>
        public string Name => Loc.T(Loc.Key("monster.", Id));

        public int Level = 1;
        public int MaxHp = 500;

        /// <summary>Dano base do ataque (entra na <see cref="Combat.DamageFormula"/> como o "dano da skill").</summary>
        public int AttackDamage = 40;

        public int AttackPower = 10;
        public int Defense = 30;

        /// <summary>Alcance do ataque, em metros até a borda do alvo.</summary>
        public float AttackRange = 1.5f;

        public float AttackCooldownSeconds = 2f;

        /// <summary>Tempo de preparo do golpe: a janela em que o jogador vê o aviso e pode esquivar.</summary>
        public float AttackWindupSeconds = 0.6f;

        public float MoveSpeed = 3f;

        /// <summary>Raio do corpo: o alcance das skills é medido até esta borda.</summary>
        public float BodyRadius = 0.5f;

        /// <summary>Agressivo ataca quem chegar perto; passivo só depois de apanhar.</summary>
        public bool Aggressive = true;

        public float AggroRadius = 7f;

        /// <summary>
        /// Agressivo que vê alguém no raio só se vira para ele; ataca se a pessoa continuar perto por esse tempo. Quem
        /// passa correndo não puxa o grupo todo. Provocado (apanhou) ataca na hora. 0 = ataca na hora (dungeon).
        /// </summary>
        public float NoticeSeconds;

        /// <summary>Distância máxima do ponto de origem antes de desistir e voltar.</summary>
        public float LeashRadius = 22f;

        /// <summary>Raio de passeio quando parado (0 = fica parado).</summary>
        public float WanderRadius = 4f;

        /// <summary>Jeito de lutar (ver <see cref="MonsterBehavior"/>).</summary>
        public MonsterBehavior Behavior = MonsterBehavior.Melee;

        public int XpReward = 20;
        public float RespawnSeconds = 8f;
        public DropTable Drops = new DropTable();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Monstro sem Id.", nameof(Id));
            if (Level < 1) throw new ArgumentException($"{Id}: nível inválido.", nameof(Level));
            if (MaxHp <= 0) throw new ArgumentException($"{Id}: HP deve ser maior que zero.", nameof(MaxHp));
            if (AttackRange <= 0 || AttackCooldownSeconds <= 0 || AttackWindupSeconds < 0)
                throw new ArgumentException($"{Id}: ataque inválido.", nameof(AttackRange));
            if (MoveSpeed <= 0) throw new ArgumentException($"{Id}: velocidade inválida.", nameof(MoveSpeed));
            if (AggroRadius < 0 || LeashRadius <= AttackRange || WanderRadius < 0)
                throw new ArgumentException($"{Id}: raios inválidos.", nameof(LeashRadius));
            Drops.Validate();
        }
    }
}
