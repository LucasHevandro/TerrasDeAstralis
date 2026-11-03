using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Game.Server.Accounts;
using Game.Server.Net;
using Game.Server.Persistence;
using Game.Shared.Accounts;

namespace Game.Server.Tests;

/// <summary>"Manter conectado": token do computador guardado só como hash, trocado a cada uso e revogável.</summary>
public class DeviceTokenTests
{
    private static (AuthService Auth, InMemoryGameStore Store, Action<TimeSpan> Advance) Create(string? terms = null)
    {
        var store = new InMemoryGameStore();
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var auth = new AuthService(store, new PasswordHasher(1000), new SessionTokens(TimeSpan.FromHours(12), () => now), () => now,
            termsVersion: terms);
        return (auth, store, span => now += span);
    }

    private static Credentials Creds(bool remember = true, string password = "senha123", string terms = "") =>
        new() { Account = "lucas", Password = password, Remember = remember, AcceptTerms = terms };

    private static async Task<string> RememberAsync(AuthService auth)
    {
        var login = await auth.LoginAsync(Creds());
        Assert.True(login.Ok);
        return login.Value!.DeviceToken;
    }

    [Fact]
    public async Task Login_sem_manter_conectado_nao_devolve_token_do_computador()
    {
        var (auth, _, _) = Create();
        await auth.RegisterAsync(Creds());

        var login = await auth.LoginAsync(Creds(remember: false));

        Assert.True(login.Ok);
        Assert.Equal("", login.Value!.DeviceToken);
    }

    [Fact]
    public async Task Token_do_computador_entra_sem_senha_e_eh_trocado_a_cada_uso()
    {
        var (auth, store, _) = Create();
        await auth.RegisterAsync(Creds());
        var first = await RememberAsync(auth);
        Assert.Equal((int)TimeSpan.FromDays(30).TotalSeconds, (await auth.LoginAsync(Creds())).Value!.DeviceExpiresInSeconds);

        var entered = await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = first });

        Assert.True(entered.Ok);
        Assert.NotNull(auth.Tokens.Validate(entered.Value!.Token));
        var second = entered.Value.DeviceToken;
        Assert.NotEmpty(second);
        Assert.NotEqual(first, second);
        // O banco só guarda o hash, e o token usado não vale mais.
        Assert.Null(await store.FindDeviceTokenAsync(second, DateTimeOffset.UtcNow));
        Assert.NotNull(await store.FindDeviceTokenAsync(DeviceTokens.Hash(second), DateTimeOffset.UtcNow));
        Assert.Null(await store.FindDeviceTokenAsync(DeviceTokens.Hash(first), DateTimeOffset.UtcNow));
        var reused = await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = first });
        Assert.Equal(401, reused.StatusCode);
        Assert.Equal("auth.device_expired", reused.Error.Key);
        Assert.True((await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = second })).Ok);
    }

    [Fact]
    public async Task Token_vence_em_30_dias()
    {
        var (auth, _, advance) = Create();
        await auth.RegisterAsync(Creds());
        var token = await RememberAsync(auth);

        advance(TimeSpan.FromDays(30));

        Assert.Equal(401, (await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = token })).StatusCode);
    }

    [Fact]
    public async Task Token_invalido_vazio_ou_enorme_eh_recusado()
    {
        var (auth, _, _) = Create();
        await auth.RegisterAsync(Creds());

        Assert.Equal(401, (await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = "" })).StatusCode);
        Assert.Equal(401, (await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = "inventado" })).StatusCode);
        Assert.Equal(401, (await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = new string('a', 5000) })).StatusCode);
    }

    [Fact]
    public async Task No_maximo_cinco_computadores_por_conta()
    {
        var (auth, _, advance) = Create();
        await auth.RegisterAsync(Creds());
        var tokens = new List<string>();
        for (var i = 0; i < DeviceTokens.MaxPerAccount + 1; i++)
        {
            tokens.Add(await RememberAsync(auth));
            advance(TimeSpan.FromMinutes(1));
        }

        // O sexto derrubou o mais antigo; os outros continuam.
        Assert.False((await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = tokens[0] })).Ok);
        foreach (var token in tokens.Skip(1)) Assert.True((await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = token })).Ok);
    }

    [Fact]
    public async Task Trocar_a_senha_derruba_os_computadores_e_as_sessoes()
    {
        var (auth, _, _) = Create();
        await auth.RegisterAsync(Creds());
        var login = await auth.LoginAsync(Creds());

        Assert.Null(await auth.ResetPasswordAsync("lucas", "senhanova1"));

        Assert.Equal(401, (await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = login.Value!.DeviceToken })).StatusCode);
        Assert.Null(auth.Tokens.Validate(login.Value.Token));
    }

    [Fact]
    public async Task Banir_apaga_os_tokens_e_a_conta_banida_nao_entra()
    {
        var (auth, store, _) = Create();
        await auth.RegisterAsync(Creds());
        var token = await RememberAsync(auth);
        var account = (await store.FindAccountAsync("lucas"))!;

        await store.SetBanAsync(account.Id, DateTimeOffset.MaxValue, "teste");

        Assert.Equal(401, (await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = token })).StatusCode);
        // Tirar o banimento não traz o token de volta.
        await store.SetBanAsync(account.Id, null, "");
        Assert.Equal(401, (await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = token })).StatusCode);
    }

    [Fact]
    public async Task Sair_da_conta_esquece_o_token_e_a_sessao()
    {
        var (auth, _, _) = Create();
        await auth.RegisterAsync(Creds());
        var login = (await auth.LoginAsync(Creds())).Value!;

        await auth.LogoutAsync(new DeviceLogin { DeviceToken = login.DeviceToken }, login.Token);

        Assert.Equal(401, (await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = login.DeviceToken })).StatusCode);
        Assert.Null(auth.Tokens.Validate(login.Token));
    }

    [Fact]
    public async Task Termos_novos_pedem_aceite_sem_gastar_o_token()
    {
        var (auth, store, _) = Create(terms: "2026-10");
        await auth.RegisterAsync(Creds(terms: "2026-10"));
        var token = (await auth.LoginAsync(Creds(terms: "2026-10"))).Value!.DeviceToken;
        await store.AcceptTermsAsync((await store.FindAccountAsync("lucas"))!.Id, "2026-01");

        var asked = await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = token });
        Assert.Equal("auth.terms_updated", asked.Error.Key);

        Assert.True((await auth.LoginWithDeviceAsync(new DeviceLogin { DeviceToken = token, AcceptTerms = "2026-10" })).Ok);
    }

    [Fact]
    public async Task Apagar_a_conta_leva_os_tokens_junto()
    {
        var (auth, store, _) = Create();
        await auth.RegisterAsync(Creds());
        var token = await RememberAsync(auth);

        Assert.True(await store.DeleteAccountAsync("lucas"));

        Assert.Null(await store.FindDeviceTokenAsync(DeviceTokens.Hash(token), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Hash_eh_sha256_em_hexa()
    {
        var hash = DeviceTokens.Hash("abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        Assert.Equal(43, DeviceTokens.New().Length);
    }

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNamingPolicy = null };

    [Fact]
    public async Task Manter_conectado_e_sair_da_conta_por_HTTP()
    {
        var auth = new AuthService(new InMemoryGameStore(), new PasswordHasher(1000), new SessionTokens());
        await using var api = new LoginApi(auth, port: 0);
        await api.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(api.Address) };
        await http.PostAsJsonAsync("/api/register", new Credentials { Account = "lucas", Password = "senha123" }, Json);

        var login = await (await http.PostAsJsonAsync("/api/login", Creds(), Json)).Content.ReadFromJsonAsync<LoginResponse>(Json);
        Assert.NotEmpty(login!.DeviceToken);

        var device = await http.PostAsJsonAsync("/api/login/device", new DeviceLogin { DeviceToken = login.DeviceToken }, Json);
        Assert.Equal(HttpStatusCode.OK, device.StatusCode);
        var entered = (await device.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        Assert.NotEmpty(entered.Token);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", entered.Token);
        var logout = await http.PostAsJsonAsync("/api/logout", new DeviceLogin { DeviceToken = entered.DeviceToken }, Json);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/characters")).StatusCode);
        var again = await http.PostAsJsonAsync("/api/login/device", new DeviceLogin { DeviceToken = entered.DeviceToken }, Json);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Equal("auth.device_expired", (await again.Content.ReadFromJsonAsync<ApiError>(Json))!.Key);
    }
}
