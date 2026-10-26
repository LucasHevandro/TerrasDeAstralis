using System;
using System.Collections.Generic;
using Game.Shared.Combat;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Text;

namespace Game.Shared.World
{
    /// <summary>Região do mapa com nome e faixa de nível.</summary>
    public sealed class ZoneDefinition
    {
        public string Id = "";

        /// <summary>Nome no idioma do jogador (chave <c>zone.&lt;id&gt;</c>).</summary>
        public string Name => Loc.T(Loc.Key("zone.", Id));

        public int MinLevel;
        public int MaxLevel;
        public float CenterX;
        public float CenterZ;
        public float Radius;
    }

    /// <summary>Grupo de monstros iguais em volta de um ponto.</summary>
    public sealed class SpawnDefinition
    {
        public MonsterData Data = new MonsterData();
        public float CenterX;
        public float CenterZ;
        public int Count = 1;
        public float Radius;
        public bool IsBoss;

        /// <summary>
        /// Chefe de mundo com horário (<see cref="WorldBossRules"/>): em vez de renascer depois de um tempo, desperta nas
        /// horas marcadas, com aviso para o servidor inteiro, e todo mundo que bateu nele ganha o crédito.
        /// </summary>
        public bool Scheduled;

        /// <summary>
        /// Pontos de nascimento, sempre os mesmos para a mesma semente (o servidor e as ferramentas concordam).
        /// </summary>
        public List<(float X, float Z)> SpawnPoints(int seed)
        {
            var random = new Random(seed);
            var points = new List<(float, float)>(Count);
            for (var i = 0; i < Count; i++)
            {
                var angle = i / (double)Math.Max(1, Count) * Math.PI * 2 + (random.NextDouble() - 0.5) * 0.6;
                var distance = Count == 1 ? 0 : Radius * (0.4 + 0.6 * random.NextDouble());
                points.Add(((float)(CenterX + Math.Cos(angle) * distance), (float)(CenterZ + Math.Sin(angle) * distance)));
            }
            return points;
        }
    }

    /// <summary>
    /// Conteúdo do mapa da Fase 2: zonas, monstros e drops. Fonte única para o servidor e para o construtor
    /// da cena do cliente. Números provisórios até a planilha de design.
    /// Cada chamada cria objetos novos (o chefe altera os próprios dados durante a luta).
    /// </summary>
    public static class WorldContent
    {
        public const float PlayerSpawnX = 0f;
        public const float PlayerSpawnZ = 0f;

        public static List<ZoneDefinition> Zones() => new List<ZoneDefinition>
        {
            new ZoneDefinition { Id = "vila", MinLevel = 1, MaxLevel = 30, CenterX = 0, CenterZ = 0, Radius = 16 },
            new ZoneDefinition { Id = "campos_do_vento", MinLevel = 1, MaxLevel = 10, CenterX = 0, CenterZ = 40, Radius = 24 },
            new ZoneDefinition { Id = "mata_sombria", MinLevel = 10, MaxLevel = 20, CenterX = 45, CenterZ = 85, Radius = 26 },
            new ZoneDefinition { Id = "ruinas_esquecidas", MinLevel = 20, MaxLevel = 30, CenterX = -45, CenterZ = 125, Radius = 26 },
            new ZoneDefinition { Id = "arena_do_guardiao", MinLevel = 30, MaxLevel = 30, CenterX = 0, CenterZ = 170, Radius = 18 },
        };

        public static List<SpawnDefinition> Spawns() => new List<SpawnDefinition>
        {
            // Campos do Vento (Nv 1–10)
            Spawn(-10, 36, 10, 10, new MonsterData
            {
                Id = "coelho_saltador", Level = 2, MaxHp = 700, AttackDamage = 25,
                AttackPower = 5, Defense = 20, AttackRange = 1.2f, MoveSpeed = 3.5f, BodyRadius = 0.35f, Aggressive = false,
                Behavior = MonsterBehavior.Skittish,
                XpReward = 30, RespawnSeconds = 6,
                Drops = new DropTable(3, 8,
                    new DropEntry("pelo_macio", ItemRarity.Common, 0.6, 1, 2),
                    new DropEntry("pocao_hp_p", ItemRarity.Common, 0.15),
                    Equip("armadura_ferro", 0.02), Equip("anel_ferro", 0.01)),
            }),
            Spawn(10, 46, 8, 9, new MonsterData
            {
                Id = "lobo_cinzento", Level = 6, MaxHp = 1800, AttackDamage = 60,
                AttackPower = 15, Defense = 40, AttackRange = 1.5f, MoveSpeed = 4.5f, AggroRadius = 8, Behavior = MonsterBehavior.Pack,
                XpReward = 70, RespawnSeconds = 8,
                Drops = new DropTable(10, 25,
                    new DropEntry("pele_lobo", ItemRarity.Common, 0.5, 1, 2),
                    new DropEntry("presa_lobo", ItemRarity.Uncommon, 0.15),
                    new DropEntry("nucleo_baixo", ItemRarity.Rare, 0.03),
                    Equip("luvas_ferro", 0.03), Equip("botas_ferro", 0.03), Equip("espada_ferro", 0.02),
                    Equip("cajado_ferro", 0.02), Equip("arco_ferro", 0.02), Equip("pedra_vigor", 0.01)),
            }),

            // Mata Sombria (Nv 10–20)
            Spawn(38, 90, 8, 9, new MonsterData
            {
                Id = "javali_furioso", Level = 12, MaxHp = 4400, AttackDamage = 120,
                AttackPower = 40, Defense = 90, AttackRange = 1.6f, AttackWindupSeconds = 0.7f, MoveSpeed = 5f, Behavior = MonsterBehavior.Charger,
                AggroRadius = 9, XpReward = 200, RespawnSeconds = 10,
                Drops = new DropTable(30, 70,
                    new DropEntry("couro_javali", ItemRarity.Common, 0.5, 1, 3),
                    new DropEntry("nucleo_baixo", ItemRarity.Rare, 0.06),
                    Equip("armadura_aco_negro", 0.02), Equip("botas_aco_negro", 0.03), Equip("pedra_furia", 0.01)),
            }),
            Spawn(55, 97, 8, 9, new MonsterData
            {
                Id = "aranha_da_mata", Level = 16, MaxHp = 5200, AttackDamage = 150,
                AttackPower = 55, Defense = 80, AttackRange = 2f, AttackWindupSeconds = 0.5f, MoveSpeed = 4f, BodyRadius = 0.8f,
                Behavior = MonsterBehavior.HitAndRun,
                AggroRadius = 10, XpReward = 300, RespawnSeconds = 10,
                Drops = new DropTable(40, 90,
                    new DropEntry("seda", ItemRarity.Common, 0.5, 1, 2),
                    new DropEntry("veneno", ItemRarity.Uncommon, 0.2),
                    new DropEntry("nucleo_medio", ItemRarity.Rare, 0.04),
                    Equip("luvas_aco_negro", 0.03), Equip("anel_aco_negro", 0.02), Equip("espada_aco_negro", 0.015),
                    Equip("cajado_aco_negro", 0.015), Equip("arco_aco_negro", 0.015)),
            }),

            // Ruínas Esquecidas (Nv 20–30)
            Spawn(-40, 128, 6, 8, new MonsterData
            {
                Id = "golem_de_pedra", Level = 22, MaxHp = 12000, AttackDamage = 240,
                AttackPower = 80, Defense = 260, AttackRange = 2.2f, AttackCooldownSeconds = 3f,
                AttackWindupSeconds = 1.0f, MoveSpeed = 2.2f, BodyRadius = 0.8f, AggroRadius = 7, XpReward = 700, RespawnSeconds = 15,
                Behavior = MonsterBehavior.AreaSlam,
                Drops = new DropTable(80, 160,
                    new DropEntry("fragmento_pedra", ItemRarity.Common, 0.6, 1, 3),
                    new DropEntry("nucleo_medio", ItemRarity.Rare, 0.08),
                    Equip("armadura_astralita", 0.015), Equip("botas_astralita", 0.02), Equip("pedra_precisao", 0.01)),
            }),
            Spawn(-58, 122, 8, 8, new MonsterData
            {
                Id = "espectro", Level = 26, MaxHp = 9000, AttackDamage = 280,
                AttackPower = 100, Defense = 120, AttackRange = 2.5f, AttackCooldownSeconds = 1.6f,
                AttackWindupSeconds = 0.45f, MoveSpeed = 5.5f, AggroRadius = 11, XpReward = 900, RespawnSeconds = 12,
                Behavior = MonsterBehavior.Blink,
                Drops = new DropTable(100, 200,
                    new DropEntry("essencia", ItemRarity.Uncommon, 0.4),
                    new DropEntry("nucleo_alto", ItemRarity.Rare, 0.05),
                    Equip("luvas_astralita", 0.02), Equip("anel_astralita", 0.015), Equip("espada_astralita", 0.01),
                    Equip("cajado_astralita", 0.01), Equip("arco_astralita", 0.01)),
            }),

            Spawn(-30, 140, 6, 7, new MonsterData
            {
                Id = "cavaleiro_caido", Level = 29, MaxHp = 14000, AttackDamage = 330,
                AttackPower = 120, Defense = 230, AttackRange = 2.4f, AttackCooldownSeconds = 2.2f,
                AttackWindupSeconds = 0.7f, MoveSpeed = 4f, BodyRadius = 0.7f, AggroRadius = 9, XpReward = 1400, RespawnSeconds = 15,
                Behavior = MonsterBehavior.Berserk,
                Drops = new DropTable(150, 300,
                    new DropEntry("essencia", ItemRarity.Uncommon, 0.3),
                    new DropEntry("nucleo_alto", ItemRarity.Rare, 0.07),
                    Equip("armadura_astralita", 0.02), Equip("espada_astralita", 0.012), Equip("cajado_astralita", 0.012), Equip("arco_astralita", 0.012),
                    Equip("pedra_precisao", 0.015)),
            }),

            // Chefe de campo
            new SpawnDefinition
            {
                CenterX = 0, CenterZ = 170, Count = 1, Radius = 0, IsBoss = true, Scheduled = true,
                Data = new MonsterData
                {
                    Id = "guardiao_das_ruinas", Level = 30, MaxHp = 60000,
                    AttackDamage = 380, AttackPower = 140, Defense = 280, AttackRange = 3.2f,
                    AttackCooldownSeconds = 2.4f, AttackWindupSeconds = 0.9f, MoveSpeed = 3.5f, BodyRadius = 1.1f, AggroRadius = 14,
                    LeashRadius = 30, WanderRadius = 0, XpReward = 15000, RespawnSeconds = 120,
                    Drops = new DropTable(2000, 4000,
                        new DropEntry("nucleo_alto", ItemRarity.Rare, 0.6, 1, 3),
                        new DropEntry("fragmento_guardiao", ItemRarity.Epic, 0.25),
                        Equip("espada_astralita", 0.25), Equip("cajado_astralita", 0.25), Equip("arco_astralita", 0.25),
                        Equip("armadura_astralita", 0.3)),
                },
            },
        };

        /// <summary>Drop de um item do catálogo (nome e raridade vêm dele).</summary>
        private static DropEntry Equip(string itemId, double chance)
        {
            var item = ItemCatalog.Get(itemId);
            return new DropEntry(itemId, item.Rarity, chance);
        }

        private static SpawnDefinition Spawn(float x, float z, int count, float radius, MonsterData data) =>
            new SpawnDefinition { CenterX = x, CenterZ = z, Count = count, Radius = radius, Data = data };
    }

    /// <summary>Habilidades do chefe (fase 2 e ataque em área), iguais no cliente offline e no servidor.</summary>
    public static class BossRules
    {
        public const float PhaseTwoAtHpFraction = 0.5f;
        public const float PhaseTwoCooldownFactor = 0.6f;
        public const float AreaIntervalSeconds = 6f;
        public const float AreaWarningSeconds = 1.6f;
        public const float AreaRadius = 4f;
        public const int AreaDamage = 350;

        /// <summary>Terceira fase (desespero): dois golpes em área por vez e com menos intervalo.</summary>
        public const float PhaseThreeAtHpFraction = 0.25f;

        public const float PhaseThreeIntervalFactor = 0.6f;

        /// <summary>O segundo círculo cai a esta distância do primeiro, para quem fugir pelo caminho mais óbvio.</summary>
        public const float PhaseThreeSecondAreaOffset = 4.5f;
    }

    /// <summary>
    /// Horário do chefe de mundo: desperta de <see cref="IntervalHours"/> em <see cref="IntervalHours"/> horas, nas horas
    /// pares de Brasília (0h, 2h, 4h...), com aviso <see cref="WarningMinutes"/> minutos antes. Cliente e servidor calculam
    /// do mesmo jeito (o /chefe do chat não precisa perguntar ao servidor).
    /// </summary>
    public static class WorldBossRules
    {
        public const int IntervalHours = 2;
        public const int WarningMinutes = 10;
        private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

        /// <summary>O último horário de despertar até <paramref name="now"/> (inclusive).</summary>
        public static DateTimeOffset CurrentSlot(DateTimeOffset now)
        {
            var local = now.ToOffset(Brasilia);
            var hour = local.Hour - local.Hour % IntervalHours;
            return new DateTimeOffset(local.Year, local.Month, local.Day, hour, 0, 0, Brasilia);
        }

        /// <summary>O próximo horário de despertar depois de <paramref name="now"/>.</summary>
        public static DateTimeOffset NextSlot(DateTimeOffset now) => CurrentSlot(now).AddHours(IntervalHours);
    }

    /// <summary>
    /// Monstro elite: ao nascer ou renascer no mundo aberto, um monstro comum tem <see cref="Chance"/> de vir mais forte
    /// (mais vida e dano) e de pagar mais (XP, Alz e uma rolagem de drop a mais). Chefes e monstros de dungeon nunca são elite.
    /// </summary>
    public static class EliteRules
    {
        public const double Chance = 0.05;
        public const int HpMultiplier = 4;
        public const float DamageMultiplier = 1.5f;
        public const int XpMultiplier = 4;
        public const int AlzMultiplier = 3;
        public const int ExtraDropRolls = 1;

        /// <summary>Só monstros comuns do mundo aberto (instância 0) podem ser elite.</summary>
        public static bool CanBeElite(SpawnDefinition spawn, int instanceId) =>
            spawn != null && !spawn.IsBoss && !spawn.Scheduled && instanceId == 0;

        /// <summary>Sorteia se o monstro nasce elite.</summary>
        public static bool Roll(SpawnDefinition spawn, int instanceId, IRandomSource random, double chance = Chance) =>
            CanBeElite(spawn, instanceId) && random.NextDouble() < chance;

        public static int MaxHp(int baseHp) => baseHp * HpMultiplier;
        public static int Damage(int baseDamage) => (int)Math.Round(baseDamage * DamageMultiplier);
        public static int Xp(int baseXp) => baseXp * XpMultiplier;

        /// <summary>Drop do elite: a rolagem normal mais <see cref="ExtraDropRolls"/> (só os itens) e o Alz multiplicado.</summary>
        public static LootResult RollLoot(DropTable drops, IRandomSource random)
        {
            var loot = drops.Roll(random);
            for (var i = 0; i < ExtraDropRolls; i++) loot.Items.AddRange(drops.Roll(random).Items);
            loot.Alz *= AlzMultiplier;
            return loot;
        }
    }

    /// <summary>Regras do personagem que o cliente offline e o servidor precisam concordar.</summary>
    public static class PlayerRules
    {
        public const int MaxSp = 2000;
        public const int StartingSp = 1000;
        public const int SpPerHit = 60;
        public const float MpRegenPerSecond = 10f;
        public const float RespawnSeconds = 4f;
        public const int FinisherBaseDamage = 400;
        public const float FinisherRange = 3f;
        public const double CritChance = 0.1;
        public const double CritMultiplier = 1.5;
        public const float BodyRadius = 0.4f;

        /// <summary>Folga do alcance do golpe do monstro: o jogador precisa sair de fato ou esquivar.</summary>
        public const float MonsterReachTolerance = 1.3f;

        /// <summary>Duração da invulnerabilidade da esquiva.</summary>
        public const float DodgeSeconds = 0.25f;

        public const float DodgeCooldownSeconds = 0.8f;

        /// <summary>Distância máxima para coletar um item com a tecla.</summary>
        public const float PickupRange = 3.5f;

        /// <summary>Skills iniciais (teclas 1 a 4).</summary>
        public static List<SkillData> StarterSkills() => new List<SkillData>
        {
            new SkillData("corte", SkillKind.Physical, 120, 2.5f, 0.5f, castSeconds: 0.45f),
            new SkillData("giro_de_lamina", SkillKind.Physical, 90, 2.5f, 2.0f, areaRadius: 3.5f, castSeconds: 0.55f),
            new SkillData("lanca_de_forca", SkillKind.Magical, 160, 12f, 1.5f, mpCost: 40, castSeconds: 0.6f),
            new SkillData("explosao_arcana", SkillKind.Magical, 220, 10f, 5.0f, areaRadius: 4f, mpCost: 90, castSeconds: 0.75f),
        };
    }
}
