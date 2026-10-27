using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Game.Server.Accounts;
using Game.Server.Net;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Net;

namespace Game.Server.Tests;

public class AuthServiceTests
{
    private static (AuthService Auth, InMemoryGameStore Store, Func<DateTimeOffset> SetClock, Action<TimeSpan> Advance) Create()
    {
        var store = new InMemoryGameStore();
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var auth = new AuthService(store, new PasswordHasher(1000), new SessionTokens(TimeSpan.FromHours(12), () => now), () => now);
        return (auth, store, () => now, span => now += span);
    }

    private static Credentials Creds(string account = "lucas", string password = "senha123") => new() { Account = account, Password = password };

    [Fact]
    public async Task Cadastro_e_login_devolvem_token()
    {
        var (auth, _, _, _) = Create();

        Assert.True((await auth.RegisterAsync(Creds())).Ok);
        var login = await auth.LoginAsync(Creds("LUCAS"));

        Assert.True(login.Ok);
        Assert.NotEmpty(login.Value!.Token);
        Assert.NotNull(auth.Tokens.Validate(login.Value.Token));
    }

    [Fact]
    public async Task Troca_de_senha_pelo_comando_vale_no_proximo_login()
    {
        var (auth, _, _, _) = Create();
        await auth.RegisterAsync(Creds());

        Assert.Equal("password.short", (await auth.ResetPasswordAsync("lucas", "curta"))?.Key);
        Assert.Equal("auth.no_account", (await auth.ResetPasswordAsync("ninguem", "senhanova1"))?.Key);
        Assert.Null(await auth.ResetPasswordAsync("LUCAS", "senhanova1"));

        Assert.False((await auth.LoginAsync(Creds())).Ok);
        Assert.True((await auth.LoginAsync(Creds(password: "senhanova1"))).Ok);
    }

    [Fact]
    public async Task Cadastro_repetido_ou_invalido_eh_recusado()
    {
        var (auth, _, _, _) = Create();
        await auth.RegisterAsync(Creds());

        Assert.Equal(409, (await auth.RegisterAsync(Creds("Lucas"))).StatusCode);
        Assert.Equal(400, (await auth.RegisterAsync(Creds("ab"))).StatusCode);
        Assert.Equal(400, (await auth.RegisterAsync(Creds("outra", "fraca"))).StatusCode);
    }

    [Fact]
    public async Task Senha_errada_e_conta_inexistente_dao_o_mesmo_erro()
    {
        var (auth, _, _, _) = Create();
        await auth.RegisterAsync(Creds());

        var wrong = await auth.LoginAsync(Creds(password: "errada123"));
        var missing = await auth.LoginAsync(Creds("ninguem"));

        Assert.Equal((401, wrong.Error), (missing.StatusCode, missing.Error));
    }

    [Fact]
    public async Task Cinco_erros_travam_a_conta_por_cinco_minutos()
    {
        var (auth, _, _, advance) = Create();
        await auth.RegisterAsync(Creds());

        for (var i = 0; i < AuthService.MaxFailedLogins; i++) await auth.LoginAsync(Creds(password: "errada123"));
        var locked = await auth.LoginAsync(Creds()); // até a senha certa é recusada durante o bloqueio

        Assert.Equal(429, locked.StatusCode);
        advance(AuthService.LockDuration + TimeSpan.FromSeconds(1));
        Assert.True((await auth.LoginAsync(Creds())).Ok);
    }

    [Fact]
    public async Task Criar_personagens_respeita_limite_nome_unico_e_classe()
    {
        var (auth, _, _, _) = Create();
        await auth.RegisterAsync(Creds());
        var token = (await auth.LoginAsync(Creds())).Value!.Token;

        Assert.True((await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Ação", ClassId = 0 })).Ok);
        Assert.Equal(409, (await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "acao" })).StatusCode);
        Assert.Equal(400, (await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Ana", ClassId = 9 })).StatusCode);
        Assert.Equal(400, (await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "1nvalido" })).StatusCode);
        Assert.True((await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Bia", ClassId = 1 })).Ok);
        Assert.True((await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Caio" })).Ok);
        Assert.Equal(409, (await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Duda" })).StatusCode);

        var list = (await auth.ListCharactersAsync(token)).Value!;
        Assert.Equal(new[] { "Ação", "Bia", "Caio" }, list.Characters.Select(c => c.Name));
        Assert.All(list.Characters, c => Assert.Equal(1, c.Level));
    }

    [Fact]
    public async Task Criar_personagem_guarda_cabelo_e_roupa_e_corrige_cor_fora_da_paleta()
    {
        var (auth, _, _, _) = Create();
        await auth.RegisterAsync(Creds());
        var token = (await auth.LoginAsync(Creds())).Value!.Token;

        var ana = (await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Ana", Hair = 3, Cloth = 5 })).Value!;
        await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Bia", Hair = 99, Cloth = -2 });

        Assert.Equal((3, 5), (ana.Hair, ana.Cloth));
        var list = (await auth.ListCharactersAsync(token)).Value!;
        Assert.Equal(new[] { (3, 5), (0, 0) }, list.Characters.Select(c => (c.Hair, c.Cloth)));
        var loaded = (await auth.ResolveCharacterAsync(token, ana.Id)).Value!;
        Assert.Equal((3, 5), (loaded.Hair, loaded.Cloth));
    }

    [Fact]
    public async Task Trava_de_senha_errada_vale_so_para_o_ip_que_errou()
    {
        var (auth, _, _, _) = Create();
        await auth.RegisterAsync(Creds());

        for (var i = 0; i < AuthService.MaxFailedLogins; i++) await auth.LoginAsync(Creds(password: "errada123"), "200.9.9.9");

        Assert.Equal(429, (await auth.LoginAsync(Creds(), "200.9.9.9")).StatusCode);
        Assert.True((await auth.LoginAsync(Creds(), "200.1.1.1")).Ok); // o dono, de outro IP, continua entrando
    }

    [Fact]
    public async Task Mesmo_ip_faz_no_maximo_dez_pedidos_de_login_ou_cadastro_por_minuto()
    {
        var (auth, _, _, advance) = Create();
        await auth.RegisterAsync(Creds());

        for (var i = 0; i < AuthService.MaxAuthRequestsPerMinute; i++)
            Assert.NotEqual(429, (await auth.LoginAsync(Creds($"conta{i}"), "200.5.5.5")).StatusCode);

        var blocked = await auth.LoginAsync(Creds(), "200.5.5.5");
        Assert.Equal(429, blocked.StatusCode);
        Assert.Equal("auth.try_again", blocked.Error.Key);
        Assert.Equal(429, (await auth.RegisterAsync(Creds("nova1"), "200.5.5.5")).StatusCode);
        Assert.True((await auth.LoginAsync(Creds(), "200.5.5.6")).Ok);

        advance(TimeSpan.FromMinutes(1));
        Assert.True((await auth.LoginAsync(Creds(), "200.5.5.5")).Ok);
    }

    [Fact]
    public async Task Nome_de_conta_gigante_no_login_eh_recusado_sem_travar_nada()
    {
        var (auth, _, _, _) = Create();

        var result = await auth.LoginAsync(Creds(new string('a', 5000)));

        Assert.Equal(401, result.StatusCode);
        Assert.Equal("auth.wrong_credentials", result.Error.Key);
    }

    [Fact]
    public async Task Mesmo_ip_cria_no_maximo_tres_contas_por_dia()
    {
        var (auth, _, _, advance) = Create();
        for (var i = 0; i < AuthService.MaxAccountsPerIp; i++)
            Assert.True((await auth.RegisterAsync(Creds($"conta{i}"), "200.1.2.3")).Ok);

        var blocked = await auth.RegisterAsync(Creds("conta9"), "200.1.2.3");
        Assert.Equal(429, blocked.StatusCode);
        Assert.Equal("auth.too_many_accounts", blocked.Error.Key);
        // Outro IP e quem não informa IP (ferramentas locais) continuam podendo.
        Assert.True((await auth.RegisterAsync(Creds("conta9"), "200.1.2.4")).Ok);
        Assert.True((await auth.RegisterAsync(Creds("conta8"))).Ok);

        advance(AuthService.AccountWindow);
        Assert.True((await auth.RegisterAsync(Creds("conta7"), "200.1.2.3")).Ok);
    }

    [Fact]
    public async Task Lista_de_personagens_leva_o_grau_da_couraca_vestida()
    {
        var (auth, store, _, _) = Create();
        await auth.RegisterAsync(Creds());
        var token = (await auth.LoginAsync(Creds())).Value!.Token;
        var ana = (await auth.CreateCharacterAsync(token, new CreateCharacterRequest { Name = "Ana" })).Value!;
        Assert.Equal(0, (await auth.ListCharactersAsync(token)).Value!.Characters.Single().ArmorGrade);

        var armor = new ItemRecord(77, ItemContainerKind.Equipment, (int)EquipSlot.Armor, "armadura_astralita", 1);
        await store.SaveCharacterAsync(new CharacterSnapshot(ana.Id, 20, 0, 0, 0, 0, 100, 100, 100, new[] { armor }), Array.Empty<ItemLogEntry>());

        Assert.Equal(3, (await auth.ListCharactersAsync(token)).Value!.Characters.Single().ArmorGrade);
    }

    [Fact]
    public async Task Nao_entra_com_personagem_de_outra_conta_nem_sem_sessao()
    {
        var (auth, _, _, _) = Create();
        await auth.RegisterAsync(Creds("conta_a"));
        await auth.RegisterAsync(Creds("conta_b"));
        var tokenA = (await auth.LoginAsync(Creds("conta_a"))).Value!.Token;
        var tokenB = (await auth.LoginAsync(Creds("conta_b"))).Value!.Token;
        var characterA = (await auth.CreateCharacterAsync(tokenA, new CreateCharacterRequest { Name = "Ana" })).Value!;

        Assert.True((await auth.ResolveCharacterAsync(tokenA, characterA.Id)).Ok);
        Assert.Equal(404, (await auth.ResolveCharacterAsync(tokenB, characterA.Id)).StatusCode);
        Assert.Equal(401, (await auth.ResolveCharacterAsync("token-falso", characterA.Id)).StatusCode);
        Assert.Equal(401, (await auth.ListCharactersAsync(null)).StatusCode);
    }

    [Fact]
    public async Task Senha_nunca_fica_guardada_em_texto()
    {
        var (auth, store, _, _) = Create();
        await auth.RegisterAsync(Creds());

        var account = await store.FindAccountAsync("lucas");

        Assert.DoesNotContain("senha123", account!.PasswordHash);
    }
}

/// <summary>A API HTTP de verdade, em porta livre.</summary>
public class LoginApiTests
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNamingPolicy = null };

    [Fact]
    public async Task Fluxo_completo_por_HTTP()
    {
        var auth = new AuthService(new InMemoryGameStore(), new PasswordHasher(1000), new SessionTokens());
        await using var api = new LoginApi(auth, port: 0);
        await api.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(api.Address) };

        var register = await http.PostAsJsonAsync("/api/register", new Credentials { Account = "lucas", Password = "senha123" }, Json);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var badLogin = await http.PostAsJsonAsync("/api/login", new Credentials { Account = "lucas", Password = "errada123" }, Json);
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);
        Assert.Contains("incorreta", (await badLogin.Content.ReadFromJsonAsync<ApiError>(Json))!.Error);

        var login = await http.PostAsJsonAsync("/api/login", new Credentials { Account = "lucas", Password = "senha123" }, Json);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(Json))!.Token;
        Assert.NotEmpty(token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/characters")).StatusCode);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var created = await http.PostAsJsonAsync("/api/characters", new CreateCharacterRequest { Name = "Guerreira", ClassId = 0 }, Json);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var list = await http.GetFromJsonAsync<CharacterList>("/api/characters", Json);
        Assert.Equal("Guerreira", Assert.Single(list!.Characters).Name);
    }

    [Fact]
    public async Task Corpo_invalido_eh_recusado_sem_quebrar()
    {
        var auth = new AuthService(new InMemoryGameStore(), new PasswordHasher(1000), new SessionTokens());
        await using var api = new LoginApi(auth, port: 0);
        await api.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(api.Address) };

        var response = await http.PostAsync("/api/login", new StringContent("isto não é json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
