using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Game.Server.Tests;

/// <summary>
/// Testes do banco PostgreSQL de verdade. Só rodam com a variável GAME_DB_TEST apontando para um banco de teste
/// (ex.: <c>Host=localhost;Port=5432;Database=game_tests;Username=game;Password=game_dev_local</c>);
/// sem ela passam sem fazer nada, para o <c>dotnet test</c> funcionar sem Docker.
/// O banco de teste é apagado e recriado a cada teste.
/// </summary>
[Collection("Postgres")]
public class PostgresStoreTests
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("GAME_DB_TEST");

    private static async Task<EfGameStore?> FreshStoreAsync()
    {
        if (string.IsNullOrEmpty(Connection)) return null;
        if (!Connection.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GAME_DB_TEST precisa ser um banco de teste (o nome deve conter 'test').");

        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(Connection).Options;
        await using (var db = new GameDbContext(options)) await db.Database.EnsureDeletedAsync();
        var store = new EfGameStore(new PooledDbContextFactory<GameDbContext>(options));
        await store.MigrateAsync();
        return store;
    }

    [Fact]
    public async Task Contas_e_nomes_unicos_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;

        var account = await store.CreateAccountAsync("lucas", "hash", "");
        await Assert.ThrowsAsync<StoreConflictException>(() => store.CreateAccountAsync("lucas", "outro", ""));

        await store.CreateCharacterAsync(account.Id, "Ação", CharacterClass.Blade, AuthService.InitialCharacter());
        await Assert.ThrowsAsync<StoreConflictException>(() =>
            store.CreateCharacterAsync(account.Id, "acao", CharacterClass.Arcanist, AuthService.InitialCharacter()));
    }

    [Fact]
    public async Task Termos_metricas_e_exclusao_de_conta_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var account = await store.CreateAccountAsync("lucas", "hash", "v1");
        var other = await store.CreateAccountAsync("bruna", "hash", "v1");
        Assert.Equal("v1", (await store.FindAccountAsync("lucas"))!.TermsVersion);
        await store.AcceptTermsAsync(account.Id, "v2");
        Assert.Equal("v2", (await store.FindAccountAsync("lucas"))!.TermsVersion);

        var ana = await store.CreateCharacterAsync(account.Id, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        var bia = await store.CreateCharacterAsync(other.Id, "Bia", CharacterClass.Blade, AuthService.InitialCharacter());
        foreach (var (who, owner) in new[] { (ana, account), (bia, other) })
        {
            await store.SaveCharacterAsync(new CharacterSnapshot(who.Id, 2, 0, 10, 0, 0, 100, 100, 100, Array.Empty<ItemRecord>())
            {
                Metrics = new[] { new MetricRecord(owner.Id, who.Id, "session", 60, "nivel=1>2", DateTimeOffset.UtcNow) },
            }, new[] { new ItemLogEntry(who.Id, DateTimeOffset.UtcNow, "alz_monstro", null, 0, 10, "") });
        }
        var before = await store.MetricsAsync(DateTimeOffset.UtcNow.AddHours(-1));
        Assert.Equal((2, 2), (before.Accounts.Count, before.Events.Count));

        Assert.True(await store.DeleteAccountAsync("lucas"));
        Assert.Null(await store.FindAccountAsync("lucas"));
        Assert.Null(await store.LoadCharacterAsync(ana.Id));
        Assert.Empty(await store.ItemLogAsync(ana.Id));
        var after = await store.MetricsAsync(DateTimeOffset.MinValue);
        Assert.Equal(other.Id, Assert.Single(after.Events).AccountId);
        Assert.NotNull(await store.LoadCharacterAsync(bia.Id));
        Assert.Single(await store.ItemLogAsync(bia.Id));
    }

    [Fact]
    public async Task Limite_de_tres_personagens_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var account = await store.CreateAccountAsync("lucas", "hash", "");

        foreach (var name in new[] { "Ana", "Bia", "Caio" })
            await store.CreateCharacterAsync(account.Id, name, CharacterClass.Blade, AuthService.InitialCharacter());

        await Assert.ThrowsAsync<StoreConflictException>(() =>
            store.CreateCharacterAsync(account.Id, "Duda", CharacterClass.Blade, AuthService.InitialCharacter()));
        Assert.Equal(3, (await store.ListCharactersAsync(account.Id)).Count);
    }

    [Fact]
    public async Task Salvar_e_carregar_estado_inventario_e_registros()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var account = await store.CreateAccountAsync("lucas", "hash", "");
        var character = await store.CreateCharacterAsync(account.Id, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        var uid = await store.MaxItemUidAsync() + 1;

        await store.SaveCharacterAsync(new CharacterSnapshot(character.Id, 7, 120, 5000, 12.5f, 40f, 900, 300, 1500,
                new[]
                {
                    new ItemRecord(uid, ItemContainerKind.Bag, 0, "pelo_macio", 3),
                    new ItemRecord(uid + 1, ItemContainerKind.Bag, 1, "nucleo_baixo", 1),
                    new ItemRecord(uid + 2, ItemContainerKind.Equipment, 0, "espada_ferro", 1, Upgrade: 6, PityFails: 2, PityLevel: 6),
                },
                Attributes: new Attributes { Strength = 15, Dexterity = 6, Intelligence = 2 }, AwakeningGauge: 40,
                Quests: new[] { new QuestRecord("primeiros_passos", true, Array.Empty<int>()), new QuestRecord("arte_do_combo", false, new[] { 4 }) }),
            new[] { new ItemLogEntry(character.Id, DateTimeOffset.UtcNow, "coleta", "pelo_macio", 3, 0, "teste") });
        // Segundo salvamento: um item some, outro muda de quantidade.
        await store.SaveCharacterAsync(new CharacterSnapshot(character.Id, 8, 10, 5100, 13f, 41f, 950, 320, 1400,
                new[]
                {
                    new ItemRecord(uid, ItemContainerKind.Bag, 5, "pelo_macio", 5),
                    new ItemRecord(uid + 2, ItemContainerKind.Equipment, 0, "espada_ferro", 1, Upgrade: 7),
                },
                Quests: new[] { new QuestRecord("primeiros_passos", true, Array.Empty<int>()), new QuestRecord("arte_do_combo", false, new[] { 9 }) })
            {
                LearnedSkills = new[] { "corte", "giro_de_lamina" },
                Hotbar = new[] { "giro_de_lamina", "", "corte", "", "", "", "", "" },
            },
            new[] { new ItemLogEntry(character.Id, DateTimeOffset.UtcNow, "alz_monstro", null, 0, 100, "teste") });

        var loaded = await store.LoadCharacterAsync(character.Id);
        Assert.Equal((8, 10, 5100L, 13f, 950), (loaded!.Level, loaded.Xp, loaded.Alz, loaded.X, loaded.Hp));
        Assert.Equal(2, loaded.Items.Count);
        var pelt = loaded.Items.Single(i => i.Uid == uid);
        Assert.Equal(("pelo_macio", 5, 5), (pelt.ItemId, pelt.Quantity, pelt.Slot)); // mudou de posição e quantidade
        Assert.Equal(7, loaded.Items.Single(i => i.Uid == uid + 2).Upgrade);
        Assert.Equal(9, loaded.Quests.Single(q => q.QuestId == "arte_do_combo").Counts[0]);
        Assert.True(loaded.Quests.Single(q => q.QuestId == "primeiros_passos").Completed);
        Assert.Equal(new[] { "corte", "giro_de_lamina" }, loaded.LearnedSkills);
        Assert.Equal(new[] { "giro_de_lamina", "", "corte", "", "", "", "", "" }, loaded.Hotbar);
        Assert.True(await store.MaxItemUidAsync() >= uid + 2);
        Assert.Equal(2, (await store.ItemLogAsync(character.Id)).Count);
    }

    [Fact]
    public async Task Manter_conectado_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var auth = new AuthService(store, new PasswordHasher(1000), new SessionTokens());
        await auth.RegisterAsync(new Credentials { Account = "lucas", Password = "senha123" });
        var account = (await store.FindAccountAsync("lucas"))!;

        var tokens = new List<string>();
        for (var i = 0; i < DeviceTokens.MaxPerAccount + 1; i++)
            tokens.Add((await auth.LoginAsync(new Credentials { Account = "lucas", Password = "senha123", Remember = true })).Value!.DeviceToken);
        Assert.Null(await store.FindDeviceTokenAsync(DeviceTokens.Hash(tokens[0]), DateTimeOffset.UtcNow));
        Assert.Equal(account.Id, await store.FindDeviceTokenAsync(DeviceTokens.Hash(tokens[^1]), DateTimeOffset.UtcNow));
        Assert.Null(await store.FindDeviceTokenAsync(DeviceTokens.Hash(tokens[^1]), DateTimeOffset.UtcNow.AddDays(31)));

        var entered = await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = tokens[^1] });
        Assert.True(entered.Ok);
        Assert.False((await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = tokens[^1] })).Ok);

        Assert.Null(await auth.ResetPasswordAsync("lucas", "senhanova1"));
        Assert.False((await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = entered.Value!.DeviceToken })).Ok);

        var again = (await auth.LoginAsync(new Credentials { Account = "lucas", Password = "senhanova1", Remember = true })).Value!.DeviceToken;
        await store.SetBanAsync(account.Id, DateTimeOffset.UtcNow.AddDays(1), "teste");
        Assert.Null(await store.FindDeviceTokenAsync(DeviceTokens.Hash(again), DateTimeOffset.UtcNow));

        await store.AddDeviceTokenAsync(account.Id, DeviceTokens.Hash("x"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 5);
        Assert.True(await store.DeleteAccountAsync("lucas"));
        Assert.False(await store.RemoveDeviceTokenAsync(DeviceTokens.Hash("x")));
    }

    [Fact]
    public async Task Fluxo_completo_de_conta_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var auth = new AuthService(store, new PasswordHasher(1000), new SessionTokens());

        Assert.True((await auth.RegisterAsync(new Credentials { Account = "lucas", Password = "senha123" })).Ok);
        var token = (await auth.LoginAsync(new Credentials { Account = "lucas", Password = "senha123" })).Value!.Token;
        var created = await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Guerreira", ClassId = 0 });

        Assert.True(created.Ok);
        Assert.True((await auth.ResolveCharacterAsync(token, created.Value!.Id)).Ok);
    }

    [Fact]
    public async Task Mercado_salva_com_o_personagem_e_nunca_volta_a_situacao_no_banco_real()
    {
        var store = await FreshStoreAsync();
        if (store == null) return;
        var account = await store.CreateAccountAsync("lucas", "hash", "");
        var ana = await store.CreateCharacterAsync(account.Id, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        var now = DateTimeOffset.UtcNow;
        var item = new ItemRecord(9001, ItemContainerKind.Bag, 0, "espada_aco_negro", 1, Upgrade: 4, OptionId: "pedra_furia");
        var active = new MarketListingRecord(9002, ana.Id, "Ana", item, 5000, MarketListingState.Active, "", now, now.AddHours(72), null);
        var sold = active with { State = MarketListingState.Sold, BuyerName = "Bia", SoldAt = now };
        CharacterSnapshot Snapshot(MarketListingRecord listing) =>
            new(ana.Id, 1, 0, 0, 0, 0, 100, 100, 100, Array.Empty<ItemRecord>()) { Market = new[] { listing } };

        await store.SaveCharacterAsync(Snapshot(active), Array.Empty<ItemLogEntry>());
        await store.SaveCharacterAsync(Snapshot(sold), Array.Empty<ItemLogEntry>());
        await store.SaveCharacterAsync(Snapshot(active), Array.Empty<ItemLogEntry>()); // atrasado: não volta para ativa

        var loaded = Assert.Single(await store.MarketListingsAsync());
        Assert.Equal((MarketListingState.Sold, "Bia", 4, "pedra_furia"), (loaded.State, loaded.BuyerName, loaded.Item.Upgrade, loaded.Item.OptionId));
        Assert.True(await store.MaxItemUidAsync() >= 9002);

        await store.SaveCharacterAsync(Snapshot(sold with { State = MarketListingState.Collected }), Array.Empty<ItemLogEntry>());
        Assert.Empty(await store.MarketListingsAsync());
        Assert.True(await store.DeleteAccountAsync("lucas")); // a oferta vai junto (cascata)
    }
}
