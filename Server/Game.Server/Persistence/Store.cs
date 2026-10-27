using Game.Shared.Accounts;
using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Text;

namespace Game.Server.Persistence;

public sealed record AccountRecord(int Id, string Name, string PasswordHash)
{
    /// <summary>Game master: pode usar os comandos /gm.</summary>
    public bool IsGm { get; init; }

    /// <summary>Banida até esta data (nula = não banida).</summary>
    public DateTimeOffset? BannedUntil { get; init; }

    public string BanReason { get; init; } = "";

    /// <summary>Sem poder falar no chat até esta data (nula = pode falar).</summary>
    public DateTimeOffset? MutedUntil { get; init; }

    public bool IsBanned(DateTimeOffset now) => BannedUntil is { } until && until > now;

    /// <summary>Versão dos termos e da política que a conta aceitou por último (vazia = nunca).</summary>
    public string TermsVersion { get; init; } = "";
}

/// <summary>
/// Um evento das métricas do teste fechado: sessão de jogo (servidor) ou relatório do cliente (tutorial, FPS, erro).
/// <paramref name="Details"/> é texto curto no formato "chave=valor;chave=valor".
/// </summary>
public sealed record MetricRecord(int AccountId, int CharacterId, string Kind, double Value, string Details, DateTimeOffset At);

/// <summary>Dados para o relatório de métricas: quando cada conta foi criada e os eventos do período.</summary>
public sealed record MetricsData(IReadOnlyList<(int AccountId, DateTimeOffset CreatedAt)> Accounts, IReadOnlyList<MetricRecord> Events);

/// <summary>Uma ação de GM (banir, silenciar, teleportar, dar ou tirar item...), para auditoria.</summary>
public sealed record GmActionRecord(string GmName, string Action, string Target, string Details, DateTimeOffset At);

/// <summary>Um item guardado: onde está (bolsa, vestido ou armazém), quantos e o estado da melhoria.</summary>
public sealed record ItemRecord(
    long Uid, ItemContainerKind Container, int Slot, string ItemId, int Quantity,
    int Upgrade = 0, int PityFails = 0, int PityLevel = 0, bool RegainGuaranteed = false, string OptionId = "");

/// <summary>
/// Oferta do mercado como fica no banco: quem vendeu, o item inteiro (com melhoria e opção), o preço e a situação.
/// A situação só anda para a frente (<see cref="MarketListingState"/>): o banco nunca volta uma oferta vendida para ativa.
/// </summary>
public sealed record MarketListingRecord(
    long Id, int SellerCharacterId, string SellerName, ItemRecord Item, long Price, MarketListingState State, string BuyerName,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? SoldAt);

/// <summary>Missão aceita (com contadores) ou concluída.</summary>
/// <param name="CompletedDay">Diárias: o dia (<see cref="Game.Shared.Progression.DailyRules"/>) da última entrega; 0 nas outras.</param>
public sealed record QuestRecord(string QuestId, bool Completed, IReadOnlyList<int> Counts, int CompletedDay = 0);

public enum RelationKind
{
    Friend = 0,
    Blocked = 1,
}

/// <summary>Amigo ou bloqueado: o outro personagem pelo id e o nome (para mostrar quando ele está offline).</summary>
public sealed record RelationRecord(RelationKind Kind, int OtherId, string OtherName);

/// <summary>Denúncia de um jogador, com as últimas mensagens do denunciado (texto original, sem filtro).</summary>
public sealed record ReportRecord(
    int ReporterId, string ReporterName, int TargetId, string TargetName, string Reason, string ChatExcerpt, DateTimeOffset At);

/// <summary>Uma dungeon concluída: tempo, medalha, combo máximo e quem estava no grupo (para o ranking).</summary>
public sealed record DungeonRunRecord(string DungeonId, double Seconds, int Medal, int MaxCombo, string Members, DateTimeOffset At);

public sealed record CharacterSummaryRecord(int Id, string Name, CharacterClass Class, int Level, int Hair = 0, int Cloth = 0,
    string ArmorId = "", string GlovesId = "", string BootsId = "", int Head = 0, int Skin = 0, bool HideHelmet = false);

/// <summary>Personagem como está no banco.</summary>
public sealed record CharacterRecord(
    int Id, int AccountId, string Name, CharacterClass Class, int Level, int Xp, long Alz,
    float X, float Z, int Hp, int Mp, int Sp, IReadOnlyList<ItemRecord> Items,
    Attributes Attributes = default, int AwakeningGauge = 0, IReadOnlyList<QuestRecord>? Quests = null)
{
    public IReadOnlyList<QuestRecord> Quests { get; init; } = Quests ?? Array.Empty<QuestRecord>();
    public IReadOnlyList<RelationRecord> Relations { get; init; } = Array.Empty<RelationRecord>();

    /// <summary>Cores de cabelo e roupa, penteado e tom de pele escolhidos na criação (índices de <see cref="Game.Shared.Items.Appearance"/>).</summary>
    public int Hair { get; init; }
    public int Cloth { get; init; }
    public int Head { get; init; }
    public int Skin { get; init; }

    /// <summary>Elmo escondido (o jogador troca quando quiser).</summary>
    public bool HideHelmet { get; init; }

    /// <summary>Skills aprendidas com o mestre (vazio = personagem de antes do mestre: vale o que o nível liberava).</summary>
    public IReadOnlyList<string> LearnedSkills { get; init; } = Array.Empty<string>();

    /// <summary>Barra de atalhos: o id da skill em cada casa ("" = vazia; lista vazia = barra padrão).</summary>
    public IReadOnlyList<string> Hotbar { get; init; } = Array.Empty<string>();

    /// <summary>Da conta (preenchidos ao entrar no jogo): GM e silêncio no chat.</summary>
    public bool IsGm { get; init; }

    public DateTimeOffset? MutedUntil { get; init; }
}

/// <summary>Estado do personagem para salvar (o que muda durante o jogo).</summary>
public sealed record CharacterSnapshot(
    int CharacterId, int Level, int Xp, long Alz, float X, float Z, int Hp, int Mp, int Sp,
    IReadOnlyList<ItemRecord> Items, Attributes Attributes = default, int AwakeningGauge = 0,
    IReadOnlyList<QuestRecord>? Quests = null)
{
    public IReadOnlyList<QuestRecord> Quests { get; init; } = Quests ?? Array.Empty<QuestRecord>();

    /// <summary>Amigos e bloqueados (o banco fica igual a esta lista).</summary>
    public IReadOnlyList<RelationRecord> Relations { get; init; } = Array.Empty<RelationRecord>();

    public IReadOnlyList<string> LearnedSkills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Hotbar { get; init; } = Array.Empty<string>();
    public bool HideHelmet { get; init; }

    /// <summary>Denúncias feitas desde o último salvamento (só acrescenta).</summary>
    public IReadOnlyList<ReportRecord> Reports { get; init; } = Array.Empty<ReportRecord>();

    /// <summary>Eventos de métricas desde o último salvamento (só acrescenta).</summary>
    public IReadOnlyList<MetricRecord> Metrics { get; init; } = Array.Empty<MetricRecord>();

    /// <summary>Ofertas do mercado que este personagem criou ou mudou desde o último salvamento (na ordem em que mudaram).</summary>
    public IReadOnlyList<MarketListingRecord> Market { get; init; } = Array.Empty<MarketListingRecord>();
}

/// <summary>Movimento de item ou Alz, para auditoria e suporte (quem ganhou o quê, quando).</summary>
public sealed record ItemLogEntry(int CharacterId, DateTimeOffset At, string Kind, string? ItemId, int Quantity, long AlzDelta, string Details);

/// <summary>Conta ou nome de personagem já existente.</summary>
public sealed class StoreConflictException : Exception
{
    public StoreConflictException(LocalText text) : base(text.ToString()) => Text = text;

    /// <summary>O motivo como texto traduzido (vai para o jogador).</summary>
    public LocalText Text { get; }
}

/// <summary>
/// Acesso ao banco do jogo. Implementações: <see cref="EfGameStore"/> (PostgreSQL) e
/// <see cref="InMemoryGameStore"/> (testes e desenvolvimento sem banco). Seguras para chamar de várias threads.
/// </summary>
public interface IGameStore
{
    Task<AccountRecord?> FindAccountAsync(string normalizedAccount);

    /// <summary>
    /// Cria a conta, já com os termos aceitos na versão <paramref name="termsVersion"/>.
    /// <see cref="StoreConflictException"/> se o nome já existe.
    /// </summary>
    Task<AccountRecord> CreateAccountAsync(string normalizedAccount, string passwordHash, string termsVersion);

    /// <summary>Grava que a conta aceitou os termos e a política nesta versão (agora).</summary>
    Task AcceptTermsAsync(int accountId, string termsVersion);

    /// <summary>
    /// Apaga a conta e tudo dela (personagens, itens, missões, amigos, registros de itens e métricas), a pedido do
    /// jogador (LGPD). Denúncias e ações de GM ficam (segurança do jogo). Falso se a conta não existe.
    /// </summary>
    Task<bool> DeleteAccountAsync(string normalizedAccount);

    /// <summary>Contas e eventos de métricas desde <paramref name="since"/> (relatório do teste fechado).</summary>
    Task<MetricsData> MetricsAsync(DateTimeOffset since);

    Task UpdateLoginAsync(int accountId, string? newPasswordHash);

    Task<AccountRecord?> FindAccountByIdAsync(int accountId);

    /// <summary>Conta dona do personagem com esse nome (sem acento/caixa), ou nula.</summary>
    Task<(int AccountId, string CharacterName)?> FindCharacterOwnerAsync(string characterName);

    /// <summary>Bane a conta até <paramref name="until"/> (nulo tira o banimento).</summary>
    Task SetBanAsync(int accountId, DateTimeOffset? until, string reason);

    /// <summary>Silencia a conta até <paramref name="until"/> (nulo tira o silêncio).</summary>
    Task SetMuteAsync(int accountId, DateTimeOffset? until);

    /// <summary>Marca ou desmarca a conta como GM. Falso se a conta não existe.</summary>
    Task<bool> SetGmAsync(string normalizedAccount, bool isGm);

    /// <summary>Troca o hash da senha (comando --set-password). Falso se a conta não existe.</summary>
    Task<bool> SetPasswordHashAsync(string normalizedAccount, string passwordHash);

    Task RecordGmActionAsync(GmActionRecord action);

    /// <summary>Ações de GM, mais recentes primeiro.</summary>
    Task<IReadOnlyList<GmActionRecord>> GmActionsAsync(int max = 100);

    Task<IReadOnlyList<CharacterSummaryRecord>> ListCharactersAsync(int accountId);

    /// <summary>
    /// Cria o personagem com os valores iniciais. <see cref="StoreConflictException"/> se o nome (sem acento/caixa)
    /// já existe ou a conta já tem o máximo de personagens.
    /// </summary>
    Task<CharacterRecord> CreateCharacterAsync(int accountId, string name, CharacterClass characterClass, CharacterRecord initial);

    Task<CharacterRecord?> LoadCharacterAsync(int characterId);

    /// <summary>Salva o estado e os registros de itens numa única transação (tudo ou nada).</summary>
    Task SaveCharacterAsync(CharacterSnapshot snapshot, IReadOnlyList<ItemLogEntry> logs);

    /// <summary>Registros de itens de um personagem, mais recentes primeiro (suporte e testes).</summary>
    Task<IReadOnlyList<ItemLogEntry>> ItemLogAsync(int characterId, int max = 100);

    /// <summary>Maior id único de item ou de oferta do mercado já guardado (o servidor continua a contar daí).</summary>
    Task<long> MaxItemUidAsync();

    /// <summary>Denúncias, mais recentes primeiro (moderação e testes).</summary>
    Task<IReadOnlyList<ReportRecord>> ReportsAsync(int max = 100);

    Task RecordDungeonRunAsync(DungeonRunRecord run);

    /// <summary>Melhores tempos de uma dungeon (menor primeiro).</summary>
    Task<IReadOnlyList<DungeonRunRecord>> TopDungeonRunsAsync(string dungeonId, int max = 10);

    /// <summary>Ofertas do mercado ainda em jogo (ativas e vendidas sem o vendedor receber), para o servidor carregar ao ligar.</summary>
    Task<IReadOnlyList<MarketListingRecord>> MarketListingsAsync();
}

/// <summary>Regra comum aos bancos: a situação de uma oferta só anda para a frente.</summary>
public static class MarketStateOrder
{
    public static int Rank(MarketListingState state) => state switch
    {
        MarketListingState.Active => 0,
        MarketListingState.Sold => 1,
        _ => 2,
    };

    /// <summary>Pode gravar <paramref name="next"/> por cima de <paramref name="current"/>?</summary>
    public static bool CanReplace(MarketListingState current, MarketListingState next) => Rank(next) >= Rank(current);
}

/// <summary>
/// Dá ids únicos de item para o servidor inteiro (mundo e criação de personagem). Começa depois do maior id do banco.
/// Seguro entre threads.
/// </summary>
public sealed class ItemUidAllocator
{
    private long _last;

    public ItemUidAllocator(long lastUsed = 0) => _last = lastUsed;

    public long Next() => Interlocked.Increment(ref _last);
}
