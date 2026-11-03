using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Game.Server.Persistence;

public sealed class AccountEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsGm { get; set; }
    public DateTimeOffset? BannedUntil { get; set; }
    public string BanReason { get; set; } = "";
    public DateTimeOffset? MutedUntil { get; set; }
    public string TermsVersion { get; set; } = "";
    public DateTimeOffset? TermsAcceptedAt { get; set; }
    public List<CharacterEntity> Characters { get; set; } = new();
}

/// <summary>Token de "Manter conectado" de um computador (só o SHA-256 do token).</summary>
public sealed class DeviceTokenEntity
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Evento das métricas do teste fechado (sessões e relatórios do cliente).</summary>
public sealed class MetricEventEntity
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public int AccountId { get; set; }
    public int CharacterId { get; set; }
    public string Kind { get; set; } = "";
    public double Value { get; set; }
    public string Details { get; set; } = "";
}

public sealed class GmActionEntity
{
    public long Id { get; set; }
    public string GmName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class CharacterEntity
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public AccountEntity? Account { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Nome sem acento e em minúsculas: índice único impede nomes "iguais".</summary>
    public string NameKey { get; set; } = "";

    public int ClassId { get; set; }
    public int Level { get; set; }
    public int Xp { get; set; }
    public long Alz { get; set; }
    public float PositionX { get; set; }
    public float PositionZ { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public int Sp { get; set; }
    public int Strength { get; set; }
    public int Dexterity { get; set; }
    public int Intelligence { get; set; }
    public int AwakeningGauge { get; set; }

    /// <summary>Skills aprendidas com o mestre, por vírgula.</summary>
    public string LearnedSkills { get; set; } = "";

    /// <summary>Barra de atalhos: o id da skill de cada casa, por vírgula (casa vazia = nada entre as vírgulas).</summary>
    public string Hotbar { get; set; } = "";

    /// <summary>Cores de cabelo e roupa escolhidas na criação.</summary>
    public int Hair { get; set; }
    public int Cloth { get; set; }
    public int Head { get; set; }
    public int Skin { get; set; }
    public bool HideHelmet { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public List<ItemInstanceEntity> Items { get; set; } = new();
    public List<CharacterQuestEntity> Quests { get; set; } = new();
    public List<CharacterRelationEntity> Relations { get; set; } = new();
}

/// <summary>Amigo (0) ou bloqueado (1). O nome do outro fica guardado para mostrar quando ele está offline.</summary>
public sealed class CharacterRelationEntity
{
    public int CharacterId { get; set; }
    public int Kind { get; set; }
    public int OtherId { get; set; }
    public string OtherName { get; set; } = "";
}

/// <summary>Dungeon concluída (ranking de tempos).</summary>
public sealed class DungeonRunEntity
{
    public long Id { get; set; }
    public string DungeonId { get; set; } = "";
    public double Seconds { get; set; }
    public int Medal { get; set; }
    public int MaxCombo { get; set; }
    public string Members { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>Denúncia de jogador, para a moderação olhar.</summary>
public sealed class PlayerReportEntity
{
    public long Id { get; set; }
    public int ReporterId { get; set; }
    public string ReporterName { get; set; } = "";
    public int TargetId { get; set; }
    public string TargetName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ChatExcerpt { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public bool Handled { get; set; }
}

/// <summary>Um item de verdade, com id único: bolsa (0), vestido (1, posição = encaixe) ou armazém (2).</summary>
public sealed class ItemInstanceEntity
{
    public long Uid { get; set; }
    public int CharacterId { get; set; }
    public int Container { get; set; }
    public int Slot { get; set; }
    public string ItemId { get; set; } = "";
    public int Quantity { get; set; }
    public int Upgrade { get; set; }
    public int PityFails { get; set; }
    public int PityLevel { get; set; }
    public bool RegainGuaranteed { get; set; }
    public string OptionId { get; set; } = "";
}

/// <summary>Oferta do mercado (quadro da escrivã). O item vai inteiro na linha; a situação só anda para a frente.</summary>
public sealed class MarketListingEntity
{
    public long Id { get; set; }
    public int SellerCharacterId { get; set; }
    public string SellerName { get; set; } = "";
    public long ItemUid { get; set; }
    public string ItemId { get; set; } = "";
    public int Quantity { get; set; }
    public int Upgrade { get; set; }
    public int PityFails { get; set; }
    public int PityLevel { get; set; }
    public bool RegainGuaranteed { get; set; }
    public string OptionId { get; set; } = "";
    public long Price { get; set; }
    public int State { get; set; }
    public string BuyerName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? SoldAt { get; set; }
}

/// <summary>Missão aceita ou concluída. Contadores separados por vírgula ("3,10").</summary>
public sealed class CharacterQuestEntity
{
    public int CharacterId { get; set; }
    public string QuestId { get; set; } = "";
    public bool Completed { get; set; }
    public string Counts { get; set; } = "";

    /// <summary>Diárias: o dia da última entrega (0 nas outras missões).</summary>
    public int CompletedDay { get; set; }
}

public sealed class ItemLogEntity
{
    public long Id { get; set; }
    public int CharacterId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Kind { get; set; } = "";
    public string? ItemId { get; set; }
    public int Quantity { get; set; }
    public long AlzDelta { get; set; }
    public string Details { get; set; } = "";
}

/// <summary>Banco do jogo (PostgreSQL). Tabelas em snake_case, nomes em português nos comentários.</summary>
public sealed class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options) { }

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<ItemInstanceEntity> Items => Set<ItemInstanceEntity>();
    public DbSet<CharacterQuestEntity> CharacterQuests => Set<CharacterQuestEntity>();
    public DbSet<ItemLogEntity> ItemLog => Set<ItemLogEntity>();
    public DbSet<CharacterRelationEntity> Relations => Set<CharacterRelationEntity>();
    public DbSet<PlayerReportEntity> Reports => Set<PlayerReportEntity>();
    public DbSet<DungeonRunEntity> DungeonRuns => Set<DungeonRunEntity>();
    public DbSet<GmActionEntity> GmActions => Set<GmActionEntity>();
    public DbSet<MetricEventEntity> MetricEvents => Set<MetricEventEntity>();
    public DbSet<MarketListingEntity> MarketListings => Set<MarketListingEntity>();
    public DbSet<DeviceTokenEntity> DeviceTokens => Set<DeviceTokenEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<DeviceTokenEntity>(e =>
        {
            e.ToTable("device_tokens");
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.AccountId);
            // Conta apagada (LGPD): os tokens vão junto.
            e.HasOne<AccountEntity>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<AccountEntity>(e =>
        {
            e.ToTable("accounts");
            e.Property(a => a.Name).HasMaxLength(32).IsRequired();
            e.HasIndex(a => a.Name).IsUnique();
            e.Property(a => a.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(a => a.BanReason).HasMaxLength(200).HasDefaultValue("");
            e.Property(a => a.TermsVersion).HasMaxLength(20).HasDefaultValue("");
        });

        model.Entity<MetricEventEntity>(e =>
        {
            e.ToTable("metric_events");
            e.HasIndex(m => m.At);
            e.HasIndex(m => new { m.Kind, m.At });
            e.HasIndex(m => m.AccountId);
            e.Property(m => m.Kind).HasMaxLength(32);
            e.Property(m => m.Details).HasMaxLength(400);
        });

        model.Entity<GmActionEntity>(e =>
        {
            e.ToTable("gm_actions");
            e.HasIndex(a => a.At);
            e.Property(a => a.GmName).HasMaxLength(32);
            e.Property(a => a.Action).HasMaxLength(32);
            e.Property(a => a.Target).HasMaxLength(32);
            e.Property(a => a.Details).HasMaxLength(400);
        });

        model.Entity<CharacterEntity>(e =>
        {
            e.ToTable("characters");
            e.Property(c => c.Name).HasMaxLength(32).IsRequired();
            e.Property(c => c.NameKey).HasMaxLength(32).IsRequired();
            e.Property(c => c.LearnedSkills).HasMaxLength(1024).IsRequired().HasDefaultValue("");
            e.Property(c => c.Hotbar).HasMaxLength(1024).IsRequired().HasDefaultValue("");
            e.Property(c => c.Hair).HasDefaultValue(0);
            e.Property(c => c.Cloth).HasDefaultValue(0);
            e.Property(c => c.Head).HasDefaultValue(0);
            e.Property(c => c.Skin).HasDefaultValue(0);
            e.Property(c => c.HideHelmet).HasDefaultValue(false);
            e.HasIndex(c => c.NameKey).IsUnique();
            e.HasIndex(c => c.AccountId);
            e.HasOne(c => c.Account).WithMany(a => a.Characters).HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CharacterId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.Quests).WithOne().HasForeignKey(q => q.CharacterId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.Relations).WithOne().HasForeignKey(r => r.CharacterId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<CharacterRelationEntity>(e =>
        {
            e.ToTable("character_relations");
            e.HasKey(r => new { r.CharacterId, r.Kind, r.OtherId });
            e.Property(r => r.OtherName).HasMaxLength(32);
        });

        model.Entity<DungeonRunEntity>(e =>
        {
            e.ToTable("dungeon_runs");
            e.HasIndex(r => new { r.DungeonId, r.Seconds });
            e.Property(r => r.DungeonId).HasMaxLength(64);
            e.Property(r => r.Members).HasMaxLength(200);
        });

        model.Entity<PlayerReportEntity>(e =>
        {
            e.ToTable("player_reports");
            e.HasIndex(r => new { r.TargetId, r.At });
            e.HasIndex(r => r.Handled);
            e.Property(r => r.ReporterName).HasMaxLength(32);
            e.Property(r => r.TargetName).HasMaxLength(32);
            e.Property(r => r.Reason).HasMaxLength(200);
            e.Property(r => r.ChatExcerpt).HasMaxLength(4000);
        });

        model.Entity<ItemInstanceEntity>(e =>
        {
            e.ToTable("item_instances");
            e.HasKey(i => i.Uid);
            e.Property(i => i.Uid).ValueGeneratedNever(); // o servidor dá o id (ItemUidAllocator)
            e.HasIndex(i => i.CharacterId);
            e.Property(i => i.ItemId).HasMaxLength(64);
            e.Property(i => i.OptionId).HasMaxLength(64);
        });

        model.Entity<MarketListingEntity>(e =>
        {
            e.ToTable("market_listings");
            e.Property(m => m.Id).ValueGeneratedNever(); // o servidor dá o id (ItemUidAllocator)
            e.HasIndex(m => m.State);
            e.HasIndex(m => m.SellerCharacterId);
            // Conta apagada (LGPD): as ofertas dos personagens dela vão junto.
            e.HasOne<CharacterEntity>().WithMany().HasForeignKey(m => m.SellerCharacterId).OnDelete(DeleteBehavior.Cascade);
            e.Property(m => m.SellerName).HasMaxLength(32);
            e.Property(m => m.BuyerName).HasMaxLength(32);
            e.Property(m => m.ItemId).HasMaxLength(64);
            e.Property(m => m.OptionId).HasMaxLength(64);
        });

        model.Entity<CharacterQuestEntity>(e =>
        {
            e.ToTable("character_quests");
            e.HasKey(q => new { q.CharacterId, q.QuestId });
            e.Property(q => q.QuestId).HasMaxLength(64);
            e.Property(q => q.Counts).HasMaxLength(200);
        });

        model.Entity<ItemLogEntity>(e =>
        {
            e.ToTable("item_log");
            e.HasIndex(l => new { l.CharacterId, l.At });
            e.Property(l => l.Kind).HasMaxLength(32);
            e.Property(l => l.ItemId).HasMaxLength(64);
            e.Property(l => l.Details).HasMaxLength(500);
        });
    }
}

/// <summary>
/// Usado pela ferramenta de migrações (<c>dotnet ef</c>): lê a conexão de GAME_DB ou usa a padrão do docker-compose.
/// </summary>
public sealed class GameDbContextDesignFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("GAME_DB") ?? DatabaseDefaults.DevelopmentConnection;
        return new GameDbContext(new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(connection).Options);
    }
}

public static class DatabaseDefaults
{
    /// <summary>Conexão do PostgreSQL do docker-compose.yml (só desenvolvimento local).</summary>
    public const string DevelopmentConnection = "Host=localhost;Port=5432;Database=game;Username=game;Password=game_dev_local";
}
