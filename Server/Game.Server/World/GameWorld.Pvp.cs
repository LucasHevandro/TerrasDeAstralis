using Game.Shared.Combat;
using Game.Shared.Net;
using Game.Shared.Pvp;
using Game.Shared.Text;
using ComboPhase = Game.Shared.Combat.ComboState;
using Game.Shared.World;

namespace Game.Server.World;

/// <summary>Duelo combinado entre dois jogadores.</summary>
public sealed class DuelSession
{
    public int Id { get; init; }
    public int PlayerA { get; init; }
    public int PlayerB { get; init; }
    public float CenterX { get; init; }
    public float CenterZ { get; init; }
    public double StartsAt { get; init; }
    public double EndsAt { get; init; }

    public bool Involves(int id) => id == PlayerA || id == PlayerB;
    public int OpponentOf(int id) => id == PlayerA ? PlayerB : PlayerA;
    public bool Started(double now) => now >= StartsAt;
}

/// <summary>Placar de um jogador numa partida da arena.</summary>
public sealed class ArenaScore
{
    public string Name = "";
    public ArenaTeam Team;
    public int Level;
    public int Kills;
    public int Deaths;
    public int Captures;
}

/// <summary>Uma partida da arena 5v5, numa faixa própria do mapa (a origem).</summary>
public sealed class ArenaMatch
{
    public int Id { get; init; }
    public int Slot { get; init; }
    public float OriginX { get; init; }
    public float OriginZ { get; init; }
    public double StartsAt { get; init; }
    public double EndsAt { get; init; }

    /// <summary>Quem ainda está na partida e de que time.</summary>
    public Dictionary<int, ArenaTeam> Teams { get; } = new();

    /// <summary>Placar de todos que jogaram (quem saiu continua no placar).</summary>
    public Dictionary<int, ArenaScore> Scores { get; } = new();

    public double[] Progress { get; } = new double[ArenaRules.Points.Count];
    public ArenaTeam[] Owner { get; } = Enumerable.Repeat(ArenaTeam.None, ArenaRules.Points.Count).ToArray();
    public bool[] Contested { get; } = new bool[ArenaRules.Points.Count];
    public int ScoreBlue { get; set; }
    public int ScoreRed { get; set; }
    public double ScoreCarry { get; set; }
    public bool Finished { get; set; }
    public ArenaTeam Winner { get; set; } = ArenaTeam.None;
    public double FinishedAt { get; set; }
    public double NextBroadcastAt { get; set; }

    public bool Started(double now) => now >= StartsAt;
}

/// <summary>PvP: dano entre jogadores, duelo e arena de captura de pontos.</summary>
public sealed partial class GameWorld
{
    private readonly Dictionary<int, DuelSession> _duels = new();
    private readonly Dictionary<int, ArenaMatch> _arenas = new();
    private readonly List<int> _arenaQueue = new();
    private int _nextDuelId = 1;
    private int _nextArenaId = 1;

    public DuelSession? FindDuel(int id) => _duels.TryGetValue(id, out var d) ? d : null;
    public ArenaMatch? FindArena(int id) => _arenas.TryGetValue(id, out var a) ? a : null;
    public IReadOnlyCollection<ArenaMatch> Arenas => _arenas.Values;
    public IReadOnlyList<int> ArenaQueue => _arenaQueue;

    // ===== Quem pode bater em quem =====

    /// <summary>O outro jogador, se pode ser atacado por este agora (duelo começado ou time inimigo na arena).</summary>
    private ServerPlayer? HostilePlayer(ServerPlayer player, int targetId)
    {
        if (targetId == player.Id || !_players.TryGetValue(targetId, out var other) || other.IsDead || player.IsDead) return null;
        var now = double.IsNaN(_lastTickAt) ? 0 : _lastTickAt;
        if (player.DuelId != 0 && player.DuelId == other.DuelId && FindDuel(player.DuelId) is { } duel && duel.Started(now)) return other;
        if (player.ArenaId != 0 && player.ArenaId == other.ArenaId && FindArena(player.ArenaId) is { Finished: false } match &&
            match.Started(now) && match.Teams.TryGetValue(player.Id, out var mine) && match.Teams.TryGetValue(other.Id, out var theirs) &&
            mine != theirs)
            return other;
        return null;
    }

    private IEnumerable<ServerPlayer> PvpTargets(ServerPlayer player, SkillData skill, ServerPlayer main)
    {
        if (skill.AreaRadius <= 0)
        {
            yield return main;
            yield break;
        }
        foreach (var other in _players.Values.ToList())
        {
            if (HostilePlayer(player, other.Id) != null && InRange(other.X, other.Z, main.X, main.Z, skill.AreaRadius)) yield return other;
        }
    }

    /// <summary>Golpe de jogador em jogador: dano reduzido do PvP; no duelo ninguém morre (para em 1 de vida).</summary>
    private void DamagePlayerToPlayer(ServerPlayer attacker, ServerPlayer victim, DamageResult hit, byte source, byte grade,
        string routeId, double now)
    {
        if (victim.IsDodging(now))
        {
            SendNearby(victim.X, victim.Z, new CombatEvent { AttackerId = attacker.Id, TargetId = victim.Id, Source = source, Dodged = true });
            return;
        }
        var amount = PvpRules.Scale(hit.Amount);
        var duel = FindDuel(victim.DuelId);
        if (duel != null) amount = Math.Min(amount, Math.Max(0, victim.Hp.Current - 1));
        var dealt = -victim.Hp.Add(-amount);
        victim.StatsDirty = true;
        attacker.LastCombatAt = victim.LastCombatAt = now;
        SendNearby(victim.X, victim.Z, new CombatEvent
        {
            AttackerId = attacker.Id, TargetId = victim.Id, Source = source, Damage = dealt, Critical = hit.Critical, Grade = grade,
            RouteId = routeId,
        });

        if (duel != null)
        {
            if (victim.Hp.Current <= 1) EndDuel(duel, attacker, LT("duel.won", attacker.Name, victim.Name));
            return;
        }
        if (victim.Hp.Current == 0 && FindArena(victim.ArenaId) is { } match) KillInArena(match, attacker, victim, now);
    }

    // ===== Duelo =====

    private void HandleDuel(ServerPlayer player, DuelCommand command, double now)
    {
        switch (command.Action)
        {
            case DuelActionKind.Request:
                RequestDuel(player, command.TargetName, now);
                break;
            case DuelActionKind.Accept:
                AcceptDuel(player, command.TargetId, now);
                break;
            case DuelActionKind.Decline:
                if (player.PendingDuelInvite is { } invite && _players.TryGetValue(invite.FromId, out var from))
                    SystemChat(from, LT("duel.declined", player.Name));
                player.PendingDuelInvite = null;
                break;
            case DuelActionKind.Forfeit:
                if (FindDuel(player.DuelId) is { } duel && _players.TryGetValue(duel.OpponentOf(player.Id), out var winner))
                    EndDuel(duel, winner, LT("duel.forfeit", player.Name, winner.Name));
                break;
        }
    }

    private LocalText? PvpBusy(ServerPlayer player) =>
        player.IsDead ? LT("pvp.busy_dead", player.Name) :
        player.DuelId != 0 ? LT("pvp.busy_duel", player.Name) :
        player.ArenaId != 0 ? LT("pvp.busy_arena", player.Name) :
        player.DungeonId != 0 ? LT("pvp.busy_dungeon", player.Name) :
        null;

    private void RequestDuel(ServerPlayer player, string name, double now)
    {
        var target = FindPlayerByName(name);
        LocalText? error =
            target == null ? LT("player.offline", name) :
            target == player ? LT("duel.self") :
            PvpBusy(player) ?? PvpBusy(target) ??
            (target.HasBlocked(player) ? LT("duel.not_accepting", target.Name) :
            Distance(player.X, player.Z, target.X, target.Z) > DuelRules.Range ? LT("duel.come_closer", target.Name) :
            target.PendingDuelInvite is { } pending && pending.ExpiresAt > now && pending.FromId != player.Id
                ? LT("duel.pending", target.Name) : null);
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        target!.PendingDuelInvite = (player.Id, now + DuelRules.InviteSeconds);
        _outbox.Send(target.Id, new DuelInvite { FromId = player.Id, FromName = player.Name, Seconds = (float)DuelRules.InviteSeconds });
        SystemChat(player, LT("duel.sent", target.Name));
    }

    private void AcceptDuel(ServerPlayer player, int fromId, double now)
    {
        var pending = player.PendingDuelInvite;
        player.PendingDuelInvite = null;
        if (pending is not { } invite || invite.FromId != fromId || invite.ExpiresAt < now || !_players.TryGetValue(fromId, out var from))
        {
            SystemChat(player, LT("duel.expired"));
            return;
        }
        var error = PvpBusy(player) ?? PvpBusy(from) ??
                    (Distance(player.X, player.Z, from.X, from.Z) > DuelRules.Range ? LT("duel.come_closer_accept", from.Name) : null);
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        RemoveFromQueue(player);
        RemoveFromQueue(from);
        var duel = new DuelSession
        {
            Id = _nextDuelId++, PlayerA = from.Id, PlayerB = player.Id, CenterX = (from.X + player.X) / 2, CenterZ = (from.Z + player.Z) / 2,
            StartsAt = now + DuelRules.CountdownSeconds, EndsAt = now + DuelRules.CountdownSeconds + DuelRules.MaxSeconds,
        };
        _duels.Add(duel.Id, duel);
        foreach (var p in new[] { from, player })
        {
            p.DuelId = duel.Id;
            p.PvpDirty = true;
            Notify(p, LT("duel.starting", (p == from ? player.Name : from.Name), DuelRules.CountdownSeconds), announcement: true);
        }
    }

    private void EndDuel(DuelSession duel, ServerPlayer? winner, LocalText message)
    {
        _duels.Remove(duel.Id);
        foreach (var id in new[] { duel.PlayerA, duel.PlayerB })
        {
            if (!_players.TryGetValue(id, out var p)) continue;
            p.DuelId = 0;
            p.PvpDirty = true;
            if (p.Combo.State == ComboPhase.Active)
            {
                p.Combo.Cancel();
                p.ComboDirty = true;
            }
            Notify(p, message, announcement: true);
        }
        // Quem está por perto também vê o resultado.
        if (winner != null)
            foreach (var other in _players.Values.Where(o => !duel.Involves(o.Id) && InRange(o.X, o.Z, duel.CenterX, duel.CenterZ, 30)))
                SystemChat(other, message);
    }

    private void TickDuels(double now)
    {
        foreach (var duel in _duels.Values.ToList())
        {
            var hasA = _players.TryGetValue(duel.PlayerA, out var a);
            var hasB = _players.TryGetValue(duel.PlayerB, out var b);
            if (!hasA || !hasB)
            {
                EndDuel(duel, hasA ? a : b, LT("duel.ended_left"));
                continue;
            }
            if (a!.IsDead || b!.IsDead)
            {
                EndDuel(duel, null, LT("duel.ended_monster"));
                continue;
            }
            if (!duel.Started(now)) continue;
            var outA = !InRange(a.X, a.Z, duel.CenterX, duel.CenterZ, DuelRules.BoundaryRadius);
            var outB = !InRange(b.X, b.Z, duel.CenterX, duel.CenterZ, DuelRules.BoundaryRadius);
            if (outA || outB)
            {
                var (loser, winner) = outA ? (a, b) : (b, a);
                EndDuel(duel, winner, LT("duel.fled", loser.Name, winner.Name));
            }
            else if (now >= duel.EndsAt)
            {
                EndDuel(duel, null, LT("duel.draw"));
            }
        }
    }

    // ===== Arena =====

    private int ArenaTeamSize => _arenaTeamSize;

    private void HandleArena(ServerPlayer player, ArenaCommand command, double now)
    {
        switch (command.Action)
        {
            case ArenaActionKind.Join:
                var error = PvpBusy(player);
                if (error != null)
                {
                    SystemChat(player, error);
                    return;
                }
                if (_arenaQueue.Contains(player.Id))
                {
                    SystemChat(player, LT("arena.already_queued"));
                    return;
                }
                _arenaQueue.Add(player.Id);
                player.InArenaQueue = true;
                MarkQueue();
                SystemChat(player, LT("arena.queued", _arenaQueue.Count, ArenaTeamSize * 2));
                break;
            case ArenaActionKind.LeaveQueue:
                if (RemoveFromQueue(player)) SystemChat(player, LT("arena.left_queue"));
                break;
            case ArenaActionKind.Leave:
                if (FindArena(player.ArenaId) is { } match) LeaveArena(match, player, LT("arena.left_match"));
                break;
        }
    }

    private bool RemoveFromQueue(ServerPlayer player)
    {
        if (!_arenaQueue.Remove(player.Id)) return false;
        player.InArenaQueue = false;
        player.PvpDirty = true;
        MarkQueue();
        return true;
    }

    private void MarkQueue()
    {
        foreach (var id in _arenaQueue)
            if (_players.TryGetValue(id, out var p)) p.PvpDirty = true;
    }

    private void TickArenas(double now, float dt)
    {
        // Fila: quem saiu do jogo ou ficou ocupado sai dela; com gente suficiente, forma uma partida.
        var removed = _arenaQueue.RemoveAll(id => !_players.TryGetValue(id, out var p) || p.DungeonId != 0 || p.DuelId != 0);
        if (removed > 0) MarkQueue();
        while (_arenaQueue.Count >= ArenaTeamSize * 2) StartArena(now);

        foreach (var match in _arenas.Values.ToList())
        {
            foreach (var id in match.Teams.Keys.Where(id => !_players.TryGetValue(id, out var p) || p.ArenaId != match.Id).ToList())
                match.Teams.Remove(id);
            if (match.Finished)
            {
                if (now >= match.FinishedAt + ArenaRules.ResultSeconds) CloseArena(match);
                continue;
            }
            if (match.Teams.Count == 0)
            {
                CloseArena(match);
                continue;
            }
            if (match.Started(now)) TickCapture(match, now, dt);

            var blueLeft = match.Teams.Values.Count(t => t == ArenaTeam.Blue);
            var redLeft = match.Teams.Values.Count(t => t == ArenaTeam.Red);
            if (match.ScoreBlue >= ArenaRules.WinScore || match.ScoreRed >= ArenaRules.WinScore || now >= match.EndsAt ||
                blueLeft == 0 || redLeft == 0)
            {
                FinishArena(match, blueLeft == 0 ? ArenaTeam.Red : redLeft == 0 ? ArenaTeam.Blue : (ArenaTeam?)null, now);
                continue;
            }
            if (now >= match.NextBroadcastAt)
            {
                match.NextBroadcastAt = now + 0.5;
                foreach (var id in match.Teams.Keys) _players[id].PvpDirty = true;
            }
        }
    }

    private void StartArena(double now)
    {
        var picked = _arenaQueue.Take(ArenaTeamSize * 2).ToList();
        _arenaQueue.RemoveRange(0, picked.Count);
        var players = picked.Select(id => _players[id]).ToList();
        var (blue, red) = ArenaRules.Balance(players.Select(p => (p.Id, p.Progress.Level)));
        var slot = 0;
        while (_arenas.Values.Any(a => a.Slot == slot)) slot++;
        var match = new ArenaMatch
        {
            Id = _nextArenaId++, Slot = slot, OriginX = 0, OriginZ = ArenaRules.OriginZ - slot * ArenaRules.Spacing,
            StartsAt = now + ArenaRules.CountdownSeconds, EndsAt = now + ArenaRules.CountdownSeconds + ArenaRules.MatchSeconds,
        };
        _arenas.Add(match.Id, match);
        foreach (var (team, ids) in new[] { (ArenaTeam.Blue, blue), (ArenaTeam.Red, red) })
        {
            for (var i = 0; i < ids.Count; i++)
            {
                var player = _players[ids[i]];
                player.InArenaQueue = false;
                player.ArenaId = match.Id;
                player.PvpDirty = true;
                match.Teams[player.Id] = team;
                match.Scores[player.Id] = new ArenaScore { Name = player.Name, Team = team, Level = player.Progress.Level };
                if (player.TradeId != 0 && FindTrade(player.TradeId) is { } trade) CancelTrade(trade, LT("trade.cancelled_arena"));
                player.Hp.Add(player.Hp.Max);
                player.Mp.Add(player.Mp.Max);
                player.StatsDirty = true;
                var (bx, bz) = ArenaBase(match, team, i);
                player.Movement.Teleport(bx, bz, now);
                // O estado (com a origem) vai antes da posição: o cliente monta o campo antes de ser levado.
                _outbox.Send(player.Id, PvpMessage(player, now));
                _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
                Notify(player, LT("arena.your_team", Loc.Ref(ArenaRules.TeamKey(team)), ArenaRules.CountdownSeconds), announcement: true);
            }
        }
        MarkQueue();
    }

    private (float X, float Z) ArenaBase(ArenaMatch match, ArenaTeam team, int index)
    {
        var (x, z) = ArenaRules.BaseOf(team);
        return (match.OriginX + x + (index - 2) * 2f, match.OriginZ + z);
    }

    private void TickCapture(ArenaMatch match, double now, float dt)
    {
        for (var i = 0; i < ArenaRules.Points.Count; i++)
        {
            var point = ArenaRules.Points[i];
            var px = match.OriginX + point.X;
            var pz = match.OriginZ + point.Z;
            var present = match.Teams
                .Where(kv => _players.TryGetValue(kv.Key, out var p) && !p.IsDead && InRange(p.X, p.Z, px, pz, ArenaRules.CaptureRadius))
                .ToList();
            var blue = present.Count(kv => kv.Value == ArenaTeam.Blue);
            var red = present.Count(kv => kv.Value == ArenaTeam.Red);
            match.Contested[i] = blue > 0 && red > 0;
            var before = match.Owner[i];
            var progress = match.Progress[i];
            var owner = ArenaRules.Advance(ref progress, before, blue, red, dt);
            match.Progress[i] = progress;
            if (owner == before) continue;
            match.Owner[i] = owner;
            foreach (var id in match.Teams.Keys) _players[id].PvpDirty = true;
            if (owner == ArenaTeam.None)
            {
                foreach (var id in match.Teams.Keys) SystemChat(_players[id], LT("arena.point_neutral", point.Name));
                continue;
            }
            foreach (var (id, _) in present.Where(kv => kv.Value == owner)) match.Scores[id].Captures++;
            foreach (var id in match.Teams.Keys)
                Notify(_players[id], LT("arena.point_taken", Loc.Ref(ArenaRules.TeamKey(owner)), point.Name), announcement: true);
        }

        match.ScoreCarry += dt;
        while (match.ScoreCarry >= 1)
        {
            match.ScoreCarry -= 1;
            foreach (var owner in match.Owner)
            {
                if (owner == ArenaTeam.Blue) match.ScoreBlue += ArenaRules.ScorePerPointSecond;
                else if (owner == ArenaTeam.Red) match.ScoreRed += ArenaRules.ScorePerPointSecond;
            }
        }
    }

    private void KillInArena(ArenaMatch match, ServerPlayer killer, ServerPlayer victim, double now)
    {
        victim.IsDead = true;
        victim.RespawnAt = now + ArenaRules.RespawnSeconds;
        if (victim.Combo.State == ComboPhase.Active)
        {
            victim.Combo.Cancel();
            victim.ComboDirty = true;
        }
        if (match.Scores.TryGetValue(killer.Id, out var k)) k.Kills++;
        if (match.Scores.TryGetValue(victim.Id, out var v)) v.Deaths++;
        if (match.Teams.TryGetValue(killer.Id, out var team))
        {
            if (team == ArenaTeam.Blue) match.ScoreBlue += ArenaRules.ScorePerKill;
            else match.ScoreRed += ArenaRules.ScorePerKill;
        }
        Notify(victim, LT("arena.downed_you", killer.Name), announcement: true);
        Notify(killer, LT("arena.you_downed", victim.Name, ArenaRules.ScorePerKill));
        foreach (var id in match.Teams.Keys) _players[id].PvpDirty = true;
    }

    private void FinishArena(ArenaMatch match, ArenaTeam? forcedWinner, double now)
    {
        match.Finished = true;
        match.FinishedAt = now;
        match.Winner = forcedWinner ?? (match.ScoreBlue > match.ScoreRed ? ArenaTeam.Blue
            : match.ScoreRed > match.ScoreBlue ? ArenaTeam.Red : ArenaTeam.None);
        foreach (var (id, team) in match.Teams)
        {
            var player = _players[id];
            var won = team == match.Winner;
            var alz = won ? ArenaRules.WinAlz : match.Winner == ArenaTeam.None ? (ArenaRules.WinAlz + ArenaRules.LossAlz) / 2 : ArenaRules.LossAlz;
            player.Alz += alz;
            player.StatsDirty = true;
            player.PvpDirty = true;
            Log(player, "arena", null, 0, alz, $"partida {match.Id}: {(won ? "vitória" : match.Winner == ArenaTeam.None ? "empate" : "derrota")} " +
                                              $"{match.ScoreBlue} x {match.ScoreRed}");
            var result = match.Winner == ArenaTeam.None ? LT("arena.draw") : won ? LT("arena.victory") : LT("arena.defeat");
            Notify(player, LT("arena.result", result, match.ScoreBlue, match.ScoreRed, alz), announcement: true);
        }
    }

    private void LeaveArena(ArenaMatch match, ServerPlayer player, LocalText message)
    {
        match.Teams.Remove(player.Id);
        SendHome(player, message);
    }

    private void CloseArena(ArenaMatch match)
    {
        foreach (var id in match.Teams.Keys.ToList())
            if (_players.TryGetValue(id, out var p)) SendHome(p, LT("arena.ended"));
        _arenas.Remove(match.Id);
    }

    /// <summary>Tira o jogador da arena e o leva para a vila, vivo.</summary>
    private void SendHome(ServerPlayer player, LocalText message)
    {
        player.ArenaId = 0;
        player.PvpDirty = true;
        if (player.IsDead)
        {
            player.IsDead = false;
            player.Hp.Add(player.Hp.Max);
        }
        var (x, z) = SpawnPointFor(player.Id);
        player.Movement.Teleport(x, z, double.IsNaN(_lastTickAt) ? 0 : _lastTickAt);
        player.StatsDirty = true;
        _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
        Notify(player, message);
    }

    private (float X, float Z)? ArenaRespawnPoint(ServerPlayer player)
    {
        var match = FindArena(player.ArenaId);
        if (match == null || !match.Teams.TryGetValue(player.Id, out var team)) return null;
        return ArenaBase(match, team, 2);
    }

    /// <summary>Não deixa sair do campo da arena. Retorna verdadeiro se corrigiu a posição.</summary>
    private bool ClampToArena(ServerPlayer player, double now)
    {
        var match = FindArena(player.ArenaId);
        if (match == null) return false;
        var x = Math.Clamp(player.X, match.OriginX - ArenaRules.HalfWidth + 0.5f, match.OriginX + ArenaRules.HalfWidth - 0.5f);
        var z = Math.Clamp(player.Z, match.OriginZ - ArenaRules.HalfLength + 0.5f, match.OriginZ + ArenaRules.HalfLength - 0.5f);
        if (x == player.X && z == player.Z) return false;
        player.Movement.Teleport(x, z, now);
        return true;
    }

    private void SendPvpState(ServerPlayer player, double now)
    {
        if (!player.PvpDirty) return;
        player.PvpDirty = false;
        _outbox.Send(player.Id, PvpMessage(player, now));
    }

    private PvpState PvpMessage(ServerPlayer player, double now)
    {
        var state = new PvpState
        {
            InQueue = player.InArenaQueue, QueueSize = (byte)Math.Min(255, _arenaQueue.Count), QueueNeeded = (byte)(ArenaTeamSize * 2),
        };
        if (FindDuel(player.DuelId) is { } duel)
        {
            state.Mode = PvpMode.Duel;
            state.StartsAt = duel.StartsAt;
            state.EndsAt = duel.EndsAt;
            state.OpponentId = duel.OpponentOf(player.Id);
            state.OpponentName = _players.TryGetValue(state.OpponentId, out var opponent) ? opponent.Name : "";
            state.OriginX = duel.CenterX;
            state.OriginZ = duel.CenterZ;
        }
        else if (FindArena(player.ArenaId) is { } match)
        {
            state.Mode = PvpMode.Arena;
            state.StartsAt = match.StartsAt;
            state.EndsAt = match.EndsAt;
            state.OriginX = match.OriginX;
            state.OriginZ = match.OriginZ;
            state.Team = (sbyte)(match.Teams.TryGetValue(player.Id, out var team) ? team : ArenaTeam.None);
            state.ScoreBlue = (ushort)Math.Min(ushort.MaxValue, match.ScoreBlue);
            state.ScoreRed = (ushort)Math.Min(ushort.MaxValue, match.ScoreRed);
            state.Finished = match.Finished;
            state.Winner = (sbyte)match.Winner;
            for (var i = 0; i < ArenaRules.Points.Count; i++)
                state.Points.Add(new ArenaPointInfo { Owner = (sbyte)match.Owner[i], Progress = (float)match.Progress[i], Contested = match.Contested[i] });
            foreach (var (id, score) in match.Scores)
                state.Players.Add(new ArenaPlayerInfo
                {
                    Id = id, Name = score.Name, Team = (sbyte)score.Team, Level = score.Level, Kills = (ushort)score.Kills,
                    Deaths = (ushort)score.Deaths, Captures = (ushort)score.Captures,
                });
        }
        return state;
    }
}
