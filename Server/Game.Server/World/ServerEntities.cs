using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Core;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Progression;
using Game.Shared.Social;
using Game.Shared.World;

namespace Game.Server.World;

/// <summary>Jogador conectado, do ponto de vista do servidor (que decide tudo de combate, itens e missões).</summary>
public sealed class ServerPlayer
{
    /// <summary>Folga de timing do combo no servidor, além da compensação de latência.</summary>
    public const double ComboGraceSeconds = ComboConfig.OnlineGraceSeconds;

    /// <summary>Intervalo mínimo entre poções do mesmo tipo.</summary>
    public const double PotionCooldownSeconds = PlayerRules.PotionCooldownSeconds;

    public int Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Id do personagem no banco; 0 = convidado (testes e bots, não é salvo).</summary>
    public int CharacterId { get; init; }

    /// <summary>Cores de cabelo e roupa, penteado e tom de pele escolhidos na criação (índices de <see cref="Game.Shared.Items.Appearance"/>).</summary>
    public int Hair { get; init; }
    public int Cloth { get; init; }
    public int Head { get; init; }
    public int Skin { get; init; }

    /// <summary>Elmo (ou chapéu) escondido: o jogador troca quando quiser, e fica salvo.</summary>
    public bool HideHelmet { get; set; }

    /// <summary>
    /// Convidado é para testes e bots: tem tudo liberado (skills e combo) sem precisar das missões, e não é salvo.
    /// </summary>
    public bool IsGuest => CharacterId == 0;

    /// <summary>Conta do personagem (0 = convidado).</summary>
    public int AccountId { get; set; }

    /// <summary>Pode usar os comandos /gm.</summary>
    public bool IsGm { get; set; }

    /// <summary>Não pode falar no chat até esta data (nula = pode).</summary>
    public DateTimeOffset? MutedUntil { get; set; }

    /// <summary>Hora (do servidor) em que entrou no jogo.</summary>
    public double JoinedAt { get; set; }

    // Detecção de bot (GameWorld.Suspicion)
    public int ExcellentStreak { get; set; }
    public double NextRouteSampleAt { get; set; }
    public List<(int, int)> RouteSamples { get; } = new();
    public int LastRouteSignature { get; set; }
    public int RepeatedRouteWindows { get; set; }

    /// <summary>Motivos de suspeita (chave de texto) e quando foram marcados.</summary>
    public Dictionary<string, double> Suspicions { get; } = new();

    public CharacterClass Class { get; }

    public required MovementValidator Movement { get; init; }
    public float Yaw { get; set; }
    public uint LastMoveSequence { get; set; }

    public CharacterProgress Progress { get; set; } = new();
    public Attributes Attributes { get; set; }

    /// <summary>Valores finais (nível + atributos + equipamento). Recalcular com <see cref="RecalculateStats"/>.</summary>
    public StatBlock Stats { get; private set; }

    public ResourcePool Hp { get; }
    public ResourcePool Mp { get; }
    public ResourcePool Sp { get; } = new(PlayerRules.MaxSp, PlayerRules.StartingSp);

    /// <summary>Skills da classe, na ordem da barra (o <c>Slot</c> do <c>UseSkill</c> é o índice aqui).</summary>
    public IReadOnlyList<ClassSkill> ClassSkills { get; }
    public IReadOnlyList<SkillData> Skills { get; }
    public SkillCooldowns Cooldowns { get; }

    /// <summary>Skills aprendidas com o mestre (ids). Convidado sabe todas.</summary>
    public HashSet<string> LearnedSkills { get; } = new();

    /// <summary>Barra de atalhos: índice em <see cref="ClassSkills"/> de cada casa (<see cref="SkillTraining.Empty"/> = vazia).</summary>
    public byte[] Hotbar { get; set; } = Enumerable.Repeat(SkillTraining.Empty, SkillTraining.HotbarSize).ToArray();
    public ComboSession Combo { get; }
    public AwakeningState Awakening { get; } = new();
    public long Alz { get; set; }

    public ItemContainer Bag { get; } = new();
    public ItemContainer Warehouse { get; } = new();
    public Equipment Equipment { get; } = new();
    public QuestLog Quests { get; } = new();

    public bool IsDead { get; set; }
    public double RespawnAt { get; set; }
    public double DodgeUntil { get; set; }
    public double NextDodgeAt { get; set; }
    public double NextPotionAt { get; set; }

    /// <summary>Recarga da poção de mana (separada da de vida: tomar MP não trava a vida numa emergência).</summary>
    public double NextManaPotionAt { get; set; }

    /// <summary>Última vez que bateu ou apanhou: a vida só volta sozinha depois de um tempo fora de combate.</summary>
    public double LastCombatAt { get; set; } = double.NegativeInfinity;
    public float HpRegenCarry { get; set; }

    /// <summary>Sentado na fogueira da vila (e onde sentou: andar levanta).</summary>
    public bool Resting { get; set; }
    public float RestX { get; set; }
    public float RestZ { get; set; }
    public float SpRegenCarry { get; set; }

    /// <summary>Conjurando uma skill até este instante (fora do combo): outra skill pedida antes espera na fila.</summary>
    public double CastUntil { get; set; }

    /// <summary>A skill que espera a conjuração atual acabar (só a última pedida) e quando foi pedida.</summary>
    public Game.Shared.Net.UseSkill? QueuedSkill { get; set; }
    public double QueuedAt { get; set; }
    public float MpRegenCarry { get; set; }

    /// <summary>Movimentos de item e Alz desde o último salvamento (vão junto no próximo).</summary>
    public List<ItemLogEntry> PendingLogs { get; } = new();

    /// <summary>Entidades e itens que este jogador já conhece (recebeu EntitySpawned/LootSpawned).</summary>
    public HashSet<int> KnownEntities { get; } = new();

    /// <summary>A aparência (arma, melhoria e peças vestidas) que este jogador já recebeu de cada outro jogador que vê.</summary>
    public Dictionary<int, (string WeaponId, int Upgrade, int ArmorGrade, int GlovesGrade, int BootsGrade, bool HideHelmet)> KnownLooks { get; } = new();

    /// <summary>Arma na mão, o nível de melhoria dela, o grau da couraça, das luvas e das botas e o elmo (o que os outros veem).</summary>
    public (string WeaponId, int Upgrade, int ArmorGrade, int GlovesGrade, int BootsGrade, bool HideHelmet) Look =>
    (
        Equipment[Game.Shared.Items.EquipSlot.Weapon]?.ItemId ?? "",
        Equipment[Game.Shared.Items.EquipSlot.Weapon]?.Upgrade ?? 0,
        WornGrade(Game.Shared.Items.EquipSlot.Armor),
        WornGrade(Game.Shared.Items.EquipSlot.Gloves),
        WornGrade(Game.Shared.Items.EquipSlot.Boots),
        HideHelmet
    );

    private int WornGrade(Game.Shared.Items.EquipSlot slot) => Game.Shared.Items.Appearance.GradeOf(Equipment[slot]?.ItemId, slot);
    public HashSet<int> KnownLoot { get; } = new();

    /// <summary>Precisa reenviar a mensagem correspondente no próximo tick.</summary>
    public bool StatsDirty { get; set; } = true;
    public bool InventoryDirty { get; set; } = true;
    public bool ComboDirty { get; set; } = true;
    public bool SheetDirty { get; set; } = true;
    public bool QuestsDirty { get; set; } = true;

    /// <summary>Grupo atual (0 = sem grupo).</summary>
    public int PartyId { get; set; }

    /// <summary>Convite de grupo esperando resposta: quem convidou e até quando vale.</summary>
    public (int InviterId, double ExpiresAt)? PendingInvite { get; set; }

    /// <summary>Assinatura do último PartyState enviado (só reenvia quando muda).</summary>
    public int LastPartySignature { get; set; }

    /// <summary>Precisa mandar o PartyState mesmo sem mudança de assinatura (entrou ou saiu do grupo).</summary>
    public bool PartyDirty { get; set; }

    public ChatRateLimiter ChatLimiter { get; } = new();

    /// <summary>Troca aberta (0 = nenhuma), pedido de troca esperando resposta e se precisa reenviar o TradeState.</summary>
    public int TradeId { get; set; }
    public (int FromId, double ExpiresAt)? PendingTradeInvite { get; set; }
    public bool TradeDirty { get; set; }

    /// <summary>Instância de dungeon em que está (0 = mundo aberto).</summary>
    public int DungeonId { get; set; }

    /// <summary>PvP: duelo e partida da arena em andamento (0 = nenhum), fila, desafio esperando e reenviar o PvpState.</summary>
    public int DuelId { get; set; }
    public int ArenaId { get; set; }
    public bool InArenaQueue { get; set; }
    public (int FromId, double ExpiresAt)? PendingDuelInvite { get; set; }
    public bool PvpDirty { get; set; }

    /// <summary>Amigos e bloqueados (salvos com o personagem).</summary>
    public List<RelationRecord> Relations { get; } = new();

    /// <summary>Precisa mandar a lista de amigos e bloqueados.</summary>
    public bool SocialDirty { get; set; } = true;

    /// <summary>Últimas mensagens enviadas (texto original), que vão junto se alguém denunciar.</summary>
    public Queue<(double At, ChatChannel Channel, string Text)> RecentChat { get; } = new();

    /// <summary>Denúncias feitas: horários (limite por hora) e quando denunciou cada nome.</summary>
    public List<double> ReportTimes { get; } = new();
    public Dictionary<string, double> ReportedAt { get; } = new();

    /// <summary>Denúncias desde o último salvamento (vão junto no próximo).</summary>
    public List<ReportRecord> PendingReports { get; } = new();

    /// <summary>Ofertas do mercado que mudaram por ação dele desde o último salvamento (vão junto no próximo).</summary>
    public List<MarketListingRecord> PendingMarket { get; } = new();

    // Métricas do teste fechado (GameWorld.Metrics): contadores da sessão, eventos a gravar e relatórios do cliente.
    public SessionStats Session { get; } = new();
    public List<MetricRecord> PendingMetrics { get; } = new();
    public int ClientReports { get; set; }

    public bool HasRelation(RelationKind kind, int characterId) =>
        characterId != 0 && Relations.Any(r => r.Kind == kind && r.OtherId == characterId);

    /// <summary>Este jogador bloqueou o outro (não recebe mensagens nem convites dele).</summary>
    public bool HasBlocked(ServerPlayer other) => HasRelation(RelationKind.Blocked, other.CharacterId);

    public ServerPlayer(CharacterClass characterClass = CharacterClass.Blade)
    {
        Class = characterClass;
        Attributes = Attributes.StartingFor(characterClass);
        ClassSkills = ClassCatalog.SkillsFor(characterClass);
        Skills = ClassSkills.Select(s => s.Skill).ToList();
        Cooldowns = new SkillCooldowns(Skills.Count);
        Combo = new ComboSession(ClassCatalog.ComboConfigFor(characterClass, ComboGraceSeconds));
        Stats = StatCalculator.Compute(1, Attributes, Equipment);
        Hp = new ResourcePool(Stats.MaxHp);
        Mp = new ResourcePool(Stats.MaxMp);
    }

    public float X => Movement.X;
    public float Z => Movement.Z;
    public bool IsDodging(double now) => now < DodgeUntil;

    /// <summary>Missão concluída (convidado conta tudo como concluído).</summary>
    public bool QuestCompleted(string questId) => IsGuest || Quests.IsCompleted(questId);

    /// <summary>Ids das skills que pode usar (convidado: todas).</summary>
    public ICollection<string> SkillUnlockedIds() =>
        IsGuest ? ClassSkills.Select(s => s.Skill.Id).ToHashSet() : LearnedSkills;

    /// <summary>A skill (índice da classe) já foi aprendida com o mestre.</summary>
    public bool SkillUnlocked(int slot) =>
        IsGuest || (slot >= 0 && slot < ClassSkills.Count && LearnedSkills.Contains(ClassSkills[slot].Skill.Id));

    public bool ComboUnlocked => ClassCatalog.ComboUnlocked(QuestCompleted);

    public int FreeAttributePoints => Attributes.FreePoints(Progress.Level, Class, Attributes);

    /// <summary>Recalcula os valores finais e ajusta os máximos de vida e MP (avisa o cliente).</summary>
    public void RecalculateStats()
    {
        Stats = StatCalculator.Compute(Progress.Level, Attributes, Equipment);
        Hp.SetMax(Stats.MaxHp);
        Mp.SetMax(Stats.MaxMp);
        StatsDirty = true;
        SheetDirty = true;
    }
}

/// <summary>Monstro simulado no servidor com a IA do Shared.</summary>
public sealed class ServerMonster
{
    public int Id { get; init; }

    /// <summary>Instância de dungeon (0 = mundo aberto). Monstro de dungeon não renasce.</summary>
    public int InstanceId { get; init; }

    /// <summary>Não recebe dano (escudo do chefe da cripta).</summary>
    public bool Invulnerable { get; set; }

    /// <summary>Elite (<see cref="EliteRules"/>): sorteado a cada nascimento; mais vida, dano, XP, Alz e drop.</summary>
    public bool IsElite { get; set; }

    public required SpawnDefinition Spawn { get; init; }
    public required MonsterBrain Brain { get; init; }
    public required ResourcePool Hp { get; init; }
    public float HomeX { get; init; }
    public float HomeZ { get; init; }

    public MonsterData Data => Spawn.Data;
    public float X { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public double RespawnAt { get; set; }

    /// <summary>Jogador que o monstro persegue (o de mais ameaça, ver <see cref="ThreatRules"/>), ou 0.</summary>
    public int AggroPlayerId { get; set; }

    /// <summary>Ameaça de cada jogador contra este monstro (dano vezes o fator da classe, provocação).</summary>
    public Dictionary<int, double> Threat { get; } = new();

    /// <summary>Dano recebido por jogador (quem causou mais dano fica com o XP e o drop).</summary>
    public Dictionary<int, int> DamageBy { get; } = new();

    public bool IsDead => Brain.State == MonsterState.Dead;

    // Chefe
    public bool PhaseTwo { get; set; }
    public bool PhaseThree { get; set; }

    // Chefe de mundo com horário: o último horário em que despertou e o próximo já avisado.
    public DateTimeOffset LastSlot { get; set; }
    public DateTimeOffset WarnedSlot { get; set; }

    /// <summary>Segundo círculo do golpe em área (só na terceira fase).</summary>
    public (float X, float Z)? PendingSecondArea { get; set; }
    public double BaseAttackCooldown { get; init; }
    public double NextAreaAt { get; set; }
    public (float X, float Z, double LandsAt)? PendingArea { get; set; }
}

/// <summary>Item no chão. Durante alguns segundos só o dono pode pegar.</summary>
public sealed class ServerLoot
{
    public const double OwnerSeconds = 15;
    public const double LifetimeSeconds = 90;

    /// <summary>Quanto tempo fica no chão um item largado por um jogador.</summary>
    public const double DroppedSeconds = 180;

    /// <summary>Item largado por um jogador: vai inteiro para quem pegar (melhoria, pedra e identificador).</summary>
    public ItemInstance? Item { get; init; }

    public int Id { get; init; }
    public ItemDrop Drop { get; init; }
    public float X { get; init; }
    public float Z { get; init; }
    public int OwnerId { get; init; }

    /// <summary>Instância de dungeon onde caiu (0 = mundo aberto); some quando a instância fecha.</summary>
    public int InstanceId { get; init; }

    /// <summary>Grupo do dono quando caiu (0 = sozinho): qualquer membro pode pegar.</summary>
    public int OwnerPartyId { get; init; }

    public double OwnerUntil { get; init; }
    public double ExpiresAt { get; init; }

    /// <summary>Item de missão: só o dono vê e pega (cada um com a missão recebe o seu).</summary>
    public bool Personal { get; init; }

    public bool VisibleTo(int playerId) => !Personal || playerId == OwnerId;

    public bool CanBePickedBy(int playerId, int partyId, double now) =>
        playerId == OwnerId || (!Personal && ((OwnerPartyId != 0 && partyId == OwnerPartyId) || now >= OwnerUntil));
}
