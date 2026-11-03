using System.Globalization;
using System.Text.Json;
using Game.Server.Accounts;
using Game.Shared.Accounts;
using Game.Shared.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Game.Server.Net;

/// <summary>
/// API HTTP de contas, no mesmo processo do jogo:
/// <code>
/// POST /api/register      { Account, Password }        → 200 | 400 | 409
/// POST /api/login         { Account, Password, Remember } → { Token, ExpiresInSeconds, DeviceToken } | 401 | 429
/// POST /api/login/device  { DeviceToken }              → igual ao login (com um DeviceToken novo) | 401 | 403
/// POST /api/logout        { DeviceToken }              → 200 (esquece o token do computador e a sessão do Bearer)
/// GET  /api/characters    Authorization: Bearer token  → { Characters: [...] }
/// POST /api/characters    { Name, ClassId }            → { Id, Name, ClassId, Level } | 400 | 409
/// </code>
/// Em desenvolvimento roda em HTTP; em produção deve ficar atrás de HTTPS (proxy reverso ou certificado no Kestrel).
/// </summary>
public sealed class LoginApi : IAsyncDisposable
{
    // Campos públicos com os nomes exatos (o JsonUtility do Unity lê assim).
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNamingPolicy = null };

    private readonly WebApplication _app;

    public LoginApi(AuthService auth, int port, Action<string>? log = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 16 * 1024);
        _app = builder.Build();
        MapMonitoring();

        _app.MapPost("/api/register", async (HttpRequest request) =>
        {
            var body = await ReadAsync<Credentials>(request);
            if (body == null) return Error(new LocalText("auth.bad_request"), 400);
            var result = await auth.RegisterAsync(body, ClientIp(request));
            if (result.Ok) log?.Invoke($"Conta criada: {result.Value}");
            return result.Ok ? Ok(new ApiError()) : Error(result.Error, result.StatusCode);
        });

        _app.MapPost("/api/login", async (HttpRequest request) =>
        {
            var body = await ReadAsync<Credentials>(request);
            if (body == null) return Error(new LocalText("auth.bad_request"), 400);
            var result = await auth.LoginAsync(body, ClientIp(request));
            return result.Ok ? Ok(result.Value!) : Error(result.Error, result.StatusCode);
        });

        _app.MapPost("/api/login/device", async (HttpRequest request) =>
        {
            var body = await ReadAsync<DeviceLogin>(request);
            if (body == null) return Error(new LocalText("auth.bad_request"), 400);
            var result = await auth.LoginWithDeviceAsync(body, ClientIp(request));
            return result.Ok ? Ok(result.Value!) : Error(result.Error, result.StatusCode);
        });

        _app.MapPost("/api/logout", async (HttpRequest request) =>
        {
            var body = await ReadAsync<DeviceLogin>(request);
            if (body == null) return Error(new LocalText("auth.bad_request"), 400);
            await auth.LogoutAsync(body, BearerToken(request));
            return Ok(new ApiError());
        });

        _app.MapGet("/api/characters", async (HttpRequest request) =>
        {
            var result = await auth.ListCharactersAsync(BearerToken(request));
            return result.Ok ? Ok(result.Value!) : Error(result.Error, result.StatusCode);
        });

        _app.MapPost("/api/characters", async (HttpRequest request) =>
        {
            var body = await ReadAsync<CreateCharacterRequest>(request);
            if (body == null) return Error(new LocalText("auth.bad_request"), 400);
            var result = await auth.CreateCharacterAsync(BearerToken(request), body);
            if (result.Ok) log?.Invoke($"Personagem criado: {result.Value!.Name} (id {result.Value.Id})");
            return result.Ok ? Ok(result.Value!) : Error(result.Error, result.StatusCode);
        });
    }

    /// <summary>
    /// IP de quem fez o pedido. Atrás do Caddy (que fala com o jogo pela rede interna do Docker), o IP de verdade vem no
    /// X-Forwarded-For, que o Caddy preenche e não aceita de fora. De um endereço público, o cabeçalho é ignorado:
    /// assim ninguém escapa do limite por IP inventando o cabeçalho.
    /// </summary>
    internal static string? ClientIp(HttpRequest request)
    {
        var remote = request.HttpContext.Connection.RemoteIpAddress;
        if (remote == null) return null;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (IsInternal(remote))
        {
            var forwarded = request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                // Se houver vários, o último é o que o nosso proxy acrescentou.
                var last = forwarded.Split(',')[^1].Trim();
                if (System.Net.IPAddress.TryParse(last, out var client)) return client.ToString();
            }
        }
        return remote.ToString();
    }

    private static bool IsInternal(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return address.IsIPv6UniqueLocal || address.IsIPv6LinkLocal;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>
    /// Saúde do servidor do jogo, para o monitoramento. Fica nula até o servidor ligar (a API sobe antes); enquanto for
    /// nula, <c>/api/health</c> responde 503.
    /// </summary>
    public Func<HealthSnapshot>? Health { get; set; }

    /// <summary>Saudável: o loop do jogo rodou um tick há menos de 5 s.</summary>
    public static bool IsHealthy(HealthSnapshot h) => h.Running && h.SecondsSinceTick < 5;

    /// <summary>Texto dos termos ou da política (pasta Legal/ ao lado do programa, copiada do Shared).</summary>
    public static string? LegalText(string name)
    {
        if (name != Legal.TermsName && name != Legal.PrivacyName) return null;
        var path = Path.Combine(AppContext.BaseDirectory, "Legal", name + ".txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private void MapMonitoring()
    {
        // Termos e política, para o link no Discord e na página do jogo (https://dominio/api/legal/termos).
        _app.MapGet("/api/legal/{name}", (string name) =>
            LegalText(name) is { } text ? Results.Text(text, "text/plain; charset=utf-8") : Results.NotFound());

        // JSON simples para quem vigia o servidor (UptimeRobot, healthcheck do Docker): 200 se está tudo bem, 503 se não.
        _app.MapGet("/api/health", () =>
        {
            var health = Health?.Invoke();
            if (health == null) return Results.Json(new { status = "iniciando" }, statusCode: 503);
            return Results.Json(new
            {
                status = IsHealthy(health) ? "ok" : "travado",
                players = health.Players,
                monsters = health.Monsters,
                connections = health.Connections,
                uptimeSeconds = Math.Round(health.UptimeSeconds),
                ticksPerSecond = health.TicksPerSecond,
                tickAverageMs = Math.Round(health.TickAverageMs, 2),
                tickMaxMs = Math.Round(health.TickMaxMs, 2),
                errors = health.Errors,
                memoryMb = Math.Round(health.MemoryMb, 1),
                protocol = health.Version,
                build = Environment.GetEnvironmentVariable("GAME_VERSION") ?? "local",
            }, statusCode: IsHealthy(health) ? 200 : 503);
        });

        // Formato de texto do Prometheus (Grafana, etc.), se um dia quiser gráficos.
        _app.MapGet("/metrics", () =>
        {
            var h = Health?.Invoke();
            if (h == null) return Results.Text("", "text/plain", statusCode: 503);
            var text = string.Join("\n", new[]
            {
                $"game_up {(IsHealthy(h) ? 1 : 0)}",
                $"game_players {h.Players}",
                $"game_monsters {h.Monsters}",
                $"game_connections {h.Connections}",
                $"game_uptime_seconds {h.UptimeSeconds.ToString("0", CultureInfo.InvariantCulture)}",
                $"game_ticks_per_second {h.TicksPerSecond.ToString("0.0", CultureInfo.InvariantCulture)}",
                $"game_tick_average_ms {h.TickAverageMs.ToString("0.000", CultureInfo.InvariantCulture)}",
                $"game_tick_max_ms {h.TickMaxMs.ToString("0.000", CultureInfo.InvariantCulture)}",
                $"game_errors_total {h.Errors}",
                $"game_memory_mb {h.MemoryMb.ToString("0.0", CultureInfo.InvariantCulture)}",
            }) + "\n";
            return Results.Text(text, "text/plain; version=0.0.4");
        });
    }

    public Task StartAsync() => _app.StartAsync();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary>Endereço em uso (com porta 0 nos testes, a porta escolhida pelo sistema).</summary>
    public string Address => _app.Urls.FirstOrDefault()?.Replace("0.0.0.0", "127.0.0.1") ?? "";

    private static async Task<T?> ReadAsync<T>(HttpRequest request) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..].Trim() : null;
    }

    private static IResult Ok<T>(T value) => Results.Json(value, Json);

    private static IResult Error(LocalText message, int status) => Results.Json(ApiError.From(message), Json, statusCode: status);
}
