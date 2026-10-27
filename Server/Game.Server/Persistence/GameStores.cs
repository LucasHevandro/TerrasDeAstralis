using Game.Shared.Accounts;
using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Game.Server.Persistence;

/// <summary>Banco PostgreSQL via Entity Framework Core. Cada operação usa um contexto curto (seguro entre threads).</summary>
public sealed class EfGameStore : IGameStore
{
    private readonly IDbContextFactory<GameDbContext> _factory;

    public EfGameStore(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <summary>Cria o banco/tabelas que faltam aplicando as migrações pendentes.</summary>
    public async Task MigrateAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task<AccountRecord?> FindAccountAsync(string normalizedAccount)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Name == normalizedAccount);
        return account == null ? null : ToRecord(account);
    }

    private static AccountRecord ToRecord(AccountEntity a) => new(a.Id, a.Name, a.PasswordHash)
    {
        IsGm = a.IsGm, BannedUntil = a.BannedUntil, BanReason = a.BanReason ?? "", MutedUntil = a.MutedUntil,
        TermsVersion = a.TermsVersion ?? "",
    };

    public async Task<AccountRecord?> FindAccountByIdAsync(int accountId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
        return account == null ? null : ToRecord(account);
    }

    public async Task<(int AccountId, string CharacterName)?> FindCharacterOwnerAsync(string characterName)
    {
        var key = AccountRules.NameKey(characterName);
        await using var db = await _factory.CreateDbContextAsync();
        var character = await db.Characters.AsNoTracking().Where(c => c.NameKey == key).Select(c => new { c.AccountId, c.Name }).FirstOrDefaultAsync();
        return character == null ? null : (character.AccountId, character.Name);
    }

    public async Task SetBanAsync(int accountId, DateTimeOffset? until, string reason)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId);
        if (account == null) return;
        account.BannedUntil = until;
        account.BanReason = until == null ? "" : reason;
        await db.SaveChangesAsync();
    }

    public async Task SetMuteAsync(int accountId, DateTimeOffset? until)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId);
        if (account == null) return;
        account.MutedUntil = until;
        await db.SaveChangesAsync();
    }

    public async Task<bool> SetGmAsync(string normalizedAccount, bool isGm)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == normalizedAccount);
        if (account == null) return false;
        account.IsGm = isGm;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetPasswordHashAsync(string normalizedAccount, string passwordHash)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == normalizedAccount);
        if (account == null) return false;
        account.PasswordHash = passwordHash;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task RecordGmActionAsync(GmActionRecord action)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.GmActions.Add(new GmActionEntity
        {
            GmName = action.GmName, Action = action.Action, Target = action.Target,
            Details = action.Details.Length > 400 ? action.Details[..400] : action.Details, At = action.At,
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<GmActionRecord>> GmActionsAsync(int max = 100)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.GmActions.AsNoTracking().OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(max)
            .Select(a => new GmActionRecord(a.GmName, a.Action, a.Target, a.Details, a.At)).ToListAsync();
    }

    public async Task<AccountRecord> CreateAccountAsync(string normalizedAccount, string passwordHash, string termsVersion)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var account = new AccountEntity
        {
            Name = normalizedAccount, PasswordHash = passwordHash, CreatedAt = now, TermsVersion = termsVersion, TermsAcceptedAt = now,
        };
        db.Accounts.Add(account);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new StoreConflictException(new LocalText("auth.account_exists"));
        }
        return ToRecord(account);
    }

    public async Task AcceptTermsAsync(int accountId, string termsVersion)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId);
        if (account == null) return;
        account.TermsVersion = termsVersion;
        account.TermsAcceptedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAccountAsync(string normalizedAccount)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == normalizedAccount);
        if (account == null) return false;
        var characters = await db.Characters.Where(c => c.AccountId == account.Id).Select(c => c.Id).ToListAsync();
        await db.ItemLog.Where(l => characters.Contains(l.CharacterId)).ExecuteDeleteAsync();
        await db.MetricEvents.Where(m => m.AccountId == account.Id).ExecuteDeleteAsync();
        // Personagens, itens, missões e amigos vão junto (cascata).
        db.Accounts.Remove(account);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<MetricsData> MetricsAsync(DateTimeOffset since)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var accounts = (await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.CreatedAt }).ToListAsync())
            .Select(a => (a.Id, a.CreatedAt)).ToList();
        var events = await db.MetricEvents.AsNoTracking().Where(m => m.At >= since).OrderBy(m => m.Id)
            .Select(m => new MetricRecord(m.AccountId, m.CharacterId, m.Kind, m.Value, m.Details, m.At)).ToListAsync();
        return new MetricsData(accounts, events);
    }

    public async Task UpdateLoginAsync(int accountId, string? newPasswordHash)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId);
        if (account == null) return;
        account.LastLoginAt = DateTimeOffset.UtcNow;
        if (newPasswordHash != null) account.PasswordHash = newPasswordHash;
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<CharacterSummaryRecord>> ListCharactersAsync(int accountId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Characters.AsNoTracking()
            .Where(c => c.AccountId == accountId)
            .OrderBy(c => c.Id)
            .Select(c => new CharacterSummaryRecord(c.Id, c.Name, (CharacterClass)c.ClassId, c.Level, c.Hair, c.Cloth,
                c.Items.Where(i => i.Container == (int)ItemContainerKind.Equipment && i.Slot == (int)EquipSlot.Armor)
                    .Select(i => i.ItemId).FirstOrDefault() ?? "",
                c.Items.Where(i => i.Container == (int)ItemContainerKind.Equipment && i.Slot == (int)EquipSlot.Gloves)
                    .Select(i => i.ItemId).FirstOrDefault() ?? "",
                c.Items.Where(i => i.Container == (int)ItemContainerKind.Equipment && i.Slot == (int)EquipSlot.Boots)
                    .Select(i => i.ItemId).FirstOrDefault() ?? "",
                c.Head, c.Skin, c.HideHelmet))
            .ToListAsync();
    }

    public async Task<CharacterRecord> CreateCharacterAsync(int accountId, string name, CharacterClass characterClass, CharacterRecord initial)
    {
        await using var db = await _factory.CreateDbContextAsync();
        // Transação serializável: dois pedidos ao mesmo tempo não passam do limite de personagens.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var count = await db.Characters.CountAsync(c => c.AccountId == accountId);
        if (count >= AccountRules.MaxCharactersPerAccount)
            throw new StoreConflictException(new LocalText("auth.max_characters", AccountRules.MaxCharactersPerAccount));

        var now = DateTimeOffset.UtcNow;
        var character = new CharacterEntity
        {
            AccountId = accountId, Name = name.Trim(), NameKey = AccountRules.NameKey(name), ClassId = (int)characterClass,
            Level = initial.Level, Xp = initial.Xp, Alz = initial.Alz, PositionX = initial.X, PositionZ = initial.Z,
            Hp = initial.Hp, Mp = initial.Mp, Sp = initial.Sp, CreatedAt = now, SavedAt = now,
            Strength = initial.Attributes.Strength, Dexterity = initial.Attributes.Dexterity,
            Intelligence = initial.Attributes.Intelligence, AwakeningGauge = initial.AwakeningGauge,
            Hair = initial.Hair, Cloth = initial.Cloth, Head = initial.Head, Skin = initial.Skin,
        };
        foreach (var item in initial.Items) character.Items.Add(ToEntity(item, 0));
        db.Characters.Add(character);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new StoreConflictException(new LocalText("auth.name_taken"));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            throw new StoreConflictException(new LocalText("auth.try_again"));
        }
        return ToRecord(character);
    }

    public async Task<CharacterRecord?> LoadCharacterAsync(int characterId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var character = await db.Characters.AsNoTracking().Include(c => c.Items).Include(c => c.Quests).Include(c => c.Relations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == characterId);
        return character == null ? null : ToRecord(character);
    }

    public async Task SaveCharacterAsync(CharacterSnapshot snapshot, IReadOnlyList<ItemLogEntry> logs)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var character = await db.Characters.Include(c => c.Items).Include(c => c.Quests).Include(c => c.Relations).AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == snapshot.CharacterId);
        if (character == null) return;

        character.Level = snapshot.Level;
        character.Xp = snapshot.Xp;
        character.Alz = snapshot.Alz;
        character.PositionX = snapshot.X;
        character.PositionZ = snapshot.Z;
        character.Hp = snapshot.Hp;
        character.Mp = snapshot.Mp;
        character.Sp = snapshot.Sp;
        character.Strength = snapshot.Attributes.Strength;
        character.Dexterity = snapshot.Attributes.Dexterity;
        character.Intelligence = snapshot.Attributes.Intelligence;
        character.AwakeningGauge = snapshot.AwakeningGauge;
        character.LearnedSkills = string.Join(",", snapshot.LearnedSkills);
        character.Hotbar = snapshot.Hotbar.Count == 0 ? "" : string.Join(",", snapshot.Hotbar);
        character.HideHelmet = snapshot.HideHelmet;
        character.SavedAt = DateTimeOffset.UtcNow;

        // Itens e missões: o servidor é a fonte da verdade; o banco fica igual ao que está na memória.
        // Compara pelo id único do item (atualiza, remove o que sumiu, cria o que é novo).
        var wanted = snapshot.Items.ToDictionary(i => i.Uid);
        character.Items.RemoveAll(i => !wanted.ContainsKey(i.Uid));
        var existing = character.Items.ToDictionary(i => i.Uid);
        foreach (var item in snapshot.Items)
        {
            if (existing.TryGetValue(item.Uid, out var row)) Copy(item, row);
            else character.Items.Add(ToEntity(item, character.Id));
        }

        var quests = snapshot.Quests.ToDictionary(q => q.QuestId);
        character.Quests.RemoveAll(q => !quests.ContainsKey(q.QuestId));
        foreach (var quest in snapshot.Quests)
        {
            var row = character.Quests.FirstOrDefault(q => q.QuestId == quest.QuestId);
            if (row == null) character.Quests.Add(row = new CharacterQuestEntity { CharacterId = character.Id, QuestId = quest.QuestId });
            row.Completed = quest.Completed;
            row.Counts = string.Join(",", quest.Counts);
            row.CompletedDay = quest.CompletedDay;
        }

        var relations = snapshot.Relations.ToDictionary(r => ((int)r.Kind, r.OtherId));
        character.Relations.RemoveAll(r => !relations.ContainsKey((r.Kind, r.OtherId)));
        foreach (var relation in snapshot.Relations)
        {
            var row = character.Relations.FirstOrDefault(r => r.Kind == (int)relation.Kind && r.OtherId == relation.OtherId);
            if (row == null)
                character.Relations.Add(row = new CharacterRelationEntity { CharacterId = character.Id, Kind = (int)relation.Kind, OtherId = relation.OtherId });
            row.OtherName = relation.OtherName;
        }

        foreach (var report in snapshot.Reports)
        {
            db.Reports.Add(new PlayerReportEntity
            {
                ReporterId = report.ReporterId, ReporterName = report.ReporterName, TargetId = report.TargetId,
                TargetName = report.TargetName, Reason = report.Reason, ChatExcerpt = report.ChatExcerpt, At = report.At,
            });
        }

        foreach (var metric in snapshot.Metrics)
        {
            db.MetricEvents.Add(new MetricEventEntity
            {
                At = metric.At, AccountId = metric.AccountId, CharacterId = metric.CharacterId, Kind = metric.Kind,
                Value = metric.Value, Details = metric.Details.Length > 400 ? metric.Details[..400] : metric.Details,
            });
        }

        // Mercado: cria ou atualiza cada oferta (sem nunca voltar a situação), na mesma transação da bolsa e do Alz.
        foreach (var listing in snapshot.Market)
        {
            if (listing.SellerCharacterId == 0) continue;
            var row = db.MarketListings.Local.FirstOrDefault(m => m.Id == listing.Id) ?? await db.MarketListings.FindAsync(listing.Id);
            if (row == null)
            {
                // Vendedor apagado (a oferta foi junto, em cascata): não há o que registrar, e inserir quebraria a
                // chave estrangeira e o salvamento inteiro do comprador.
                if (!await db.Characters.AnyAsync(c => c.Id == listing.SellerCharacterId)) continue;
                db.MarketListings.Add(row = new MarketListingEntity { Id = listing.Id, SellerCharacterId = listing.SellerCharacterId });
            }
            else if (!MarketStateOrder.CanReplace((MarketListingState)row.State, listing.State)) continue;
            Copy(listing, row);
        }

        foreach (var log in logs)
        {
            db.ItemLog.Add(new ItemLogEntity
            {
                CharacterId = log.CharacterId, At = log.At, Kind = log.Kind, ItemId = log.ItemId,
                Quantity = log.Quantity, AlzDelta = log.AlzDelta, Details = log.Details,
            });
        }

        // SaveChanges é uma transação: estado, inventário e registros entram juntos ou nenhum entra.
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ItemLogEntry>> ItemLogAsync(int characterId, int max = 100)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.ItemLog.AsNoTracking()
            .Where(l => l.CharacterId == characterId)
            .OrderByDescending(l => l.Id)
            .Take(max)
            .Select(l => new ItemLogEntry(l.CharacterId, l.At, l.Kind, l.ItemId, l.Quantity, l.AlzDelta, l.Details))
            .ToListAsync();
    }

    public async Task<long> MaxItemUidAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var items = await db.Items.MaxAsync(i => (long?)i.Uid) ?? 0;
        var listings = await db.MarketListings.MaxAsync(m => (long?)m.Id) ?? 0;
        var listedItems = await db.MarketListings.MaxAsync(m => (long?)m.ItemUid) ?? 0;
        return Math.Max(items, Math.Max(listings, listedItems));
    }

    public async Task<IReadOnlyList<MarketListingRecord>> MarketListingsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var active = (int)MarketListingState.Active;
        var sold = (int)MarketListingState.Sold;
        var rows = await db.MarketListings.AsNoTracking().Where(m => m.State == active || m.State == sold).OrderBy(m => m.Id).ToListAsync();
        return rows.Select(ToRecord).ToList();
    }

    private static void Copy(MarketListingRecord listing, MarketListingEntity row)
    {
        row.SellerName = listing.SellerName;
        row.ItemUid = listing.Item.Uid;
        row.ItemId = listing.Item.ItemId;
        row.Quantity = listing.Item.Quantity;
        row.Upgrade = listing.Item.Upgrade;
        row.PityFails = listing.Item.PityFails;
        row.PityLevel = listing.Item.PityLevel;
        row.RegainGuaranteed = listing.Item.RegainGuaranteed;
        row.OptionId = listing.Item.OptionId;
        row.Price = listing.Price;
        row.State = (int)listing.State;
        row.BuyerName = listing.BuyerName;
        row.CreatedAt = listing.CreatedAt;
        row.ExpiresAt = listing.ExpiresAt;
        row.SoldAt = listing.SoldAt;
    }

    private static MarketListingRecord ToRecord(MarketListingEntity row) => new(
        row.Id, row.SellerCharacterId, row.SellerName,
        new ItemRecord(row.ItemUid, ItemContainerKind.Bag, 0, row.ItemId, row.Quantity, row.Upgrade, row.PityFails, row.PityLevel,
            row.RegainGuaranteed, row.OptionId),
        row.Price, (MarketListingState)row.State, row.BuyerName, row.CreatedAt, row.ExpiresAt, row.SoldAt);

    public async Task RecordDungeonRunAsync(DungeonRunRecord run)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.DungeonRuns.Add(new DungeonRunEntity
        {
            DungeonId = run.DungeonId, Seconds = run.Seconds, Medal = run.Medal, MaxCombo = run.MaxCombo, Members = run.Members, At = run.At,
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<DungeonRunRecord>> TopDungeonRunsAsync(string dungeonId, int max = 10)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DungeonRuns.AsNoTracking()
            .Where(r => r.DungeonId == dungeonId)
            .OrderBy(r => r.Seconds).ThenBy(r => r.Id)
            .Take(max)
            .Select(r => new DungeonRunRecord(r.DungeonId, r.Seconds, r.Medal, r.MaxCombo, r.Members, r.At))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ReportRecord>> ReportsAsync(int max = 100)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Reports.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(max)
            .Select(r => new ReportRecord(r.ReporterId, r.ReporterName, r.TargetId, r.TargetName, r.Reason, r.ChatExcerpt, r.At))
            .ToListAsync();
    }

    private static CharacterRecord ToRecord(CharacterEntity c) => new(
        c.Id, c.AccountId, c.Name, (CharacterClass)c.ClassId, c.Level, c.Xp, c.Alz, c.PositionX, c.PositionZ,
        c.Hp, c.Mp, c.Sp,
        c.Items.OrderBy(i => i.Uid).Select(i => new ItemRecord(i.Uid, (ItemContainerKind)i.Container, i.Slot, i.ItemId, i.Quantity,
            i.Upgrade, i.PityFails, i.PityLevel, i.RegainGuaranteed, i.OptionId)).ToList(),
        new Attributes { Strength = c.Strength, Dexterity = c.Dexterity, Intelligence = c.Intelligence },
        c.AwakeningGauge,
        c.Quests.Select(q => new QuestRecord(q.QuestId, q.Completed, ParseCounts(q.Counts), q.CompletedDay)).ToList())
    {
        Relations = c.Relations.OrderBy(r => r.Kind).ThenBy(r => r.OtherName)
            .Select(r => new RelationRecord((RelationKind)r.Kind, r.OtherId, r.OtherName)).ToList(),
        LearnedSkills = string.IsNullOrEmpty(c.LearnedSkills) ? Array.Empty<string>() : c.LearnedSkills.Split(',', StringSplitOptions.RemoveEmptyEntries),
        Hotbar = string.IsNullOrEmpty(c.Hotbar) ? Array.Empty<string>() : c.Hotbar.Split(','),
        Hair = c.Hair, Cloth = c.Cloth, Head = c.Head, Skin = c.Skin, HideHelmet = c.HideHelmet,
    };

    private static int[] ParseCounts(string counts) => string.IsNullOrEmpty(counts)
        ? Array.Empty<int>()
        : counts.Split(',').Select(c => int.TryParse(c, out var n) ? n : 0).ToArray();

    private static ItemInstanceEntity ToEntity(ItemRecord item, int characterId)
    {
        var row = new ItemInstanceEntity { Uid = item.Uid, CharacterId = characterId };
        Copy(item, row);
        return row;
    }

    private static void Copy(ItemRecord item, ItemInstanceEntity row)
    {
        row.Container = (int)item.Container;
        row.Slot = item.Slot;
        row.ItemId = item.ItemId;
        row.Quantity = item.Quantity;
        row.Upgrade = item.Upgrade;
        row.PityFails = item.PityFails;
        row.PityLevel = item.PityLevel;
        row.RegainGuaranteed = item.RegainGuaranteed;
        row.OptionId = item.OptionId ?? "";
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

/// <summary>Banco em memória: testes e desenvolvimento sem PostgreSQL (tudo se perde ao desligar).</summary>
public sealed class InMemoryGameStore : IGameStore
{
    private readonly object _lock = new();
    private readonly Dictionary<int, AccountRecord> _accounts = new();
    private readonly Dictionary<int, CharacterRecord> _characters = new();
    private readonly List<ItemLogEntry> _log = new();
    private readonly List<ReportRecord> _reports = new();
    private readonly List<DungeonRunRecord> _runs = new();
    private readonly List<MetricRecord> _metrics = new();
    private readonly Dictionary<long, MarketListingRecord> _market = new();
    private readonly Dictionary<int, DateTimeOffset> _accountCreated = new();
    private int _nextAccountId = 1;
    private int _nextCharacterId = 1;

    /// <summary>Quantas vezes algo foi salvo (testes).</summary>
    public int SaveCount { get; private set; }

    public Task<AccountRecord?> FindAccountAsync(string normalizedAccount)
    {
        lock (_lock) return Task.FromResult(_accounts.Values.FirstOrDefault(a => a.Name == normalizedAccount));
    }

    private readonly List<GmActionRecord> _gmActions = new();

    public Task<AccountRecord?> FindAccountByIdAsync(int accountId)
    {
        lock (_lock) return Task.FromResult(_accounts.TryGetValue(accountId, out var account) ? account : null);
    }

    public Task<(int AccountId, string CharacterName)?> FindCharacterOwnerAsync(string characterName)
    {
        var key = AccountRules.NameKey(characterName);
        lock (_lock)
        {
            var character = _characters.Values.FirstOrDefault(c => AccountRules.NameKey(c.Name) == key);
            return Task.FromResult(character == null ? ((int, string)?)null : (character.AccountId, character.Name));
        }
    }

    public Task SetBanAsync(int accountId, DateTimeOffset? until, string reason)
    {
        lock (_lock)
            if (_accounts.TryGetValue(accountId, out var account))
                _accounts[accountId] = account with { BannedUntil = until, BanReason = until == null ? "" : reason };
        return Task.CompletedTask;
    }

    public Task SetMuteAsync(int accountId, DateTimeOffset? until)
    {
        lock (_lock)
            if (_accounts.TryGetValue(accountId, out var account)) _accounts[accountId] = account with { MutedUntil = until };
        return Task.CompletedTask;
    }

    public Task<bool> SetGmAsync(string normalizedAccount, bool isGm)
    {
        lock (_lock)
        {
            var account = _accounts.Values.FirstOrDefault(a => a.Name == normalizedAccount);
            if (account == null) return Task.FromResult(false);
            _accounts[account.Id] = account with { IsGm = isGm };
            return Task.FromResult(true);
        }
    }

    public Task<bool> SetPasswordHashAsync(string normalizedAccount, string passwordHash)
    {
        lock (_lock)
        {
            var account = _accounts.Values.FirstOrDefault(a => a.Name == normalizedAccount);
            if (account == null) return Task.FromResult(false);
            _accounts[account.Id] = account with { PasswordHash = passwordHash };
            return Task.FromResult(true);
        }
    }

    public Task RecordGmActionAsync(GmActionRecord action)
    {
        lock (_lock) _gmActions.Add(action);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GmActionRecord>> GmActionsAsync(int max = 100)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<GmActionRecord>>(_gmActions.AsEnumerable().Reverse().Take(max).ToList());
    }

    public Task<AccountRecord> CreateAccountAsync(string normalizedAccount, string passwordHash, string termsVersion)
    {
        lock (_lock)
        {
            if (_accounts.Values.Any(a => a.Name == normalizedAccount)) throw new StoreConflictException(new LocalText("auth.account_exists"));
            var account = new AccountRecord(_nextAccountId++, normalizedAccount, passwordHash) { TermsVersion = termsVersion };
            _accounts.Add(account.Id, account);
            _accountCreated[account.Id] = DateTimeOffset.UtcNow;
            return Task.FromResult(account);
        }
    }

    public Task AcceptTermsAsync(int accountId, string termsVersion)
    {
        lock (_lock)
        {
            if (_accounts.TryGetValue(accountId, out var account)) _accounts[accountId] = account with { TermsVersion = termsVersion };
        }
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAccountAsync(string normalizedAccount)
    {
        lock (_lock)
        {
            var account = _accounts.Values.FirstOrDefault(a => a.Name == normalizedAccount);
            if (account == null) return Task.FromResult(false);
            var characters = _characters.Values.Where(c => c.AccountId == account.Id).Select(c => c.Id).ToHashSet();
            foreach (var id in characters) _characters.Remove(id);
            foreach (var listing in _market.Values.Where(m => characters.Contains(m.SellerCharacterId)).ToList()) _market.Remove(listing.Id);
            _log.RemoveAll(l => characters.Contains(l.CharacterId));
            _metrics.RemoveAll(m => m.AccountId == account.Id);
            _accounts.Remove(account.Id);
            _accountCreated.Remove(account.Id);
            return Task.FromResult(true);
        }
    }

    public Task<MetricsData> MetricsAsync(DateTimeOffset since)
    {
        lock (_lock)
        {
            return Task.FromResult(new MetricsData(
                _accountCreated.Select(a => (a.Key, a.Value)).ToList(), _metrics.Where(m => m.At >= since).ToList()));
        }
    }

    public Task UpdateLoginAsync(int accountId, string? newPasswordHash)
    {
        lock (_lock)
        {
            if (newPasswordHash != null && _accounts.TryGetValue(accountId, out var account))
                _accounts[accountId] = account with { PasswordHash = newPasswordHash };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CharacterSummaryRecord>> ListCharactersAsync(int accountId)
    {
        lock (_lock)
        {
            IReadOnlyList<CharacterSummaryRecord> list = _characters.Values.Where(c => c.AccountId == accountId).OrderBy(c => c.Id)
                .Select(c => new CharacterSummaryRecord(c.Id, c.Name, c.Class, c.Level, c.Hair, c.Cloth,
                    Worn(c, EquipSlot.Armor), Worn(c, EquipSlot.Gloves), Worn(c, EquipSlot.Boots), c.Head, c.Skin, c.HideHelmet)).ToList();
            return Task.FromResult(list);
        }
    }

    private static string Worn(CharacterRecord c, EquipSlot slot) =>
        c.Items.FirstOrDefault(i => i.Container == ItemContainerKind.Equipment && i.Slot == (int)slot)?.ItemId ?? "";

    public Task<CharacterRecord> CreateCharacterAsync(int accountId, string name, CharacterClass characterClass, CharacterRecord initial)
    {
        lock (_lock)
        {
            if (_characters.Values.Count(c => c.AccountId == accountId) >= AccountRules.MaxCharactersPerAccount)
                throw new StoreConflictException(new LocalText("auth.max_characters", AccountRules.MaxCharactersPerAccount));
            var key = AccountRules.NameKey(name);
            if (_characters.Values.Any(c => AccountRules.NameKey(c.Name) == key)) throw new StoreConflictException(new LocalText("auth.name_taken"));
            var character = initial with { Id = _nextCharacterId++, AccountId = accountId, Name = name.Trim(), Class = characterClass };
            _characters.Add(character.Id, character);
            return Task.FromResult(character);
        }
    }

    public Task<CharacterRecord?> LoadCharacterAsync(int characterId)
    {
        lock (_lock) return Task.FromResult(_characters.TryGetValue(characterId, out var c) ? c : null);
    }

    public Task SaveCharacterAsync(CharacterSnapshot s, IReadOnlyList<ItemLogEntry> logs)
    {
        lock (_lock)
        {
            if (!_characters.TryGetValue(s.CharacterId, out var c)) return Task.CompletedTask;
            _characters[s.CharacterId] = c with
            {
                Level = s.Level, Xp = s.Xp, Alz = s.Alz, X = s.X, Z = s.Z, Hp = s.Hp, Mp = s.Mp, Sp = s.Sp,
                Items = s.Items.ToList(), Attributes = s.Attributes, AwakeningGauge = s.AwakeningGauge,
                Quests = s.Quests.Select(q => q with { Counts = q.Counts.ToArray() }).ToList(),
                Relations = s.Relations.ToList(),
                LearnedSkills = s.LearnedSkills.ToList(), Hotbar = s.Hotbar.ToList(), HideHelmet = s.HideHelmet,
            };
            _log.AddRange(logs);
            _reports.AddRange(s.Reports);
            _metrics.AddRange(s.Metrics);
            foreach (var listing in s.Market)
            {
                if (listing.SellerCharacterId == 0) continue;
                if (_market.TryGetValue(listing.Id, out var current) && !MarketStateOrder.CanReplace(current.State, listing.State)) continue;
                _market[listing.Id] = listing;
            }
            SaveCount++;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ItemLogEntry>> ItemLogAsync(int characterId, int max = 100)
    {
        lock (_lock)
        {
            IReadOnlyList<ItemLogEntry> list = _log.Where(l => l.CharacterId == characterId).Reverse().Take(max).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<long> MaxItemUidAsync()
    {
        lock (_lock)
        {
            var items = _characters.Values.SelectMany(c => c.Items).Select(i => i.Uid)
                .Concat(_market.Values.SelectMany(m => new[] { m.Id, m.Item.Uid }));
            return Task.FromResult(items.DefaultIfEmpty(0).Max());
        }
    }

    public Task<IReadOnlyList<MarketListingRecord>> MarketListingsAsync()
    {
        lock (_lock)
        {
            IReadOnlyList<MarketListingRecord> list = _market.Values
                .Where(m => m.State is MarketListingState.Active or MarketListingState.Sold).OrderBy(m => m.Id).ToList();
            return Task.FromResult(list);
        }
    }

    /// <summary>Todas as ofertas guardadas, em qualquer situação (testes).</summary>
    public IReadOnlyList<MarketListingRecord> AllMarketListings()
    {
        lock (_lock) return _market.Values.OrderBy(m => m.Id).ToList();
    }

    public Task RecordDungeonRunAsync(DungeonRunRecord run)
    {
        lock (_lock) _runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DungeonRunRecord>> TopDungeonRunsAsync(string dungeonId, int max = 10)
    {
        lock (_lock)
        {
            IReadOnlyList<DungeonRunRecord> list = _runs.Where(r => r.DungeonId == dungeonId).OrderBy(r => r.Seconds).Take(max).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<IReadOnlyList<ReportRecord>> ReportsAsync(int max = 100)
    {
        lock (_lock)
        {
            IReadOnlyList<ReportRecord> list = Enumerable.Reverse(_reports).Take(max).ToList();
            return Task.FromResult(list);
        }
    }
}
