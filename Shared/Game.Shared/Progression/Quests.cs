using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Items;
using Game.Shared.Text;

namespace Game.Shared.Progression
{
    public enum NpcRole
    {
        QuestGiver = 0,
        Smith = 1,
        Warehouse = 2,

        /// <summary>Loja: vende consumíveis e equipamento básico, compra drops.</summary>
        Merchant = 3,

        /// <summary>Portal da dungeon: entrada com o grupo e ranking de tempos.</summary>
        DungeonPortal = 4,

        /// <summary>Mestre de skills: ensina as skills da classe (nível, missão de rank e Alz).</summary>
        Trainer = 5,

        /// <summary>Mercado: quadro de ofertas entre jogadores (vende mesmo com o vendedor offline).</summary>
        Market = 6,
    }

    /// <summary>NPC da vila ou de uma região: posição fixa e o que ele faz.</summary>
    public sealed class NpcDefinition
    {
        public string Id = "";

        /// <summary>Nome no idioma do jogador (chave <c>npc.&lt;id&gt;</c>).</summary>
        public string Name => Loc.T(Loc.Key("npc.", Id));

        public NpcRole Role;
        public float X;
        public float Z;
    }

    /// <summary>Os NPCs da Vila de Partida (em volta do ponto de nascimento).</summary>
    public static class NpcCatalog
    {
        /// <summary>Distância máxima para falar com um NPC.</summary>
        public const float InteractRange = 4f;

        public const string Elder = "anciao_doran";
        public const string Smith = "ferreira_ilsa";
        public const string Warehouse = "guarda_volumes_bento";
        public const string Merchant = "mercadora_lia";
        public const string DungeonPortal = "guardia_selene";
        public const string Trainer = "mestre_aldric";
        public const string Market = "escriva_marta";

        /// <summary>Quem dá as missões de cada região, na entrada dela (fora da vila).</summary>
        public const string Hunter = "cacador_rurik";
        public const string Scout = "batedora_nara";
        public const string Scholar = "erudita_veska";

        public static readonly IReadOnlyList<NpcDefinition> All = new List<NpcDefinition>
        {
            new NpcDefinition { Id = Elder, Role = NpcRole.QuestGiver, X = 0, Z = 8 },
            new NpcDefinition { Id = Smith, Role = NpcRole.Smith, X = 7, Z = 3 },
            new NpcDefinition { Id = Warehouse, Role = NpcRole.Warehouse, X = -7, Z = 3 },
            new NpcDefinition { Id = Merchant, Role = NpcRole.Merchant, X = 5, Z = -6 },
            new NpcDefinition { Id = DungeonPortal, Role = NpcRole.DungeonPortal, X = -5, Z = -6 },
            new NpcDefinition { Id = Trainer, Role = NpcRole.Trainer, X = -9, Z = -1.5f },
            new NpcDefinition { Id = Market, Role = NpcRole.Market, X = 9, Z = -1.5f },
            new NpcDefinition { Id = Hunter, Role = NpcRole.QuestGiver, X = -10, Z = 33 },
            new NpcDefinition { Id = Scout, Role = NpcRole.QuestGiver, X = 32, Z = 66 },
            new NpcDefinition { Id = Scholar, Role = NpcRole.QuestGiver, X = -32, Z = 106 },
        };

        public static NpcDefinition? Find(string id) => All.FirstOrDefault(n => n.Id == id);

        public static bool InRange(NpcDefinition npc, float x, float z)
        {
            var dx = npc.X - x;
            var dz = npc.Z - z;
            return dx * dx + dz * dz <= InteractRange * InteractRange;
        }
    }

    public enum QuestObjectiveKind
    {
        /// <summary>Derrotar N monstros de um tipo.</summary>
        Kill = 0,

        /// <summary>Ter N itens na bolsa ao entregar (são consumidos).</summary>
        Collect = 1,

        /// <summary>Falar com um NPC.</summary>
        Talk = 2,
    }

    public sealed class QuestObjective
    {
        public QuestObjectiveKind Kind { get; }

        /// <summary>Id do monstro, do item ou do NPC.</summary>
        public string TargetId { get; }

        public int Count { get; }

        public QuestObjective(QuestObjectiveKind kind, string targetId, int count = 1)
        {
            Kind = kind;
            TargetId = targetId;
            Count = Math.Max(1, count);
        }
    }

    public sealed class QuestDefinition
    {
        public string Id = "";

        /// <summary>Nome no idioma do jogador (chave <c>quest.&lt;id&gt;</c>; a descrição é <c>.desc</c>).</summary>
        public string Name => Loc.T(Loc.Key("quest.", Id));

        public string Description => Loc.T(Loc.Key("quest.", Id, ".desc"));
        public int MinLevel = 1;

        /// <summary>Missão que precisa estar concluída antes (vazio = nenhuma).</summary>
        public string PreviousQuestId = "";

        public string GiverNpcId = NpcCatalog.Elder;
        public List<QuestObjective> Objectives = new List<QuestObjective>();
        public long RewardXp;
        public long RewardAlz;
        public List<(string ItemId, int Quantity)> RewardItems = new List<(string, int)>();

        /// <summary>Texto curto do que a missão libera, para mostrar na janela (vazio = nada).</summary>
        public string Unlocks => Loc.Optional(Loc.Key("quest.", Id, ".unlocks"));

        /// <summary>
        /// Diária: depois de entregue, volta a ficar disponível no dia seguinte (<see cref="DailyRules"/>). Não ocupa as
        /// vagas de missão ativa e não entra na conta da subida de nível (o jogador pode fazer ou não).
        /// </summary>
        public bool Daily;
    }

    /// <summary>Quando um dia de missões diárias começa: à meia-noite de Brasília (UTC−3, sem horário de verão).</summary>
    public static class DailyRules
    {
        public const int UtcOffsetHours = -3;

        /// <summary>Número do dia (dias desde 1970 no horário de Brasília): muda à meia-noite de lá.</summary>
        public static int Day(DateTimeOffset now) =>
            (int)Math.Floor((now.UtcDateTime.AddHours(UtcOffsetHours) - DateTime.UnixEpoch).TotalDays);

        public static int Today() => Day(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Missões do MVP, do nível 1 ao 30 (GDD: matar N, coletar N, falar com NPC). A história principal fica com o
    /// Ancião, encadeada (três delas são as missões de rank: combo e skills de nível médio e alto). Cada região tem um
    /// NPC na entrada com missões paralelas de quase todo nível, valendo cerca de meio nível de XP cada.
    /// </summary>
    public static class QuestCatalog
    {
        public const int MaxActive = 5;

        public static readonly IReadOnlyList<QuestDefinition> All = new List<QuestDefinition>
        {
            new QuestDefinition
            {
                Id = "primeiros_passos", MinLevel = 1,
                Objectives = { new QuestObjective(QuestObjectiveKind.Talk, NpcCatalog.Smith) },
                RewardXp = 300, RewardAlz = 500, RewardItems = { ("pocao_hp_p", 5) },
            },
            new QuestDefinition
            {
                Id = ClassCatalog.ComboQuestId, MinLevel = 2, PreviousQuestId = "primeiros_passos",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "coelho_saltador", 10) },
                RewardXp = 1200, RewardAlz = 1500, RewardItems = { (ItemCatalog.CoreLow, 2) },
            },
            new QuestDefinition
            {
                Id = "pele_e_presa", MinLevel = 6, PreviousQuestId = ClassCatalog.ComboQuestId,
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "lobo_cinzento", 12),
                    new QuestObjective(QuestObjectiveKind.Collect, "missao_pele_lobo", 6),
                },
                RewardXp = 4000, RewardAlz = 4000, RewardItems = { (ItemCatalog.CoreLow, 3), ("pedra_vigor", 1) },
            },
            new QuestDefinition
            {
                Id = ClassCatalog.RankTwoQuestId, MinLevel = 10, PreviousQuestId = "pele_e_presa",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "javali_furioso", 15),
                    new QuestObjective(QuestObjectiveKind.Kill, "aranha_da_mata", 15),
                },
                RewardXp = 10000, RewardAlz = 12000, RewardItems = { (ItemCatalog.CoreMedium, 2) },
            },
            new QuestDefinition
            {
                Id = "veneno_raro", MinLevel = 15, PreviousQuestId = ClassCatalog.RankTwoQuestId,
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Collect, "missao_veneno", 8),
                    new QuestObjective(QuestObjectiveKind.Talk, NpcCatalog.Smith),
                },
                RewardXp = 18000, RewardAlz = 20000, RewardItems = { (ItemCatalog.CoreMedium, 3), ("pedra_furia", 1) },
            },
            new QuestDefinition
            {
                Id = ClassCatalog.RankThreeQuestId, MinLevel = 20, PreviousQuestId = "veneno_raro",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "golem_de_pedra", 20),
                    new QuestObjective(QuestObjectiveKind.Collect, "missao_fragmento_pedra", 10),
                },
                RewardXp = 28000, RewardAlz = 45000, RewardItems = { (ItemCatalog.CoreHigh, 2) },
            },
            new QuestDefinition
            {
                Id = "ecos_espectrais", MinLevel = 25, PreviousQuestId = ClassCatalog.RankThreeQuestId,
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "espectro", 25),
                    new QuestObjective(QuestObjectiveKind.Collect, "missao_essencia", 10),
                },
                RewardXp = 40000, RewardAlz = 40000, RewardItems = { (ItemCatalog.CoreHigh, 3), ("pedra_precisao", 1) },
            },
            new QuestDefinition
            {
                Id = "o_guardiao", MinLevel = 30, PreviousQuestId = "ecos_espectrais",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "guardiao_das_ruinas") },
                RewardXp = 0, RewardAlz = 60000, RewardItems = { (ItemCatalog.CoreHigh, 5) },
            },

            // ----- Santuário das Ruínas -----
            new QuestDefinition
            {
                Id = "o_santuario", GiverNpcId = NpcCatalog.Scholar, MinLevel = 26,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "oraculo_corrompido") },
                RewardXp = 60000, RewardAlz = 8000, RewardItems = { ("nucleo_alto", 2) },
            },

            // ----- Missões diárias (uma por região e o chefe de campo) -----
            new QuestDefinition
            {
                Id = "diaria_coelhos", GiverNpcId = NpcCatalog.Hunter, MinLevel = 1, Daily = true,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "coelho_saltador", 12) },
                RewardXp = 700, RewardAlz = 400, RewardItems = { ("pocao_hp_p", 3) },
            },
            new QuestDefinition
            {
                Id = "diaria_lobos", GiverNpcId = NpcCatalog.Hunter, MinLevel = 6, Daily = true,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "lobo_cinzento", 10) },
                RewardXp = 1500, RewardAlz = 1000, RewardItems = { ("nucleo_baixo", 1) },
            },
            new QuestDefinition
            {
                Id = "diaria_mata", GiverNpcId = NpcCatalog.Scout, MinLevel = 12, Daily = true,
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "javali_furioso", 8),
                    new QuestObjective(QuestObjectiveKind.Kill, "aranha_da_mata", 6),
                },
                RewardXp = 11000, RewardAlz = 2500, RewardItems = { ("nucleo_medio", 1) },
            },
            new QuestDefinition
            {
                Id = "diaria_ruinas", GiverNpcId = NpcCatalog.Scholar, MinLevel = 22, Daily = true,
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "golem_de_pedra", 6),
                    new QuestObjective(QuestObjectiveKind.Kill, "espectro", 6),
                },
                RewardXp = 28000, RewardAlz = 5000, RewardItems = { ("nucleo_alto", 1) },
            },
            new QuestDefinition
            {
                Id = "diaria_guardiao", GiverNpcId = NpcCatalog.Elder, MinLevel = 28, Daily = true,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "guardiao_das_ruinas", 1) },
                RewardXp = 20000, RewardAlz = 10000, RewardItems = { ("nucleo_alto", 2) },
            },

            // ----- Missões das regiões e da Cripta -----
            new QuestDefinition
            {
                Id = "coelhos_na_horta", GiverNpcId = NpcCatalog.Hunter, MinLevel = 2,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "coelho_saltador", 8) },
                RewardXp = 900, RewardAlz = 100, RewardItems = { ("pocao_hp_p", 3) },
            },
            new QuestDefinition
            {
                Id = "pelos_para_o_inverno", GiverNpcId = NpcCatalog.Hunter, MinLevel = 4, PreviousQuestId = "coelhos_na_horta",
                Objectives = { new QuestObjective(QuestObjectiveKind.Collect, "missao_pelo_macio", 8) },
                RewardXp = 2300, RewardAlz = 300, RewardItems = { ("pocao_hp_p", 4) },
            },
            new QuestDefinition
            {
                Id = "uivos_na_noite", GiverNpcId = NpcCatalog.Hunter, MinLevel = 5,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "lobo_cinzento", 8) },
                RewardXp = 3200, RewardAlz = 400, RewardItems = { ("pocao_hp_m", 2) },
            },
            new QuestDefinition
            {
                Id = "presas_afiadas", GiverNpcId = NpcCatalog.Hunter, MinLevel = 7, PreviousQuestId = "uivos_na_noite",
                Objectives = { new QuestObjective(QuestObjectiveKind.Collect, "missao_presa_lobo", 2) },
                RewardXp = 4900, RewardAlz = 600, RewardItems = { ("nucleo_baixo", 1) },
            },
            new QuestDefinition
            {
                Id = "matilha_alfa", GiverNpcId = NpcCatalog.Hunter, MinLevel = 8, PreviousQuestId = "presas_afiadas",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "lobo_cinzento", 15), new QuestObjective(QuestObjectiveKind.Collect, "missao_pele_lobo", 5) },
                RewardXp = 8000, RewardAlz = 1000, RewardItems = { ("pocao_hp_m", 3), ("nucleo_baixo", 1) },
            },
            new QuestDefinition
            {
                Id = "rastros_na_trilha", GiverNpcId = NpcCatalog.Hunter, MinLevel = 9, PreviousQuestId = "matilha_alfa",
                Objectives = { new QuestObjective(QuestObjectiveKind.Talk, NpcCatalog.Scout) },
                RewardXp = 5600, RewardAlz = 700,
            },
            new QuestDefinition
            {
                Id = "javalis_famintos", GiverNpcId = NpcCatalog.Scout, MinLevel = 10,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "javali_furioso", 12) },
                RewardXp = 11000, RewardAlz = 1300, RewardItems = { ("pocao_hp_m", 3) },
            },
            new QuestDefinition
            {
                Id = "couro_resistente", GiverNpcId = NpcCatalog.Scout, MinLevel = 12, PreviousQuestId = "javalis_famintos",
                Objectives = { new QuestObjective(QuestObjectiveKind.Collect, "missao_couro_javali", 10) },
                RewardXp = 12700, RewardAlz = 1500, RewardItems = { ("nucleo_baixo", 2) },
            },
            new QuestDefinition
            {
                Id = "teias_na_trilha", GiverNpcId = NpcCatalog.Scout, MinLevel = 13,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "aranha_da_mata", 10) },
                RewardXp = 14300, RewardAlz = 1700, RewardItems = { ("pocao_hp_m", 3) },
            },
            new QuestDefinition
            {
                Id = "seda_fina", GiverNpcId = NpcCatalog.Scout, MinLevel = 14, PreviousQuestId = "teias_na_trilha",
                Objectives = { new QuestObjective(QuestObjectiveKind.Collect, "missao_seda", 8) },
                RewardXp = 16100, RewardAlz = 1900, RewardItems = { ("nucleo_medio", 1) },
            },
            new QuestDefinition
            {
                Id = "ninho_da_aranha", GiverNpcId = NpcCatalog.Scout, MinLevel = 17, PreviousQuestId = "seda_fina",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "aranha_da_mata", 25) },
                RewardXp = 26200, RewardAlz = 3100, RewardItems = { ("nucleo_medio", 1), ("pocao_hp_m", 4) },
            },
            new QuestDefinition
            {
                Id = "limpar_a_mata", GiverNpcId = NpcCatalog.Scout, MinLevel = 18, PreviousQuestId = "couro_resistente",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "javali_furioso", 15), new QuestObjective(QuestObjectiveKind.Kill, "aranha_da_mata", 15) },
                RewardXp = 28700, RewardAlz = 3400, RewardItems = { ("nucleo_medio", 2) },
            },
            new QuestDefinition
            {
                Id = "pedras_que_andam", GiverNpcId = NpcCatalog.Scout, MinLevel = 19, PreviousQuestId = "ninho_da_aranha",
                Objectives = { new QuestObjective(QuestObjectiveKind.Talk, NpcCatalog.Scholar) },
                RewardXp = 18200, RewardAlz = 2200,
            },
            new QuestDefinition
            {
                Id = "golens_despertos", GiverNpcId = NpcCatalog.Scholar, MinLevel = 21,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "golem_de_pedra", 10) },
                RewardXp = 30500, RewardAlz = 3700, RewardItems = { ("pocao_hp_m", 4) },
            },
            new QuestDefinition
            {
                // O Nv22 era o único sem missão nova.
                Id = "pedras_sussurrantes", GiverNpcId = NpcCatalog.Scholar, MinLevel = 22, PreviousQuestId = "golens_despertos",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "golem_de_pedra", 6),
                    new QuestObjective(QuestObjectiveKind.Collect, "missao_fragmento_pedra", 6),
                },
                RewardXp = 33000, RewardAlz = 4000, RewardItems = { ("pocao_hp_m", 3) },
            },
            new QuestDefinition
            {
                Id = "nucleo_de_pedra", GiverNpcId = NpcCatalog.Scholar, MinLevel = 23, PreviousQuestId = "golens_despertos",
                Objectives = { new QuestObjective(QuestObjectiveKind.Collect, "missao_fragmento_pedra", 12) },
                RewardXp = 35200, RewardAlz = 4200, RewardItems = { ("nucleo_alto", 1) },
            },
            new QuestDefinition
            {
                Id = "lamentos", GiverNpcId = NpcCatalog.Scholar, MinLevel = 24,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "espectro", 12) },
                RewardXp = 37700, RewardAlz = 4500, RewardItems = { ("pocao_hp_m", 4) },
            },
            new QuestDefinition
            {
                Id = "essencia_perdida", GiverNpcId = NpcCatalog.Scholar, MinLevel = 26, PreviousQuestId = "lamentos",
                Objectives = { new QuestObjective(QuestObjectiveKind.Collect, "missao_essencia", 6) },
                RewardXp = 42700, RewardAlz = 5100, RewardItems = { ("nucleo_alto", 1) },
            },
            new QuestDefinition
            {
                Id = "cavaleiros_caidos", GiverNpcId = NpcCatalog.Scholar, MinLevel = 27, PreviousQuestId = "essencia_perdida",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "cavaleiro_caido", 10) },
                RewardXp = 49900, RewardAlz = 6000, RewardItems = { ("nucleo_alto", 1) },
            },
            new QuestDefinition
            {
                Id = "honra_perdida", GiverNpcId = NpcCatalog.Scholar, MinLevel = 28, PreviousQuestId = "cavaleiros_caidos",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "cavaleiro_caido", 20), new QuestObjective(QuestObjectiveKind.Collect, "missao_essencia", 5) },
                RewardXp = 57700, RewardAlz = 6900, RewardItems = { ("nucleo_alto", 2) },
            },
            new QuestDefinition
            {
                Id = "antidoto", GiverNpcId = NpcCatalog.Scout, MinLevel = 16, PreviousQuestId = "teias_na_trilha",
                Objectives = { new QuestObjective(QuestObjectiveKind.Collect, "missao_veneno", 4) },
                RewardXp = 21900, RewardAlz = 2600, RewardItems = { ("pocao_hp_m", 5) },
            },
            new QuestDefinition
            {
                Id = "o_ultimo_portao", GiverNpcId = NpcCatalog.Scholar, MinLevel = 29, PreviousQuestId = "honra_perdida",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "cavaleiro_caido", 15),
                    new QuestObjective(QuestObjectiveKind.Kill, "espectro", 10),
                },
                RewardXp = 61000, RewardAlz = 7300, RewardItems = { ("nucleo_alto", 2) },
            },
            new QuestDefinition
            {
                Id = "a_cripta_esquecida", GiverNpcId = NpcCatalog.Elder, MinLevel = 18, PreviousQuestId = ClassCatalog.RankTwoQuestId,
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "senhor_da_cripta") },
                RewardXp = 38300, RewardAlz = 4600, RewardItems = { ("nucleo_medio", 2) },
            },
        };

        public static QuestDefinition? Find(string id) => All.FirstOrDefault(q => q.Id == id);

        public static QuestDefinition Get(string id) =>
            Find(id) ?? throw new ArgumentException($"Missão desconhecida: {id}", nameof(id));
    }

    /// <summary>Andamento de uma missão aceita: um contador por objetivo.</summary>
    public sealed class QuestProgress
    {
        public string QuestId { get; }
        public int[] Counts { get; }

        public QuestProgress(string questId, int[]? counts = null)
        {
            QuestId = questId;
            var objectives = QuestCatalog.Find(questId)?.Objectives.Count ?? 0;
            Counts = new int[objectives];
            if (counts != null) Array.Copy(counts, Counts, Math.Min(counts.Length, Counts.Length));
        }
    }

    /// <summary>Resultado da entrega de uma missão.</summary>
    public readonly struct QuestReward
    {
        public long Xp { get; }
        public long Alz { get; }

        public QuestReward(long xp, long alz)
        {
            Xp = xp;
            Alz = alz;
        }
    }

    /// <summary>
    /// Diário de missões de um personagem. Matar e falar contam aqui; coletar é conferido na bolsa na hora de entregar
    /// (assim o jogador pode vender ou guardar no meio do caminho sem dessincronizar o contador).
    /// </summary>
    public sealed class QuestLog
    {
        private readonly Dictionary<string, QuestProgress> _active = new Dictionary<string, QuestProgress>();
        private readonly HashSet<string> _completed = new HashSet<string>();
        private readonly Dictionary<string, int> _dailyDone = new Dictionary<string, int>();
        private readonly Func<int> _today;

        /// <param name="today">Dia atual das diárias (<see cref="DailyRules.Today"/>; os testes passam outro).</param>
        public QuestLog(Func<int>? today = null)
        {
            _today = today ?? DailyRules.Today;
        }

        public IEnumerable<QuestProgress> Active => _active.Values;

        /// <summary>Concluídas: as normais e as diárias entregues hoje (as de outros dias estão disponíveis de novo).</summary>
        public IEnumerable<string> Completed
        {
            get
            {
                var today = _today();
                return _completed.Concat(_dailyDone.Where(d => d.Value == today).Select(d => d.Key));
            }
        }

        /// <summary>Dia em que cada diária foi entregue pela última vez (para salvar).</summary>
        public IReadOnlyDictionary<string, int> DailyDone => _dailyDone;

        public bool IsCompleted(string questId) =>
            _completed.Contains(questId) || (_dailyDone.TryGetValue(questId, out var day) && day == _today());
        public bool IsActive(string questId) => _active.ContainsKey(questId);
        public QuestProgress? ProgressOf(string questId) => _active.TryGetValue(questId, out var p) ? p : null;

        /// <summary>Nulo se pode aceitar; senão o motivo.</summary>
        public LocalText? CanAccept(QuestDefinition quest, int level)
        {
            if (quest.Daily && IsCompleted(quest.Id)) return new LocalText("quest.daily_done");
            if (_completed.Contains(quest.Id)) return new LocalText("quest.done_already");
            if (_active.ContainsKey(quest.Id)) return new LocalText("quest.active_already");
            if (level < quest.MinLevel) return new LocalText("quest.level", quest.MinLevel);
            if (!string.IsNullOrEmpty(quest.PreviousQuestId) && !_completed.Contains(quest.PreviousQuestId))
                return new LocalText("quest.previous", Loc.Ref("quest." + quest.PreviousQuestId));
            // As diárias não ocupam as vagas: dá para fazer a história e as diárias juntas.
            if (!quest.Daily && _active.Keys.Count(id => !QuestCatalog.Get(id).Daily) >= QuestCatalog.MaxActive)
                return new LocalText("quest.max_active", QuestCatalog.MaxActive);
            return null;
        }

        public LocalText? Accept(string questId, int level)
        {
            var quest = QuestCatalog.Find(questId);
            if (quest == null) return new LocalText("quest.unknown");
            var reason = CanAccept(quest, level);
            if (reason != null) return reason;
            _active[questId] = new QuestProgress(questId);
            return null;
        }

        public bool Abandon(string questId) => _active.Remove(questId);

        /// <summary>Missões que o NPC pode oferecer agora (as da história primeiro, depois as diárias).</summary>
        public IEnumerable<QuestDefinition> AvailableFrom(string npcId, int level) =>
            QuestCatalog.All.Where(q => q.GiverNpcId == npcId && CanAccept(q, level) == null).OrderBy(q => q.Daily);

        /// <summary>Conta um monstro derrotado. Retorna as missões que mudaram.</summary>
        public List<string> OnKill(string monsterId) => Advance(QuestObjectiveKind.Kill, monsterId);

        /// <summary>Conta uma conversa com o NPC. Retorna as missões que mudaram.</summary>
        public List<string> OnTalk(string npcId) => Advance(QuestObjectiveKind.Talk, npcId);

        private List<string> Advance(QuestObjectiveKind kind, string targetId)
        {
            var changed = new List<string>();
            foreach (var progress in _active.Values)
            {
                var objectives = QuestCatalog.Get(progress.QuestId).Objectives;
                for (var i = 0; i < objectives.Count; i++)
                {
                    var objective = objectives[i];
                    if (objective.Kind != kind || objective.TargetId != targetId || progress.Counts[i] >= objective.Count) continue;
                    progress.Counts[i]++;
                    if (!changed.Contains(progress.QuestId)) changed.Add(progress.QuestId);
                }
            }
            return changed;
        }

        /// <summary>Quanto já foi feito do objetivo (coletar olha a bolsa).</summary>
        public static int Done(QuestProgress progress, int index, ItemContainer bag)
        {
            var objective = QuestCatalog.Get(progress.QuestId).Objectives[index];
            var done = objective.Kind == QuestObjectiveKind.Collect ? bag.Count(objective.TargetId) : progress.Counts[index];
            return Math.Min(done, objective.Count);
        }

        public static bool IsReady(QuestProgress progress, ItemContainer bag)
        {
            var objectives = QuestCatalog.Get(progress.QuestId).Objectives;
            for (var i = 0; i < objectives.Count; i++)
                if (Done(progress, i, bag) < objectives[i].Count) return false;
            return true;
        }

        /// <summary>
        /// Entrega a missão no NPC que a deu: consome os itens de coleta e põe os itens da recompensa na bolsa. Tudo ou
        /// nada — se faltar espaço, nada muda. XP e Alz ficam para quem chama aplicar.
        /// </summary>
        public LocalText? TurnIn(string questId, ItemContainer bag, Func<long> newUid, out QuestReward reward)
        {
            reward = default;
            if (!_active.TryGetValue(questId, out var progress)) return new LocalText("quest.not_active");
            if (!IsReady(progress, bag)) return new LocalText("quest.not_ready");
            var quest = QuestCatalog.Get(questId);

            // Confere o espaço numa cópia antes de mexer na bolsa de verdade.
            var trial = bag.Clone();
            foreach (var objective in quest.Objectives)
                if (objective.Kind == QuestObjectiveKind.Collect) trial.TryRemove(objective.TargetId, objective.Count);
            long trialUid = 0;
            foreach (var (itemId, quantity) in quest.RewardItems)
                if (!trial.TryAdd(itemId, quantity, () => --trialUid)) return new LocalText("quest.bag_full");

            foreach (var objective in quest.Objectives)
                if (objective.Kind == QuestObjectiveKind.Collect) bag.TryRemove(objective.TargetId, objective.Count);
            foreach (var (itemId, quantity) in quest.RewardItems) bag.TryAdd(itemId, quantity, newUid);

            _active.Remove(questId);
            if (quest.Daily) _dailyDone[questId] = _today();
            else _completed.Add(questId);
            reward = new QuestReward(quest.RewardXp, quest.RewardAlz);
            return null;
        }

        /// <summary>Restaura do banco. <paramref name="dailyDone"/>: dia em que cada diária foi entregue.</summary>
        public void Restore(IEnumerable<string> completed, IEnumerable<QuestProgress> active,
            IEnumerable<KeyValuePair<string, int>>? dailyDone = null)
        {
            _completed.Clear();
            _active.Clear();
            _dailyDone.Clear();
            foreach (var id in completed)
            {
                var quest = QuestCatalog.Find(id);
                if (quest != null && !quest.Daily) _completed.Add(id);
            }
            foreach (var (id, day) in dailyDone ?? Array.Empty<KeyValuePair<string, int>>())
                if (QuestCatalog.Find(id) is { Daily: true }) _dailyDone[id] = day;
            foreach (var progress in active)
                if (QuestCatalog.Find(progress.QuestId) != null && !_completed.Contains(progress.QuestId))
                    _active[progress.QuestId] = progress;
        }
    }
}
