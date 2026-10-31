using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Net;
using Game.Shared.Text;
using Game.Shared.World;

namespace Game.Server.Net;

/// <summary>Opções do servidor (linha de comando).</summary>
public sealed record ServerOptions
{
    public int Port { get; init; } = Protocol.DefaultPort;

    /// <summary>Atraso artificial em cada sentido, em ms (ida e volta = o dobro). Só para testes.</summary>
    public int SimulatedLatencyMs { get; init; }

    /// <summary>Variação aleatória extra em cada sentido, em ms (de 0 até este valor).</summary>
    public int SimulatedJitterMs { get; init; }

    /// <summary>De quantos em quantos segundos registrar as métricas (0 = nunca).</summary>
    public double StatsIntervalSeconds { get; init; }

    /// <summary>Aceita Hello sem login (convidados não são salvos). Para bots e testes.</summary>
    public bool AllowGuests { get; init; }

    /// <summary>Dungeon fácil (vida e dano baixos, sem nível mínimo) para os bots de nível 1 conseguirem terminar.</summary>
    public bool DungeonTest { get; init; }

    /// <summary>Nível mínimo das dungeons no lugar do catálogo (0 = o do catálogo). Dificuldade normal; para testes
    /// com amigos ainda sem nível.</summary>
    public int DungeonMinLevel { get; init; }

    /// <summary>Todo monstro comum do mundo aberto nasce elite (teste do visual).</summary>
    public bool AllElites { get; init; }

    /// <summary>Classe dos convidados (testes das outras classes com o exe).</summary>
    public CharacterClass GuestClass { get; init; } = CharacterClass.Blade;

    /// <summary>Segundos para mandar o Hello depois de conectar (senão a conexão cai).</summary>
    public double HelloTimeoutSeconds { get; init; } = 15;

    /// <summary>Segundos sem receber nada (o cliente manda ping a cada 1 s) antes de derrubar a conexão.</summary>
    public double IdleTimeoutSeconds { get; init; } = 30;

    /// <summary>Conexões ao mesmo tempo vindas do mesmo IP (o próprio computador, 127.0.0.1, não tem limite).</summary>
    public int MaxConnectionsPerIp { get; init; } = 8;

    /// <summary>Mensagens por segundo por conexão (anti flood) e a folga para rajadas curtas.</summary>
    public double MessagesPerSecond { get; init; } = 40;

    public double MessageBurst { get; init; } = 80;

    /// <summary>Jogadores por time na arena (5; menos para testar com poucos jogadores).</summary>
    public int ArenaTeamSize { get; init; } = Game.Shared.Pvp.ArenaRules.TeamSize;

    /// <summary>De quantos em quantos segundos salvar os personagens conectados.</summary>
    public double SaveIntervalSeconds { get; init; } = 10;
}

/// <summary>
/// Liga a rede ao mundo: aceita conexões TCP, junta as mensagens recebidas numa fila e roda o loop do jogo
/// numa thread só, a <see cref="GameWorld.TickRate"/> ticks por segundo. Opcionalmente atrasa as mensagens
/// (simulador de latência) e registra métricas de desempenho.
/// </summary>
public sealed class ServerHost : IOutbox, IDisposable
{
    private readonly ServerOptions _options;
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<(ClientConnection Connection, Action<double> Work)> _inbox = new();
    private readonly Dictionary<int, ClientConnection> _byPlayer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<string> _log;
    private readonly Random _jitter = new();
    private readonly AuthService? _auth;
    private readonly CharacterSaver? _saver;
    private int _nextConnectionId;
    private readonly ConcurrentDictionary<int, ClientConnection> _connections = new();
    private double _nextSweepAt;

    // Saúde (para o monitoramento): ticks do último segundo, erros desde que ligou e a última foto.
    private int _healthTicks;
    private double _healthTickMs;
    private double _healthTickMax;
    private double _lastTickAt;
    private long _errors;
    private volatile HealthSnapshot _health = new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Protocol.Version.ToString());

    /// <summary>Foto mais recente da saúde do servidor (segura de qualquer thread).</summary>
    public HealthSnapshot Health
    {
        get
        {
            var h = _health;
            // Loop travado: a foto envelhece e o monitoramento percebe.
            return h with { SecondsSinceTick = h.Running ? Now - _lastTickAt : h.SecondsSinceTick };
        }
    }

    /// <summary>Erros pegos no loop do jogo desde que o servidor ligou.</summary>
    public long Errors => Interlocked.Read(ref _errors);
    private Thread? _loopThread;

    // Simulador de latência: trabalho e envios esperando a hora certa (só usados pela thread do loop).
    private readonly List<(double At, Action<double> Work)> _delayedIn = new();
    private readonly List<(double At, ClientConnection Connection, byte[] Frame)> _delayedOut = new();
    private readonly Dictionary<int, double> _lastInAt = new();
    private readonly Dictionary<int, double> _lastOutAt = new();

    public GameWorld World { get; }
    public ServerMetrics Metrics { get; } = new();

    /// <summary>Porta em uso (útil quando criado com porta 0 nos testes).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Segundos desde que o servidor iniciou: o relógio do jogo.</summary>
    public double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Servidor de teste: só convidados, sem banco.</summary>
    public ServerHost(int port, Action<string>? log = null) : this(new ServerOptions { Port = port, AllowGuests = true }, log) { }

    /// <summary>
    /// <paramref name="auth"/> e <paramref name="saver"/> nulos = sem contas (só convidados, se permitido).
    /// </summary>
    public ServerHost(ServerOptions options, Action<string>? log = null, AuthService? auth = null, CharacterSaver? saver = null)
    {
        _options = options;
        _auth = auth;
        _saver = saver;
        _log = log ?? (_ => { });
        _listener = new TcpListener(IPAddress.Any, options.Port);
        World = new GameWorld(this, uids: auth?.Uids, dungeonTuning: options.DungeonTest ? DungeonTuning.Test
            : options.DungeonMinLevel > 0 ? DungeonTuning.Normal with { MinLevel = options.DungeonMinLevel } : null,
            arenaTeamSize: options.ArenaTeamSize, eliteChance: options.AllElites ? 1 : EliteRules.Chance)
        {
            GuestClass = options.GuestClass,
        };
        // Mercado: cada compra, venda e retirada salva quem fez na hora, na ordem da fila (veja GameWorld.Market).
        World.SaveRequested += player =>
        {
            if (_saver == null || player.CharacterId == 0) return;
            var (snapshot, logs) = World.CaptureForSave(player);
            _saver.Enqueue(snapshot, logs);
        };
        // Expulsão por GM (vale também para banimento): mostra o motivo e fecha a conexão.
        World.KickRequested += (playerId, reason) =>
        {
            if (!_byPlayer.TryGetValue(playerId, out var connection)) return;
            _log($"Conexão #{connection.ConnectionId} expulsa por um GM ({reason})");
            Refuse(connection, reason);
        };
        World.SuspicionRaised += (player, reasonKey) =>
            _log($"[suspeita] {player.Name} (conta {player.AccountId}): {Loc.T(reasonKey)}");
    }

    private bool SimulatingLatency => _options.SimulatedLatencyMs > 0 || _options.SimulatedJitterMs > 0;

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
        _loopThread = new Thread(GameLoop) { IsBackground = true, Name = "GameLoop" };
        _loopThread.Start();
        _log($"Servidor ouvindo na porta {Port} ({GameWorld.TickRate} ticks/s, protocolo v{Protocol.Version})");
        if (SimulatingLatency)
            _log($"ATENÇÃO: latência simulada de {_options.SimulatedLatencyMs} ms (+0 a {_options.SimulatedJitterMs} ms) em cada sentido");
    }

    public void Stop()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        _listener.Stop();
        _loopThread?.Join(TimeSpan.FromSeconds(2));

        // Com o loop parado, é seguro ler o mundo daqui: fecha as sessões (métricas) e salva todo mundo antes de fechar
        // as conexões.
        foreach (var player in World.Players) World.EndSession(player, Now);
        SaveAll();
        if (_saver != null)
        {
            _log($"Salvando {World.Players.Count(p => p.CharacterId != 0)} personagens antes de desligar...");
            _saver.FlushAndStopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        }
        foreach (var connection in _byPlayer.Values.ToList()) connection.Close();
    }

    /// <summary>Põe na fila de salvamento todos os personagens conectados (convidados não são salvos).</summary>
    private void SaveAll()
    {
        if (_saver == null) return;
        foreach (var player in World.Players)
        {
            if (player.CharacterId == 0) continue;
            var (snapshot, logs) = World.CaptureForSave(player);
            _saver.Enqueue(snapshot, logs);
        }
    }

    public void Dispose() => Stop();

    void IOutbox.Send(int playerId, INetMessage message)
    {
        if (!_byPlayer.TryGetValue(playerId, out var connection)) return;
        var frame = NetCodec.Encode(message);
        Metrics.CountSent(frame.Length);
        if (!SimulatingLatency)
        {
            connection.Send(frame);
            return;
        }
        var at = DelayedTime(_lastOutAt, connection.ConnectionId);
        _delayedOut.Add((at, connection, frame));
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            var connection = new ClientConnection(Interlocked.Increment(ref _nextConnectionId), client)
            {
                Limiter = new InboundLimiter(_options.MessagesPerSecond, _options.MessageBurst),
            };
            if (TooManyFrom(connection.Address))
            {
                _log($"Conexão de {connection.RemoteEndPoint} recusada: mais de {_options.MaxConnectionsPerIp} do mesmo IP");
                connection.Close();
                continue;
            }
            _connections[connection.ConnectionId] = connection;
            _log($"Conexão #{connection.ConnectionId} de {connection.RemoteEndPoint}");
            connection.Start(OnMessage, OnClosed);
        }
    }

    // Chamados na thread de rede: só enfileiram trabalho para o loop do jogo.
    private void OnMessage(ClientConnection connection, INetMessage message)
    {
        Metrics.CountReceived();
        _inbox.Enqueue((connection, now => Dispatch(connection, message, now)));
    }

    private void OnClosed(ClientConnection connection, string reason) =>
        _inbox.Enqueue((connection, closedAt =>
        {
            _log($"Conexão #{connection.ConnectionId} encerrada ({reason})");
            _connections.TryRemove(connection.ConnectionId, out var _);
            _lastInAt.Remove(connection.ConnectionId);
            _lastOutAt.Remove(connection.ConnectionId);
            if (connection.PlayerId == 0) return;
            var leaving = World.FindPlayer(connection.PlayerId);
            if (leaving != null && leaving.CharacterId != 0 && _saver != null)
            {
                World.EndSession(leaving, closedAt);
                var (snapshot, logs) = World.CaptureForSave(leaving);
                _saver.Enqueue(snapshot, logs);
            }
            _byPlayer.Remove(connection.PlayerId);
            World.RemovePlayer(connection.PlayerId);
        }));

    private void Dispatch(ClientConnection connection, INetMessage message, double now)
    {
        if (connection.IsClosed) return;

        if (connection.PlayerId == 0)
        {
            if (connection.LoginPending) return; // esperando o banco: ignora o resto até entrar
            if (message is not Hello hello)
            {
                Refuse(connection, new LocalText("net.hello_first"));
                return;
            }
            if (hello.ProtocolVersion != Protocol.Version)
            {
                Refuse(connection, new LocalText("net.old_version", hello.ProtocolVersion, Protocol.Version));
                return;
            }

            if (string.IsNullOrEmpty(hello.Token))
            {
                if (!_options.AllowGuests)
                {
                    Refuse(connection, new LocalText("net.login_required"));
                    return;
                }
                Enter(connection, hello.Name, now, character: null);
                return;
            }

            if (_auth == null)
            {
                Refuse(connection, new LocalText("net.no_accounts"));
                return;
            }

            // Carrega o personagem do banco sem travar o loop; a entrada acontece de volta na thread do loop.
            connection.LoginPending = true;
            _ = ResolveAndEnterAsync(connection, hello);
            return;
        }

        World.Handle(connection.PlayerId, message, now);
    }

    private async Task ResolveAndEnterAsync(ClientConnection connection, Hello hello, int attempt = 1)
    {
        AuthResult<CharacterRecord> result;
        var version = _saver?.SaveVersion(hello.CharacterId) ?? 0;
        try
        {
            // Saiu e entrou de novo logo em seguida (trocar de personagem e voltar): espera o salvamento da saída
            // terminar, senão o personagem seria lido do banco como estava antes dela. Se não terminar (banco fora do
            // ar), recusa: é melhor tentar de novo do que entrar com o estado antigo.
            if (_saver != null && !await _saver.WaitForCharacterAsync(hello.CharacterId, TimeSpan.FromSeconds(15)))
            {
                _log($"ATENÇÃO: personagem {hello.CharacterId} ainda tinha salvamento na fila depois de 15 s; entrada recusada.");
                result = AuthResult<CharacterRecord>.Fail(new LocalText("net.db_unavailable"), 503);
            }
            else result = await _auth!.ResolveCharacterAsync(hello.Token, hello.CharacterId);
        }
        catch (Exception ex)
        {
            _log($"Erro ao carregar personagem {hello.CharacterId}: {ex.GetBaseException().Message}");
            result = AuthResult<CharacterRecord>.Fail(new LocalText("net.db_unavailable"), 503);
        }

        _inbox.Enqueue((connection, now =>
        {
            if (connection.IsClosed)
            {
                connection.LoginPending = false;
                return;
            }
            // A conexão antiga do mesmo personagem fechou enquanto ele era lido do banco: a foto da saída entrou na
            // fila depois da leitura, então o que foi lido pode ser o estado de antes. Lê de novo depois de ela gravar.
            if (result.Ok && _saver != null && _saver.SaveVersion(hello.CharacterId) != version)
            {
                if (attempt < 3)
                {
                    _ = ResolveAndEnterAsync(connection, hello, attempt + 1);
                    return;
                }
                result = AuthResult<CharacterRecord>.Fail(new LocalText("auth.try_again"), 503);
            }
            connection.LoginPending = false;
            if (!result.Ok)
            {
                Refuse(connection, result.Error);
                return;
            }
            if (World.IsCharacterOnline(result.Value!.Id))
            {
                Refuse(connection, new LocalText("net.already_online"));
                return;
            }
            Enter(connection, result.Value.Name, now, result.Value);
        }));
    }

    private void Enter(ClientConnection connection, string name, double now, CharacterRecord? character)
    {
        // A conexão é registrada assim que o id existe, antes das boas-vindas saírem.
        var player = World.AddPlayer(name, now, id =>
        {
            connection.PlayerId = id;
            _byPlayer[id] = connection;
        }, character);
        var kind = character == null ? "convidado" : $"personagem {character.Id}, nível {character.Level}";
        _log($"Conexão #{connection.ConnectionId} entrou como {player.Name} (id {player.Id}, {kind})");
    }

    private void Refuse(ClientConnection connection, LocalText reason)
    {
        connection.Send(NetCodec.Encode(new Reject { Reason = reason }));
        _log($"Conexão #{connection.ConnectionId} recusada: {reason}");
        // Dá tempo para o Reject sair antes de fechar.
        _ = Task.Delay(200).ContinueWith(_ => connection.Close());
    }

    /// <summary>Mais conexões abertas do mesmo IP do que o permitido (127.0.0.1 e ::1 ficam de fora: testes e bots).</summary>
    private bool TooManyFrom(System.Net.IPAddress? address)
    {
        if (address == null || System.Net.IPAddress.IsLoopback(address) || _options.MaxConnectionsPerIp <= 0) return false;
        var count = 0;
        foreach (var other in _connections.Values)
            if (!other.IsClosed && address.Equals(other.Address)) count++;
        return count >= _options.MaxConnectionsPerIp;
    }

    /// <summary>
    /// Uma vez por segundo (no loop do jogo): derruba quem conectou e não mandou o Hello a tempo, e quem parou de
    /// mandar mensagens (cliente travado ou conexão morta que o TCP ainda não percebeu).
    /// </summary>
    private void SweepConnections()
    {
        foreach (var connection in _connections.Values)
        {
            if (connection.IsClosed) continue;
            if (connection.PlayerId == 0 && !connection.LoginPending && connection.AgeSeconds > _options.HelloTimeoutSeconds)
            {
                _connections.TryRemove(connection.ConnectionId, out _);
                Refuse(connection, new LocalText("net.hello_timeout"));
            }
            else if (connection.IdleSeconds > _options.IdleTimeoutSeconds)
            {
                _log($"Conexão #{connection.ConnectionId} sem mensagens há {connection.IdleSeconds:0} s: fechando");
                connection.Close();
            }
        }
    }

    /// <summary>
    /// Roda um trabalho do loop sem deixar um erro derrubar o servidor inteiro: registra, conta (aparece na saúde e no
    /// monitoramento) e segue. Um erro aqui é sempre defeito a corrigir; o log guarda o detalhe.
    /// </summary>
    private void Safely(Action work, string what)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            var count = Interlocked.Increment(ref _errors);
            if (count <= 50 || count % 100 == 0) _log($"[erro] {what}: {ex}");
        }
    }

    private void UpdateHealth(double now)
    {
        var monsters = World.Monsters.Count;
        _health = new HealthSnapshot(
            Running: true, Players: World.Players.Count, Monsters: monsters, Connections: _connections.Count,
            UptimeSeconds: now, TicksPerSecond: _healthTicks, TickAverageMs: _healthTicks == 0 ? 0 : _healthTickMs / _healthTicks,
            TickMaxMs: _healthTickMax, Errors: Errors, MemoryMb: GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            SecondsSinceTick: 0, Version: Protocol.Version.ToString());
        _healthTicks = 0;
        _healthTickMs = 0;
        _healthTickMax = 0;
    }

    private void GameLoop()
    {
        var tickSeconds = 1.0 / GameWorld.TickRate;
        var nextTick = Now;
        var nextStatsAt = Now + _options.StatsIntervalSeconds;
        var nextSaveAt = Now + _options.SaveIntervalSeconds;
        while (!_stop.IsCancellationRequested)
        {
            var now = Now;
            while (_inbox.TryDequeue(out var item))
            {
                if (!SimulatingLatency) Safely(() => item.Work(now), "mensagem");
                else _delayedIn.Add((DelayedTime(_lastInAt, item.Connection.ConnectionId), item.Work));
            }
            if (SimulatingLatency) RunDelayed(now);

            if (now >= nextTick)
            {
                var started = Stopwatch.GetTimestamp();
                Safely(() => World.Tick(now), "tick");
                var tickMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Metrics.CountTick(tickMs);
                _healthTicks++;
                _healthTickMs += tickMs;
                _healthTickMax = Math.Max(_healthTickMax, tickMs);
                _lastTickAt = now;
                nextTick += tickSeconds;
                if (now - nextTick > 1) nextTick = now; // atrasou demais: não tenta compensar todos os ticks
            }

            if (now >= _nextSweepAt)
            {
                _nextSweepAt = now + 1;
                Safely(SweepConnections, "varredura");
                UpdateHealth(now);
            }

            if (_options.SaveIntervalSeconds > 0 && now >= nextSaveAt)
            {
                nextSaveAt = now + _options.SaveIntervalSeconds;
                SaveAll();
            }

            if (_options.StatsIntervalSeconds > 0 && now >= nextStatsAt)
            {
                nextStatsAt = now + _options.StatsIntervalSeconds;
                _log(Metrics.Report(World.Players.Count, World.Monsters.Count));
            }

            var wait = nextTick - Now;
            if (wait > 0.001) Thread.Sleep(TimeSpan.FromSeconds(Math.Min(wait, SimulatingLatency ? 0.002 : 0.005)));
        }
    }

    /// <summary>Executa o que já chegou na hora (entrada) e envia o que já pode sair (saída).</summary>
    private void RunDelayed(double now)
    {
        for (var i = 0; i < _delayedIn.Count;)
        {
            if (_delayedIn[i].At > now)
            {
                i++;
                continue;
            }
            var work = _delayedIn[i].Work;
            _delayedIn.RemoveAt(i);
            work(now);
        }
        for (var i = 0; i < _delayedOut.Count;)
        {
            if (_delayedOut[i].At > now)
            {
                i++;
                continue;
            }
            var (_, connection, frame) = _delayedOut[i];
            _delayedOut.RemoveAt(i);
            connection.Send(frame);
        }
    }

    /// <summary>Horário em que a mensagem "chega", sem passar à frente de uma anterior da mesma conexão (como no TCP).</summary>
    private double DelayedTime(Dictionary<int, double> lastAt, int connectionId)
    {
        var delay = (_options.SimulatedLatencyMs + _jitter.NextDouble() * _options.SimulatedJitterMs) / 1000.0;
        var at = Now + delay;
        if (lastAt.TryGetValue(connectionId, out var previous) && at < previous) at = previous;
        lastAt[connectionId] = at;
        return at;
    }
}

/// <summary>
/// Como o servidor está agora (atualizado uma vez por segundo pelo loop do jogo): para o monitoramento
/// (<c>/api/health</c> e <c>/metrics</c>).
/// </summary>
public sealed record HealthSnapshot(
    bool Running, int Players, int Monsters, int Connections, double UptimeSeconds, double TicksPerSecond, double TickAverageMs,
    double TickMaxMs, long Errors, double MemoryMb, double SecondsSinceTick, string Version);

/// <summary>Contadores de desempenho do servidor, zerados a cada relatório.</summary>
public sealed class ServerMetrics
{
    private readonly Stopwatch _window = Stopwatch.StartNew();
    private TimeSpan _cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;
    private long _bytesSent;
    private long _messagesSent;
    private long _messagesReceived;
    private int _ticks;
    private double _tickTotalMs;
    private double _tickMaxMs;

    public void CountSent(int bytes)
    {
        Interlocked.Add(ref _bytesSent, bytes);
        Interlocked.Increment(ref _messagesSent);
    }

    public void CountReceived() => Interlocked.Increment(ref _messagesReceived);

    public void CountTick(double milliseconds)
    {
        _ticks++;
        _tickTotalMs += milliseconds;
        if (milliseconds > _tickMaxMs) _tickMaxMs = milliseconds;
    }

    /// <summary>Resumo desde o último relatório (e zera os contadores).</summary>
    public string Report(int players, int monsters)
    {
        var seconds = Math.Max(0.001, _window.Elapsed.TotalSeconds);
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var cpuSeconds = (cpu - _cpuAtStart).TotalSeconds;
        var coreUsage = cpuSeconds / seconds * 100;
        var machineUsage = coreUsage / Environment.ProcessorCount;
        var kbPerSecond = Interlocked.Exchange(ref _bytesSent, 0) / 1024.0 / seconds;
        var outRate = Interlocked.Exchange(ref _messagesSent, 0) / seconds;
        var inRate = Interlocked.Exchange(ref _messagesReceived, 0) / seconds;
        var avgTick = _ticks == 0 ? 0 : _tickTotalMs / _ticks;
        var ticksPerSecond = _ticks / seconds;

        var report =
            $"[métricas] {players} jogadores, {monsters} monstros | ticks {ticksPerSecond:0.0}/s, " +
            $"médio {avgTick:0.00} ms, máx {_tickMaxMs:0.00} ms | CPU {coreUsage:0.0}% de 1 núcleo " +
            $"({machineUsage:0.0}% da máquina) | saída {kbPerSecond:0.0} KB/s" +
            (players > 0 ? $" ({kbPerSecond / players:0.0} KB/s por jogador)" : "") +
            $" | mensagens {outRate:0}/s saindo, {inRate:0}/s entrando";

        _window.Restart();
        _cpuAtStart = cpu;
        _ticks = 0;
        _tickTotalMs = 0;
        _tickMaxMs = 0;
        return report;
    }
}
