using System.Net.Sockets;
using Game.Server.Accounts;
using Game.Server.Net;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Net;

namespace Game.Server.Tests;

public class PersistenceTests
{
    private sealed class TestClient : IDisposable
    {
        private readonly TcpClient _tcp = new();
        private readonly FrameBuffer _frames = new();
        private readonly byte[] _buffer = new byte[16384];
        private NetworkStream _stream = null!;

        public async Task ConnectAsync(int port)
        {
            await _tcp.ConnectAsync("127.0.0.1", port);
            _stream = _tcp.GetStream();
        }

        public Task SendAsync(INetMessage message) => _stream.WriteAsync(NetCodec.Encode(message)).AsTask();

        public async Task<INetMessage> ReceiveFirstAsync(params Type[] types)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                while (_frames.TryRead(out var type, out var payload))
                {
                    var message = NetCodec.Decode(type, payload);
                    if (types.Contains(message.GetType())) return message;
                }
                var read = await _stream.ReadAsync(_buffer, cts.Token);
                if (read == 0) throw new IOException("Servidor fechou a conexão.");
                _frames.Append(_buffer, 0, read);
            }
        }

        public void Dispose() => _tcp.Dispose();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public InMemoryGameStore Store { get; } = new();
        public AuthService Auth { get; }
        public CharacterSaver Saver { get; }
        public ServerHost Host { get; }

        public Fixture(bool allowGuests = false)
        {
            Auth = new AuthService(Store, new PasswordHasher(1000), new SessionTokens());
            Saver = new CharacterSaver(Store);
            Host = new ServerHost(new ServerOptions { Port = 0, AllowGuests = allowGuests, SaveIntervalSeconds = 0 }, null, Auth, Saver);
            Host.Start();
        }

        public async Task<(string Token, int CharacterId)> AccountWithCharacterAsync(string account = "lucas", string name = "Ana")
        {
            await Auth.RegisterAsync(new Credentials { Account = account, Password = "senha123" });
            var token = (await Auth.LoginAsync(new Credentials { Account = account, Password = "senha123" })).Value!.Token;
            var character = (await Auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = name })).Value!;
            return (token, character.Id);
        }

        public async ValueTask DisposeAsync()
        {
            Host.Stop();
            await Saver.DisposeAsync();
        }
    }

    [Fact]
    public async Task Personagem_volta_como_estava_e_eh_salvo_ao_sair()
    {
        await using var f = new Fixture();
        var (token, characterId) = await f.AccountWithCharacterAsync();
        // Estado salvo antes: nível 5, 1234 Alz e um item.
        await f.Store.SaveCharacterAsync(new CharacterSnapshot(characterId, 5, 40, 1234, 3, 4, 100, 50, 900,
            new[] { new ItemRecord(500, ItemContainerKind.Bag, 3, "pelo_macio", 7) }), Array.Empty<ItemLogEntry>());

        using (var client = new TestClient())
        {
            await client.ConnectAsync(f.Host.Port);
            await client.SendAsync(new Hello { Token = token, CharacterId = characterId });
            Assert.IsType<Welcome>(await client.ReceiveFirstAsync(typeof(Welcome), typeof(Reject)));
            var stats = (PlayerStats)await client.ReceiveFirstAsync(typeof(PlayerStats));

            Assert.Equal((5, 40, 1234L, 900), (stats.Level, stats.Xp, stats.Alz, stats.Sp));
        }

        // Saiu: o servidor salva sozinho. Espera o salvamento chegar ao banco.
        for (var i = 0; i < 50 && f.Store.SaveCount < 2; i++) await Task.Delay(50);
        var saved = await f.Store.LoadCharacterAsync(characterId);
        Assert.True(f.Store.SaveCount >= 2);
        Assert.Equal((5, 1234L), (saved!.Level, saved.Alz));
        var pelt = Assert.Single(saved.Items, i => i.ItemId == "pelo_macio");
        Assert.Equal((500L, 3, 7), (pelt.Uid, pelt.Slot, pelt.Quantity)); // mesmo item, mesma posição
    }

    [Fact]
    public async Task Mesmo_personagem_nao_entra_duas_vezes()
    {
        await using var f = new Fixture();
        var (token, characterId) = await f.AccountWithCharacterAsync();
        using var first = new TestClient();
        using var second = new TestClient();
        await first.ConnectAsync(f.Host.Port);
        await first.SendAsync(new Hello { Token = token, CharacterId = characterId });
        await first.ReceiveFirstAsync(typeof(Welcome));

        await second.ConnectAsync(f.Host.Port);
        await second.SendAsync(new Hello { Token = token, CharacterId = characterId });

        var reject = Assert.IsType<Reject>(await second.ReceiveFirstAsync(typeof(Welcome), typeof(Reject)));
        Assert.Contains("já está no jogo", reject.Reason.ToString());
    }

    [Theory]
    [InlineData("", 0, "Entre com sua conta")]
    [InlineData("token-inventado", 1, "Sessão expirada")]
    public async Task Sem_conta_valida_nao_entra(string token, int characterId, string expected)
    {
        await using var f = new Fixture(allowGuests: false);
        using var client = new TestClient();
        await client.ConnectAsync(f.Host.Port);

        await client.SendAsync(new Hello { Name = "Intruso", Token = token, CharacterId = characterId });

        var reject = Assert.IsType<Reject>(await client.ReceiveFirstAsync(typeof(Welcome), typeof(Reject)));
        Assert.Contains(expected, reject.Reason.ToString());
    }

    [Fact]
    public async Task Personagem_de_outra_conta_nao_entra()
    {
        await using var f = new Fixture();
        var (_, characterOfA) = await f.AccountWithCharacterAsync("conta_a", "Ana");
        var (tokenB, _) = await f.AccountWithCharacterAsync("conta_b", "Bia");
        using var client = new TestClient();
        await client.ConnectAsync(f.Host.Port);

        await client.SendAsync(new Hello { Token = tokenB, CharacterId = characterOfA });

        Assert.IsType<Reject>(await client.ReceiveFirstAsync(typeof(Welcome), typeof(Reject)));
    }

    [Fact]
    public async Task Desligar_o_servidor_salva_quem_esta_conectado()
    {
        var f = new Fixture();
        var (token, characterId) = await f.AccountWithCharacterAsync();
        using var client = new TestClient();
        await client.ConnectAsync(f.Host.Port);
        await client.SendAsync(new Hello { Token = token, CharacterId = characterId });
        await client.ReceiveFirstAsync(typeof(Welcome));
        var before = f.Store.SaveCount;

        await f.DisposeAsync();

        Assert.True(f.Store.SaveCount > before);
    }
}

public class SaveAndLogTests
{
    private sealed class Outbox : IOutbox
    {
        public void Send(int playerId, INetMessage message) { }
    }

    [Fact]
    public void Coleta_vira_registro_e_vai_junto_no_proximo_salvamento()
    {
        var world = new GameWorld(new Outbox(), Array.Empty<Game.Shared.World.SpawnDefinition>());
        var record = AuthService.InitialCharacter() with { Id = 9, AccountId = 1, Name = "Ana" };
        var player = world.AddPlayer("", 0, null, record);
        player.Bag.TryAdd("pelo_macio", 2, () => 77);
        player.PendingLogs.Add(new ItemLogEntry(9, DateTimeOffset.UtcNow, "coleta", "pelo_macio", 2, 0, "teste"));

        var (snapshot, logs) = world.CaptureForSave(player);

        Assert.Equal(9, snapshot.CharacterId);
        Assert.Equal(2, Assert.Single(snapshot.Items, i => i.Uid == 77).Quantity);
        Assert.Single(logs);
        Assert.Empty(player.PendingLogs); // não repete no próximo salvamento
        Assert.Equal("Ana", player.Name);
    }

    [Fact]
    public void Morto_eh_salvo_vivo_na_vila()
    {
        var world = new GameWorld(new Outbox(), Array.Empty<Game.Shared.World.SpawnDefinition>());
        var player = world.AddPlayer("", 0, null, AuthService.InitialCharacter() with { Id = 3, X = 20, Z = 30 });
        player.IsDead = true;
        player.Hp.Add(-99999);

        var (snapshot, _) = world.CaptureForSave(player);

        Assert.Equal(player.Hp.Max, snapshot.Hp);
        Assert.InRange(MathF.Sqrt(snapshot.X * snapshot.X + snapshot.Z * snapshot.Z), 0, 3.1f);
    }

    [Fact]
    public void Posicao_salva_estragada_nasce_na_vila()
    {
        var world = new GameWorld(new Outbox(), Array.Empty<Game.Shared.World.SpawnDefinition>());

        var player = world.AddPlayer("", 0, null, AuthService.InitialCharacter() with { Id = 3, X = float.NaN, Z = 99999 });

        Assert.InRange(MathF.Sqrt(player.X * player.X + player.Z * player.Z), 0, 3.1f);
    }

    /// <summary>Banco que falha as primeiras N vezes.</summary>
    private sealed class FlakyStore : IGameStore
    {
        private readonly InMemoryGameStore _inner = new();
        public int FailuresLeft = 2;
        public InMemoryGameStore Inner => _inner;

        public Task SaveCharacterAsync(CharacterSnapshot snapshot, IReadOnlyList<ItemLogEntry> logs)
        {
            if (FailuresLeft-- > 0) throw new IOException("banco fora do ar");
            return _inner.SaveCharacterAsync(snapshot, logs);
        }

        public Task<AccountRecord?> FindAccountAsync(string a) => _inner.FindAccountAsync(a);
        public Task<AccountRecord> CreateAccountAsync(string a, string h, string t) => _inner.CreateAccountAsync(a, h, t);
        public Task AcceptTermsAsync(int id, string t) => _inner.AcceptTermsAsync(id, t);
        public Task<bool> DeleteAccountAsync(string a) => _inner.DeleteAccountAsync(a);
        public Task<MetricsData> MetricsAsync(DateTimeOffset since) => _inner.MetricsAsync(since);
        public Task UpdateLoginAsync(int id, string? h) => _inner.UpdateLoginAsync(id, h);
        public Task<IReadOnlyList<CharacterSummaryRecord>> ListCharactersAsync(int id) => _inner.ListCharactersAsync(id);
        public Task<CharacterRecord> CreateCharacterAsync(int id, string n, CharacterClass c, CharacterRecord i) => _inner.CreateCharacterAsync(id, n, c, i);
        public Task<CharacterRecord?> LoadCharacterAsync(int id) => _inner.LoadCharacterAsync(id);
        public Task<IReadOnlyList<ItemLogEntry>> ItemLogAsync(int id, int max = 100) => _inner.ItemLogAsync(id, max);
        public Task<long> MaxItemUidAsync() => _inner.MaxItemUidAsync();
        public Task<IReadOnlyList<ReportRecord>> ReportsAsync(int max = 100) => _inner.ReportsAsync(max);
        public Task RecordDungeonRunAsync(DungeonRunRecord run) => _inner.RecordDungeonRunAsync(run);
        public Task<IReadOnlyList<DungeonRunRecord>> TopDungeonRunsAsync(string dungeonId, int max = 10) => _inner.TopDungeonRunsAsync(dungeonId, max);
        public Task<IReadOnlyList<MarketListingRecord>> MarketListingsAsync() => _inner.MarketListingsAsync();
        public Task<AccountRecord?> FindAccountByIdAsync(int id) => _inner.FindAccountByIdAsync(id);
        public Task<(int AccountId, string CharacterName)?> FindCharacterOwnerAsync(string n) => _inner.FindCharacterOwnerAsync(n);
        public Task SetBanAsync(int id, DateTimeOffset? until, string reason) => _inner.SetBanAsync(id, until, reason);
        public Task SetMuteAsync(int id, DateTimeOffset? until) => _inner.SetMuteAsync(id, until);
        public Task<bool> SetGmAsync(string a, bool gm) => _inner.SetGmAsync(a, gm);
        public Task<bool> SetPasswordHashAsync(string a, string hash) => _inner.SetPasswordHashAsync(a, hash);
        public Task RecordGmActionAsync(GmActionRecord action) => _inner.RecordGmActionAsync(action);
        public Task<IReadOnlyList<GmActionRecord>> GmActionsAsync(int max = 100) => _inner.GmActionsAsync(max);
    }

    [Fact]
    public async Task Salvamento_tenta_de_novo_se_o_banco_falhar()
    {
        var store = new FlakyStore();
        var character = await store.CreateCharacterAsync(1, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        await using var saver = new CharacterSaver(store);

        saver.Enqueue(new CharacterSnapshot(character.Id, 2, 0, 500, 0, 0, 100, 100, 100, Array.Empty<ItemRecord>()),
            new List<ItemLogEntry> { new(character.Id, DateTimeOffset.UtcNow, "alz_monstro", null, 0, 500, "teste") });
        await saver.FlushAndStopAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, saver.Failures);
        Assert.Equal(1, saver.Saved);
        Assert.Equal(500, (await store.LoadCharacterAsync(character.Id))!.Alz);
        Assert.Single(await store.ItemLogAsync(character.Id)); // registro gravado uma vez só
    }

    [Fact]
    public async Task Foto_que_falhou_a_rodada_inteira_vai_junto_com_a_proxima()
    {
        var store = new FlakyStore { FailuresLeft = CharacterSaver.AttemptsPerRound };
        var character = await store.CreateCharacterAsync(1, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        await using var saver = new CharacterSaver(store) { FirstRetryDelay = TimeSpan.FromMilliseconds(1), RetryUnsavedAfter = TimeSpan.FromMinutes(5) };

        saver.Enqueue(new CharacterSnapshot(character.Id, 2, 0, 500, 0, 0, 100, 100, 100, Array.Empty<ItemRecord>()),
            new List<ItemLogEntry> { new(character.Id, DateTimeOffset.UtcNow, "compra", null, 0, -500, "primeira") });
        while (store.FailuresLeft > 0) await Task.Delay(10);
        await Task.Delay(50);
        Assert.True(saver.IsPending(character.Id)); // não foi gravada, então ainda não pode entrar de novo

        saver.Enqueue(new CharacterSnapshot(character.Id, 2, 0, 800, 0, 0, 100, 100, 100, Array.Empty<ItemRecord>()),
            new List<ItemLogEntry> { new(character.Id, DateTimeOffset.UtcNow, "alz_monstro", null, 0, 300, "segunda") });
        Assert.True(await saver.WaitForCharacterAsync(character.Id, TimeSpan.FromSeconds(10)));

        Assert.Equal(800, (await store.LoadCharacterAsync(character.Id))!.Alz);
        Assert.Equal(2, (await store.ItemLogAsync(character.Id)).Count); // o registro da foto que falhou não se perdeu
    }

    [Fact]
    public async Task Foto_que_falhou_eh_tentada_de_novo_com_a_fila_vazia()
    {
        var store = new FlakyStore { FailuresLeft = CharacterSaver.AttemptsPerRound };
        var character = await store.CreateCharacterAsync(1, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        await using var saver = new CharacterSaver(store)
        {
            FirstRetryDelay = TimeSpan.FromMilliseconds(1), RetryUnsavedAfter = TimeSpan.FromMilliseconds(200),
        };

        saver.Enqueue(new CharacterSnapshot(character.Id, 2, 0, 500, 0, 0, 100, 100, 100, Array.Empty<ItemRecord>()), new List<ItemLogEntry>());

        Assert.True(await saver.WaitForCharacterAsync(character.Id, TimeSpan.FromSeconds(10)));
        Assert.Equal(500, (await store.LoadCharacterAsync(character.Id))!.Alz);
    }

    [Fact]
    public async Task Espera_o_salvamento_do_personagem_antes_de_carregar_de_novo()
    {
        var store = new FlakyStore(); // falha 2 vezes: o salvamento leva uns 750 ms
        var character = await store.CreateCharacterAsync(1, "Ana", CharacterClass.Blade, AuthService.InitialCharacter());
        await using var saver = new CharacterSaver(store);
        Assert.True(await saver.WaitForCharacterAsync(character.Id, TimeSpan.FromSeconds(1))); // nada na fila

        saver.Enqueue(new CharacterSnapshot(character.Id, 2, 0, 700, 0, 0, 100, 100, 100, Array.Empty<ItemRecord>()), new List<ItemLogEntry>());
        Assert.True(saver.IsPending(character.Id));
        Assert.False(saver.IsPending(character.Id + 1));
        // A versão muda a cada foto: quem leu o personagem do banco antes disso sabe que precisa ler de novo.
        Assert.Equal(1, saver.SaveVersion(character.Id));
        Assert.Equal(0, saver.SaveVersion(character.Id + 1));

        Assert.True(await saver.WaitForCharacterAsync(character.Id, TimeSpan.FromSeconds(10)));
        Assert.Equal(700, (await store.LoadCharacterAsync(character.Id))!.Alz); // já está no banco quando a espera acaba
    }
}
