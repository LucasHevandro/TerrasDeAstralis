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

    /// <summary>Fogueira de retorno no mundo aberto (nome na chave <c>respawn_point.&lt;id&gt;</c>).</summary>
    public sealed class RespawnPoint
    {
        public string Id { get; }
        public float X { get; }
        public float Z { get; }

        public string Name => Loc.T(Loc.Key("respawn_point.", Id));

        public RespawnPoint(string id, float x, float z)
        {
            Id = id;
            X = x;
            Z = z;
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

        /// <summary>
        /// Fogueiras onde se renasce no mundo aberto: a mais perto de onde o jogador morreu. Antes era sempre a vila, e
        /// morrer nas Ruínas ou no chefe custava meio minuto de corrida de volta. A primeira é a vila.
        /// </summary>
        public static readonly IReadOnlyList<RespawnPoint> RespawnPoints = new List<RespawnPoint>
        {
            new RespawnPoint("vila", PlayerSpawnX, PlayerSpawnZ),
            new RespawnPoint("acampamento_mata", 28, 62),
            new RespawnPoint("acampamento_ruinas", -22, 100),
            new RespawnPoint("entrada_arena", 10, 144),
        };

        /// <summary>Fogueira mais perto do ponto (x, z).</summary>
        public static RespawnPoint NearestRespawn(float x, float z)
        {
            var best = RespawnPoints[0];
            var bestDistance = float.MaxValue;
            foreach (var point in RespawnPoints)
            {
                var distance = (point.X - x) * (point.X - x) + (point.Z - z) * (point.Z - z);
                if (distance < bestDistance)
                {
                    best = point;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public static List<ZoneDefinition> Zones() => new List<ZoneDefinition>
        {
            new ZoneDefinition { Id = "vila", MinLevel = 1, MaxLevel = 30, CenterX = 0, CenterZ = 0, Radius = 16 },
            new ZoneDefinition { Id = "campos_do_vento", MinLevel = 1, MaxLevel = 10, CenterX = 0, CenterZ = 40, Radius = 24 },
            new ZoneDefinition { Id = "mata_sombria", MinLevel = 10, MaxLevel = 20, CenterX = 45, CenterZ = 85, Radius = 26 },
            new ZoneDefinition { Id = "ruinas_esquecidas", MinLevel = 20, MaxLevel = 30, CenterX = -45, CenterZ = 125, Radius = 26 },
            new ZoneDefinition { Id = "arena_do_guardiao", MinLevel = 28, MaxLevel = 30, CenterX = 0, CenterZ = 170, Radius = 18 },
        };

        public static List<SpawnDefinition> Spawns() => new List<SpawnDefinition>
        {
            // Campos do Vento (Nv 1–10)
            // Raios largos e aggro curto (2026-10-27): antes os grupos eram apertados e chegar perto de um puxava todos.
            Spawn(-10, 36, 10, 13, new MonsterData
            {
                Id = "coelho_saltador", Level = 2, MaxHp = 700, AttackDamage = 50,
                AttackPower = 5, Defense = 20, AttackRange = 1.2f, MoveSpeed = 3.5f, BodyRadius = 0.35f, Aggressive = false,
                Behavior = MonsterBehavior.Skittish,
                XpReward = 30, RespawnSeconds = 6,
                Drops = new DropTable(3, 8,
                    new DropEntry("pelo_macio", ItemRarity.Common, 0.6, 1, 2),
                    new DropEntry("pocao_hp_p", ItemRarity.Common, 0.15),
                    new DropEntry("pocao_mp_p", ItemRarity.Common, 0.12),
                    Equip("armadura_ferro", 0.02), Equip("anel_ferro", 0.01)),
            }),
            Spawn(10, 46, 8, 11, new MonsterData
            {
                Id = "lobo_cinzento", Level = 6, MaxHp = 1800, AttackDamage = 150,
                AttackPower = 15, Defense = 40, AttackRange = 1.5f, MoveSpeed = 4.5f, AggroRadius = 6, Behavior = MonsterBehavior.Pack,
                XpReward = 100, RespawnSeconds = 8,
                Drops = new DropTable(10, 25,
                    new DropEntry("pele_lobo", ItemRarity.Common, 0.5, 1, 2),
                    new DropEntry("presa_lobo", ItemRarity.Uncommon, 0.15),
                    new DropEntry("pocao_mp_p", ItemRarity.Common, 0.1),
                    new DropEntry("nucleo_baixo", ItemRarity.Rare, 0.03),
                    Equip("luvas_ferro", 0.03), Equip("botas_ferro", 0.03), Equip("espada_ferro", 0.02),
                    Equip("cajado_ferro", 0.02), Equip("arco_ferro", 0.02), Equip("pedra_vigor", 0.01)),
            }),

            // Lobos-alfa no norte dos Campos: a ponte do Nv9-10 até os javalis.
            Spawn(-16, 57, 6, 11, new MonsterData
            {
                Id = "lobo_alfa", Level = 9, MaxHp = 3000, AttackDamage = 220,
                AttackPower = 25, Defense = 60, AttackRange = 1.6f, MoveSpeed = 4.8f, AggroRadius = 6, Behavior = MonsterBehavior.Pack,
                XpReward = 140, RespawnSeconds = 9,
                Drops = new DropTable(15, 35,
                    new DropEntry("pele_lobo", ItemRarity.Common, 0.6, 1, 2),
                    new DropEntry("presa_lobo", ItemRarity.Uncommon, 0.25),
                    new DropEntry("nucleo_baixo", ItemRarity.Rare, 0.04),
                    Equip("luvas_ferro", 0.03), Equip("botas_aco_negro", 0.01)),
            }),

            // Mata Sombria (Nv 10–20)
            Spawn(38, 90, 8, 10, new MonsterData
            {
                Id = "javali_furioso", Level = 12, MaxHp = 4400, AttackDamage = 300,
                AttackPower = 40, Defense = 90, AttackRange = 1.6f, AttackWindupSeconds = 0.7f, MoveSpeed = 5f, Behavior = MonsterBehavior.Charger,
                AggroRadius = 6.5f, XpReward = 200, RespawnSeconds = 10,
                Drops = new DropTable(30, 70,
                    new DropEntry("couro_javali", ItemRarity.Common, 0.5, 1, 3),
                    new DropEntry("pocao_mp_m", ItemRarity.Common, 0.06),
                    new DropEntry("nucleo_baixo", ItemRarity.Rare, 0.06),
                    Equip("armadura_aco_negro", 0.02), Equip("botas_aco_negro", 0.03), Equip("pedra_furia", 0.01)),
            }),
            Spawn(62, 96, 8, 11, new MonsterData
            {
                Id = "aranha_da_mata", Level = 16, MaxHp = 5200, AttackDamage = 375,
                AttackPower = 55, Defense = 80, AttackRange = 2f, AttackWindupSeconds = 0.5f, MoveSpeed = 4f, BodyRadius = 0.8f,
                Behavior = MonsterBehavior.HitAndRun,
                AggroRadius = 7, XpReward = 300, RespawnSeconds = 10,
                Drops = new DropTable(40, 90,
                    new DropEntry("seda", ItemRarity.Common, 0.5, 1, 2),
                    new DropEntry("veneno", ItemRarity.Uncommon, 0.2),
                    new DropEntry("nucleo_medio", ItemRarity.Rare, 0.04),
                    Equip("luvas_aco_negro", 0.03), Equip("anel_aco_negro", 0.02), Equip("espada_aco_negro", 0.015),
                    Equip("cajado_aco_negro", 0.015), Equip("arco_aco_negro", 0.015)),
            }),

            // Aranhas-rainhas na borda da Mata, no caminho das Ruínas: a ponte do Nv19-20 até os golens.
            Spawn(28, 104, 6, 9, new MonsterData
            {
                Id = "aranha_rainha", Level = 19, MaxHp = 7500, AttackDamage = 450,
                AttackPower = 65, Defense = 110, AttackRange = 2.2f, AttackWindupSeconds = 0.55f, MoveSpeed = 4.2f, BodyRadius = 1f,
                Behavior = MonsterBehavior.HitAndRun,
                AggroRadius = 7, XpReward = 480, RespawnSeconds = 11,
                Drops = new DropTable(60, 120,
                    new DropEntry("seda", ItemRarity.Common, 0.5, 1, 2),
                    new DropEntry("veneno", ItemRarity.Uncommon, 0.3),
                    new DropEntry("nucleo_medio", ItemRarity.Rare, 0.05),
                    Equip("anel_aco_negro", 0.02), Equip("armadura_astralita", 0.005)),
            }),

            // Ruínas Esquecidas (Nv 20–30)
            Spawn(-40, 126, 6, 10, new MonsterData
            {
                Id = "golem_de_pedra", Level = 22, MaxHp = 12000, AttackDamage = 600,
                AttackPower = 80, Defense = 260, AttackRange = 2.2f, AttackCooldownSeconds = 3f,
                AttackWindupSeconds = 1.0f, MoveSpeed = 2.2f, BodyRadius = 0.8f, AggroRadius = 5.5f, XpReward = 700, RespawnSeconds = 15,
                Behavior = MonsterBehavior.AreaSlam,
                Drops = new DropTable(80, 160,
                    new DropEntry("fragmento_pedra", ItemRarity.Common, 0.6, 1, 3),
                    new DropEntry("nucleo_medio", ItemRarity.Rare, 0.08),
                    Equip("armadura_astralita", 0.015), Equip("botas_astralita", 0.02), Equip("pedra_precisao", 0.01)),
            }),
            Spawn(-62, 120, 8, 11, new MonsterData
            {
                Id = "espectro", Level = 26, MaxHp = 9000, AttackDamage = 700,
                AttackPower = 100, Defense = 120, AttackRange = 2.5f, AttackCooldownSeconds = 1.6f,
                AttackWindupSeconds = 0.45f, MoveSpeed = 5.5f, AggroRadius = 8, XpReward = 900, RespawnSeconds = 12,
                Behavior = MonsterBehavior.Blink,
                Drops = new DropTable(100, 200,
                    new DropEntry("essencia", ItemRarity.Uncommon, 0.4),
                    new DropEntry("nucleo_alto", ItemRarity.Rare, 0.05),
                    Equip("luvas_astralita", 0.02), Equip("anel_astralita", 0.015), Equip("espada_astralita", 0.01),
                    Equip("cajado_astralita", 0.01), Equip("arco_astralita", 0.01)),
            }),

            Spawn(-28, 144, 6, 10, new MonsterData
            {
                Id = "cavaleiro_caido", Level = 29, MaxHp = 14000, AttackDamage = 825,
                AttackPower = 120, Defense = 230, AttackRange = 2.4f, AttackCooldownSeconds = 2.2f,
                AttackWindupSeconds = 0.7f, MoveSpeed = 4f, BodyRadius = 0.7f, AggroRadius = 6.5f, XpReward = 1400, RespawnSeconds = 15,
                Behavior = MonsterBehavior.Berserk,
                Drops = new DropTable(150, 300,
                    new DropEntry("essencia", ItemRarity.Uncommon, 0.3),
                    new DropEntry("nucleo_alto", ItemRarity.Rare, 0.07),
                    Equip("armadura_astralita", 0.02), Equip("espada_astralita", 0.012), Equip("cajado_astralita", 0.012), Equip("arco_astralita", 0.012),
                    Equip("pedra_precisao", 0.015)),
            }),

            // Cavaleiros errantes na entrada da arena (Nv 28): mais caça para o fim da subida.
            Spawn(-10, 157, 6, 10, new MonsterData
            {
                Id = "cavaleiro_errante", Level = 28, MaxHp = 13000, AttackDamage = 780,
                AttackPower = 115, Defense = 220, AttackRange = 2.4f, AttackCooldownSeconds = 2.2f,
                AttackWindupSeconds = 0.7f, MoveSpeed = 4f, BodyRadius = 0.7f, AggroRadius = 6.5f, XpReward = 1250, RespawnSeconds = 14,
                Behavior = MonsterBehavior.Berserk,
                Drops = new DropTable(140, 280,
                    new DropEntry("essencia", ItemRarity.Uncommon, 0.3),
                    new DropEntry("nucleo_alto", ItemRarity.Rare, 0.06),
                    Equip("luvas_astralita", 0.02), Equip("botas_astralita", 0.02)),
            }),

            // Chefe de campo
            new SpawnDefinition
            {
                CenterX = 0, CenterZ = 170, Count = 1, Radius = 0, IsBoss = true, Scheduled = true,
                Data = new MonsterData
                {
                    Id = "guardiao_das_ruinas", Level = 30, MaxHp = 180000,
                    AttackDamage = 570, AttackPower = 140, Defense = 280, AttackRange = 3.2f,
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

        /// <summary>Grupo comum do mundo aberto: os agressivos esperam um instante antes de atacar quem chega perto.</summary>
        private static SpawnDefinition Spawn(float x, float z, int count, float radius, MonsterData data)
        {
            if (data.Aggressive) data.NoticeSeconds = MonsterBehaviors.WorldNoticeSeconds;
            return new SpawnDefinition { CenterX = x, CenterZ = z, Count = count, Radius = radius, Data = data };
        }
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
        /// <summary>MP que volta por segundo (era 10; baixou para as poções de mana terem uso).</summary>
        public const float MpRegenPerSecond = 4f;

        /// <summary>Fração da vida que volta por segundo, só fora de combate (bem mais lenta que o MP).</summary>
        public const float HpRegenFractionPerSecond = 0.005f;

        /// <summary>Segundos sem bater nem apanhar para a vida começar a voltar.</summary>
        public const float OutOfCombatSeconds = 8f;

        /// <summary>Recarga entre poções do mesmo tipo (vida e mana têm recargas separadas).</summary>
        public const double PotionCooldownSeconds = 1.0;
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
