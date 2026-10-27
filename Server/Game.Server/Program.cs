using System.Globalization;
using Game.Server.Accounts;
using Game.Server.Net;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

// Servidor do jogo e API de login.
// Uso: Game.Server [--port 7777] [--api-port 7778] [--db postgres|memory] [--guests] [--stats 10]
//                  [--latency 75] [--jitter 20] [--save-interval 10]
//   --db postgres  (padrão) banco PostgreSQL; conexão na variável GAME_DB ou a do docker-compose
//   --db memory    banco em memória (tudo se perde ao desligar) — desenvolvimento sem Docker
//   --guests       aceita entrar sem conta (bots e testes; convidados não são salvos)
//   --dungeon-test dungeon fácil e sem nível mínimo (bots de nível 1 conseguem terminar)
//   --dungeon-min-level N  nível mínimo das dungeons (1 = qualquer nível; 0 = o do catálogo), dificuldade normal
//   --elites       todo monstro comum do mundo aberto nasce elite (para testar o visual)
//   --guest-class c convidados entram com a classe c (blade, arcanist ou ranger; para testes)
//   --arena-size N jogadores por time na arena (padrão 5; 1 para testar a dois)
//   --stats N      métricas de desempenho a cada N segundos
//   --gm a,b       contas tratadas como GM nesta execução (testes locais; em produção use --set-gm)
//   --set-gm conta / --unset-gm conta  marca ou desmarca a conta como GM no banco e sai
//   --set-password conta  pede a senha nova no terminal (sem mostrar), grava e sai
//   --max-per-ip N conexões ao mesmo tempo do mesmo IP (padrão 8; 0 = sem limite; o próprio PC não conta)
//   --delete-account conta  apaga a conta e tudo dela (pedido do jogador, LGPD) e sai
//   --report [dias] resumo das métricas do teste fechado (padrão: desde o início) e sai
//   --check-health pergunta ao servidor já ligado (/api/health) se está tudo bem; sai com 0 ou 1 (healthcheck do Docker)
//   --latency N    (testes) atrasa cada mensagem N ms em cada sentido; --jitter N soma até N ms aleatórios
var options = new ServerOptions();
var apiPort = -1;
var database = "postgres";
var gmAccounts = new List<string>();
string? setGm = null;
var checkHealth = false;
string? deleteAccount = null;
string? setPassword = null;
double? reportDays = null;
var setGmValue = true;
for (var i = 0; i < args.Length; i++)
{
    var value = i + 1 < args.Length ? args[i + 1] : "";
    switch (args[i])
    {
        case "--port" when int.TryParse(value, out var port):
            options = options with { Port = port };
            break;
        case "--api-port" when int.TryParse(value, out var apiValue):
            apiPort = apiValue;
            break;
        case "--db":
            database = value;
            break;
        case "--guests":
            options = options with { AllowGuests = true };
            break;
        case "--arena-size" when int.TryParse(value, out var arenaSize):
            options = options with { ArenaTeamSize = Math.Max(1, arenaSize) };
            break;
        case "--dungeon-test":
            options = options with { DungeonTest = true };
            break;
        case "--dungeon-min-level" when int.TryParse(value, out var dungeonMin):
            options = options with { DungeonMinLevel = Math.Max(0, dungeonMin) };
            break;
        case "--elites":
            options = options with { AllElites = true };
            break;
        case "--guest-class" when value.Length > 0:
            options = options with { GuestClass = ClassInfo.FromTypeId(value.Trim().ToLowerInvariant()) };
            break;
        case "--gm" when value.Length > 0:
            gmAccounts.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            break;
        case "--set-gm" when value.Length > 0:
            setGm = value;
            break;
        case "--unset-gm" when value.Length > 0:
            setGm = value;
            setGmValue = false;
            break;
        case "--check-health":
            checkHealth = true;
            break;
        case "--delete-account" when value.Length > 0:
            deleteAccount = value;
            break;
        case "--set-password" when value.Length > 0:
            setPassword = value;
            break;
        case "--report":
            reportDays = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var days) ? days : 0;
            break;
        case "--max-per-ip" when int.TryParse(value, out var perIp):
            options = options with { MaxConnectionsPerIp = perIp };
            break;
        case "--stats" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var stats):
            options = options with { StatsIntervalSeconds = stats };
            break;
        case "--save-interval" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var save):
            options = options with { SaveIntervalSeconds = save };
            break;
        case "--latency" when int.TryParse(value, out var latency):
            options = options with { SimulatedLatencyMs = Math.Max(0, latency) };
            break;
        case "--jitter" when int.TryParse(value, out var jitter):
            options = options with { SimulatedJitterMs = Math.Max(0, jitter) };
            break;
    }
}
if (apiPort < 0) apiPort = options.Port + 1;

if (checkHealth)
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var response = await http.GetAsync($"http://127.0.0.1:{apiPort}/api/health");
        Console.WriteLine(await response.Content.ReadAsStringAsync());
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.GetBaseException().Message);
        return 1;
    }
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

IGameStore store;
if (database == "memory")
{
    store = new InMemoryGameStore();
    Log("ATENÇÃO: banco em memória — contas e personagens somem ao desligar.");
}
else
{
    var connection = Environment.GetEnvironmentVariable("GAME_DB") ?? DatabaseDefaults.DevelopmentConnection;
    var factory = new PooledDbContextFactory<GameDbContext>(new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(connection).Options);
    var ef = new EfGameStore(factory);
    try
    {
        await ef.MigrateAsync();
    }
    catch (Exception ex)
    {
        Log($"ERRO: não foi possível conectar ao PostgreSQL ({ex.GetBaseException().Message}).");
        Log("Abra o Docker Desktop e rode 'docker compose up -d' na raiz do projeto, ou use '--db memory'.");
        return 1;
    }
    store = ef;
    Log("Banco PostgreSQL pronto (migrações aplicadas).");
}

if (setGm != null)
{
    var changed = await store.SetGmAsync(Game.Shared.Accounts.AccountRules.NormalizeAccount(setGm), setGmValue);
    Log(changed ? $"Conta {setGm} {(setGmValue ? "agora é GM" : "não é mais GM")}." : $"Conta {setGm} não encontrada.");
    return changed ? 0 : 1;
}

if (deleteAccount != null)
{
    var deleted = await store.DeleteAccountAsync(Game.Shared.Accounts.AccountRules.NormalizeAccount(deleteAccount));
    Log(deleted ? $"Conta {deleteAccount} apagada, com personagens, itens, registros e métricas." : $"Conta {deleteAccount} não encontrada.");
    return deleted ? 0 : 1;
}

if (setPassword != null)
{
    var first = ReadSecret($"Senha nova para {setPassword}: ");
    var second = ReadSecret("Repita a senha: ");
    if (first != second)
    {
        Log("As senhas não são iguais. Nada mudou.");
        return 1;
    }
    var reset = new AuthService(store, new PasswordHasher(), new SessionTokens());
    var refused = await reset.ResetPasswordAsync(setPassword, first);
    Log(refused == null ? $"Senha da conta {setPassword} trocada." : $"Senha não trocada: {refused}");
    return refused == null ? 0 : 1;
}

if (reportDays != null)
{
    var now = DateTimeOffset.UtcNow;
    var since = reportDays > 0 ? now.AddDays(-reportDays.Value) : DateTimeOffset.MinValue;
    Console.Write(MetricsReport.Build(await store.MetricsAsync(since), since, now));
    return 0;
}

var auth = new AuthService(store, new PasswordHasher(), new SessionTokens(), uids: new ItemUidAllocator(await store.MaxItemUidAsync()),
    gmAccounts: gmAccounts, termsVersion: Game.Shared.Accounts.Legal.Version);
if (gmAccounts.Count > 0) Log($"ATENÇÃO: GM nesta execução: {string.Join(", ", gmAccounts)} (--gm).");
await using var saver = new CharacterSaver(store, Log);
await using var api = new LoginApi(auth, apiPort, Log);
if (!await TryStartAsync(() => api.StartAsync(), apiPort, "da API de login")) return 1;
Log($"API de login em http://localhost:{apiPort}/api (em produção, use HTTPS na frente)");

using var host = new ServerHost(options, Log, auth, saver);
api.Health = () => host.Health;

// Ranking da dungeon: carrega os melhores tempos e grava cada vitória (fora do loop do jogo).
foreach (var dungeonId in Game.Shared.Dungeon.DungeonCatalog.Ids)
    host.World.SeedRanking(await store.TopDungeonRunsAsync(dungeonId));
// Mercado: as ofertas à venda e as vendidas ainda não recebidas voltam para o quadro.
host.World.SeedMarket(await store.MarketListingsAsync());
host.World.RunCompleted += run => _ = store.RecordDungeonRunAsync(run).ContinueWith(
    t => Log($"Falha ao gravar o ranking: {t.Exception?.GetBaseException().Message}"), TaskContinuationOptions.OnlyOnFaulted);
if (options.DungeonTest) Log("ATENÇÃO: dungeon de teste (fácil e sem nível mínimo).");
else if (options.DungeonMinLevel > 0) Log($"ATENÇÃO: dungeons liberadas a partir do nível {options.DungeonMinLevel} (--dungeon-min-level).");
if (options.AllElites) Log("ATENÇÃO: todo monstro do mundo aberto nasce elite (--elites).");
if (options.GuestClass != CharacterClass.Blade) Log($"ATENÇÃO: convidados entram como {ClassInfo.TypeId(options.GuestClass)} (--guest-class).");

// Ferramentas de GM: auditoria e moderação gravadas no banco, fora do loop do jogo.
host.World.GmActionDone += action =>
{
    Log($"[gm] {action.GmName}: {action.Action} {action.Target} {action.Details}".TrimEnd());
    _ = store.RecordGmActionAsync(action).ContinueWith(
        t => Log($"Falha ao gravar ação de GM: {t.Exception?.GetBaseException().Message}"), TaskContinuationOptions.OnlyOnFaulted);
};
host.World.ModerationRequested += request => _ = ApplyModerationAsync(request);

async Task ApplyModerationAsync(Game.Server.World.ModerationRequest request)
{
    try
    {
        var accountId = request.AccountId;
        if (accountId == 0) accountId = (await store.FindCharacterOwnerAsync(request.CharacterName))?.AccountId ?? 0;
        if (accountId == 0)
        {
            Log($"[gm] personagem {request.CharacterName} não encontrado no banco ({request.Kind})");
            return;
        }
        switch (request.Kind)
        {
            case Game.Server.World.ModerationKind.Ban: await store.SetBanAsync(accountId, request.Until, request.Reason); break;
            case Game.Server.World.ModerationKind.Unban: await store.SetBanAsync(accountId, null, ""); break;
            case Game.Server.World.ModerationKind.Mute: await store.SetMuteAsync(accountId, request.Until); break;
            case Game.Server.World.ModerationKind.Unmute: await store.SetMuteAsync(accountId, null); break;
        }
    }
    catch (Exception ex)
    {
        Log($"Falha na moderação de {request.CharacterName}: {ex.GetBaseException().Message}");
    }
}
using var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};
// "docker stop" (e o systemd) mandam SIGTERM: desliga do mesmo jeito, salvando antes.
using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    stop.Set();
});

if (!await TryStartAsync(() => { host.Start(); return Task.CompletedTask; }, options.Port, "do jogo")) return 1;
if (options.AllowGuests) Log("ATENÇÃO: aceitando convidados sem conta (--guests).");
Log("Ctrl+C para desligar (os personagens são salvos antes).");
stop.Wait();
Log("Desligando...");
host.Stop();
Log($"Salvamentos concluídos: {saver.Saved}, falhas: {saver.Failures}.");
return 0;

// Lê uma senha do terminal sem mostrar o que é digitado (com a entrada redirecionada, lê a linha normalmente).
static string ReadSecret(string prompt)
{
    Console.Write(prompt);
    // O PowerShell põe a marca de UTF-8 (BOM) no começo do que manda por pipe.
    if (Console.IsInputRedirected) return (Console.ReadLine() ?? "").TrimStart('﻿').TrimEnd('\r');
    var text = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (text.Length > 0) text.Length--;
        }
        else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
    }
    Console.WriteLine();
    return text.ToString();
}

// Porta ocupada (outro servidor aberto, ou outro programa): explica em vez de fechar com o erro técnico.
async Task<bool> TryStartAsync(Func<Task> start, int port, string what)
{
    try
    {
        await start();
        return true;
    }
    catch (Exception ex) when (ex.GetBaseException() is System.Net.Sockets.SocketException
                               { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse })
    {
        Log($"A porta {port} {what} já está em uso. Feche o outro servidor (ou o programa que usa a porta) ou escolha " +
            $"outra com --port (a API usa a porta seguinte).");
        return false;
    }
}
