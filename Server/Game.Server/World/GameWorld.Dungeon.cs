using Game.Server.Persistence;
using Game.Shared.Dungeon;
using Game.Shared.Items;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;
using Game.Shared.World;

namespace Game.Server.World;

/// <summary>Ajustes da dungeon para testes (bots de nível 1): vida e dano dos monstros e nível mínimo.</summary>
public sealed record DungeonTuning(float HpScale = 1f, float DamageScale = 1f, int? MinLevel = null)
{
    /// <summary>Vida dos monstros a 75%: uma dupla termina dentro do tempo (antes só grupos de 3 ou 4).</summary>
    public static readonly DungeonTuning Normal = new(HpScale: 0.75f);

    /// <summary>Para bots convidados de nível 1 conseguirem terminar (servidor com --dungeon-test).</summary>
    public static readonly DungeonTuning Test = new(0.012f, 0.08f, 1);
}

/// <summary>Uma cópia da dungeon para um grupo. Fica numa faixa própria do mapa (a origem), longe das outras.</summary>
public sealed class DungeonInstance
{
    public int Id { get; init; }
    public int Slot { get; init; }
    public required DungeonDefinition Definition { get; init; }
    public float OriginX { get; init; }
    public float OriginZ { get; init; }
    public double StartedAt { get; init; }
    public HashSet<int> PlayerIds { get; } = new();

    /// <summary>Nomes de quem entrou (para o ranking).</summary>
    public List<string> Members { get; } = new();

    public bool AllAccounts { get; init; }

    /// <summary>Monstros de cada sala (ids).</summary>
    public List<List<int>> RoomMonsters { get; } = new();

    public int Room { get; set; }
    public int BossId { get; set; }
    public List<int> Servants { get; } = new();
    public bool Shield { get; set; }
    public int ShieldsUsed { get; set; }
    public double NextSlamAt { get; set; }
    public (float X, float Z, double LandsAt)? PendingSlam { get; set; }
    public int MaxCombo { get; set; }
    public bool Completed { get; set; }
    public bool Failed { get; set; }
    public double CompletedAt { get; set; }
    public double Elapsed { get; set; }
    public DungeonMedal Medal { get; set; }
    public int LastMonstersLeft { get; set; } = -1;
    public bool Dirty { get; set; } = true;

    public float EntranceX => OriginX;
    public float EntranceZ => OriginZ + Definition.EntranceZ;
    public bool InBossRoom => Room == Definition.Rooms.Count - 1;
}

/// <summary>Dungeon em instância: entrada com chave, salas com portões, chefe com escudo, baú, medalha e ranking.</summary>
public sealed partial class GameWorld
{
    private const int MaxRankingEntries = 10;

    private readonly Dictionary<int, DungeonInstance> _dungeons = new();
    private readonly Dictionary<string, List<DungeonRunRecord>> _rankings = new();
    private int _nextDungeonId = 1;

    /// <summary>Uma dungeon foi concluída por um grupo só de personagens de conta (o servidor grava no banco).</summary>
    public event Action<DungeonRunRecord>? RunCompleted;

    public IReadOnlyCollection<DungeonInstance> Dungeons => _dungeons.Values;
    public DungeonInstance? FindDungeon(int id) => _dungeons.TryGetValue(id, out var d) ? d : null;

    public IReadOnlyList<DungeonRunRecord> Ranking(string dungeonId) =>
        _rankings.TryGetValue(dungeonId, out var list) ? list : Array.Empty<DungeonRunRecord>();

    /// <summary>Carrega o ranking salvo (ao ligar o servidor).</summary>
    public void SeedRanking(IEnumerable<DungeonRunRecord> runs)
    {
        foreach (var run in runs) AddToRanking(run);
    }

    private void AddToRanking(DungeonRunRecord run)
    {
        if (!_rankings.TryGetValue(run.DungeonId, out var list)) _rankings[run.DungeonId] = list = new List<DungeonRunRecord>();
        list.Add(run);
        list.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
        if (list.Count > MaxRankingEntries) list.RemoveRange(MaxRankingEntries, list.Count - MaxRankingEntries);
    }

    private static NpcDefinition Portal => NpcCatalog.Find(NpcCatalog.DungeonPortal)!;

    private int MinLevelFor(DungeonDefinition definition) => _dungeonTuning.MinLevel ?? definition.MinLevel;

    // ===== Entrar e sair =====

    private void HandleDungeon(ServerPlayer player, DungeonCommand command, double now)
    {
        if (command.Action == DungeonActionKind.Leave)
        {
            if (player.DungeonId != 0) ExitDungeon(player, LT("dungeon.left"));
            return;
        }
        var definition = DungeonCatalog.Find(command.DungeonId);
        if (definition == null) return;
        var error = CanEnter(player, definition, out var members);
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        Enter(definition, members, now);
    }

    private LocalText? CanEnter(ServerPlayer player, DungeonDefinition definition, out List<ServerPlayer> members)
    {
        members = PartyMembers(player).ToList();
        var party = FindParty(player.PartyId);
        if (player.DungeonId != 0) return LT("dungeon.already_in");
        if (!NearNpc(player, NpcRole.DungeonPortal)) return LT("dungeon.talk_to_portal", Loc.Ref("npc." + Portal.Id));
        if (party != null && party.LeaderId != player.Id) return LT("dungeon.leader_only");
        if (members.Count > DungeonRules.MaxPlayers) return LT("dungeon.max_players", DungeonRules.MaxPlayers);
        var minLevel = MinLevelFor(definition);
        foreach (var member in members)
        {
            if (member.DungeonId != 0) return LT("dungeon.member_in_dungeon", member.Name);
            if (member.DuelId != 0 || member.ArenaId != 0) return LT("dungeon.member_pvp", member.Name);
            if (member.TradeId != 0) return LT("dungeon.member_trade", member.Name);
            if (member.IsDead) return LT("dungeon.member_dead", member.Name);
            if (member.Progress.Level < minLevel) return LT("dungeon.member_level", member.Name, minLevel);
            if (Distance(member.X, member.Z, Portal.X, Portal.Z) > DungeonRules.PortalRange)
                return LT("dungeon.member_far", member.Name);
            if (member.Bag.Count(definition.KeyItemId) < 1)
                return LT("dungeon.member_key", member.Name, Loc.Ref("item." + definition.KeyItemId));
        }
        return null;
    }

    private void Enter(DungeonDefinition definition, List<ServerPlayer> members, double now)
    {
        var slot = 0;
        while (_dungeons.Values.Any(d => d.Slot == slot)) slot++;
        var instance = new DungeonInstance
        {
            Id = _nextDungeonId++, Slot = slot, Definition = definition, OriginX = 0,
            OriginZ = DungeonRules.InstanceOriginZ + slot * DungeonRules.InstanceSpacing, StartedAt = now,
            AllAccounts = members.All(m => !m.IsGuest), NextSlamAt = now,
        };
        _dungeons.Add(instance.Id, instance);

        var seed = 1000 + instance.Id;
        foreach (var room in definition.Rooms)
        {
            var ids = new List<int>();
            foreach (var spawn in room.Spawns)
            {
                Tune(spawn.Data);
                foreach (var (x, z) in spawn.SpawnPoints(seed++))
                {
                    var monster = CreateMonster(spawn, instance.OriginX + x, instance.OriginZ + z, instance.Id);
                    ids.Add(monster.Id);
                    if (spawn.Data.Id == definition.BossId) instance.BossId = monster.Id;
                }
            }
            instance.RoomMonsters.Add(ids);
        }

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            member.Bag.TryRemove(definition.KeyItemId, 1);
            member.InventoryDirty = true;
            Log(member, "chave_dungeon", definition.KeyItemId, 1, 0, $"entrou em {definition.Id} (instância {instance.Id})");
            RemoveFromQueue(member);
            instance.PlayerIds.Add(member.Id);
            instance.Members.Add(member.Name);
            member.DungeonId = instance.Id;
            member.Movement.Teleport(instance.EntranceX + (i - 1.5f) * 1.5f, instance.EntranceZ, now);
            // O estado vai antes da posição: o cliente monta o chão da dungeon antes de ser levado para lá.
            _outbox.Send(member.Id, StateMessage(instance));
            _outbox.Send(member.Id, new PositionCorrection { X = member.X, Z = member.Z });
            Notify(member, LT("dungeon.started", Loc.Ref("dungeon." + definition.Id), DungeonRules.FormatTime(DungeonRules.TimeLimitSeconds)), announcement: true);
        }
    }

    private void Tune(MonsterData data)
    {
        data.MaxHp = Math.Max(1, (int)(data.MaxHp * _dungeonTuning.HpScale));
        data.AttackDamage = Math.Max(1, (int)(data.AttackDamage * _dungeonTuning.DamageScale));
        data.AttackPower = (int)(data.AttackPower * _dungeonTuning.DamageScale);
    }

    /// <summary>Tira o jogador da dungeon e o leva para perto do portal na vila.</summary>
    private void ExitDungeon(ServerPlayer player, LocalText message)
    {
        var instance = FindDungeon(player.DungeonId);
        player.DungeonId = 0;
        instance?.PlayerIds.Remove(player.Id);
        if (player.IsDead)
        {
            player.IsDead = false;
            player.Hp.Add(player.Hp.Max);
        }
        player.Movement.Teleport(Portal.X, Portal.Z + 2.5f, double.IsNaN(_lastTickAt) ? 0 : _lastTickAt);
        player.StatsDirty = true;
        _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
        _outbox.Send(player.Id, new DungeonState { Active = false });
        Notify(player, message);
    }

    private void DestroyDungeon(DungeonInstance instance)
    {
        foreach (var id in instance.PlayerIds.ToList())
            if (_players.TryGetValue(id, out var p) && p.DungeonId == instance.Id) ExitDungeon(p, LT("dungeon.closed"));
        foreach (var monster in _monsters.Values.Where(m => m.InstanceId == instance.Id).ToList()) _monsters.Remove(monster.Id);
        foreach (var loot in _loot.Values.Where(l => l.InstanceId == instance.Id).ToList()) RemoveLoot(loot, 0);
        _dungeons.Remove(instance.Id);
    }

    // ===== Andamento =====

    private void TickDungeons(double now)
    {
        foreach (var instance in _dungeons.Values.ToList())
        {
            instance.PlayerIds.RemoveWhere(id => !_players.TryGetValue(id, out var p) || p.DungeonId != instance.Id);
            if (instance.PlayerIds.Count == 0)
            {
                DestroyDungeon(instance);
                continue;
            }
            if (instance.Completed)
            {
                if (now - instance.CompletedAt >= DungeonRules.CloseAfterCompletionSeconds) DestroyDungeon(instance);
                else SendDungeonState(instance);
                continue;
            }
            if (now - instance.StartedAt > DungeonRules.TimeLimitSeconds)
            {
                instance.Failed = true;
                foreach (var p in InstancePlayers(instance)) Notify(p, LT("dungeon.timeout"), announcement: true);
                DestroyDungeon(instance);
                continue;
            }

            // Sala limpa: abre o portão da próxima.
            while (!instance.InBossRoom && instance.RoomMonsters[instance.Room].All(id => FindMonster(id)?.IsDead ?? true))
            {
                instance.Room++;
                instance.Dirty = true;
                foreach (var p in InstancePlayers(instance))
                    Notify(p, LT("dungeon.gate_open", Loc.Ref("room." + instance.Definition.Rooms[instance.Room].Id)), announcement: true);
            }

            if (instance.InBossRoom) TickDungeonBoss(instance, now);
            if (instance.Completed) continue;

            var left = MonstersLeft(instance);
            if (left != instance.LastMonstersLeft)
            {
                instance.LastMonstersLeft = left;
                instance.Dirty = true;
            }
            SendDungeonState(instance);
        }
    }

    private void TickDungeonBoss(DungeonInstance instance, double now)
    {
        var rules = instance.Definition.Boss;
        var boss = FindMonster(instance.BossId);
        if (boss == null) return;
        if (boss.IsDead)
        {
            CompleteDungeon(instance, now);
            return;
        }

        // Escudo: nas frações de vida, fica imune e chama servos; cai quando os servos morrem.
        var fractions = rules.ShieldAtHpFractions;
        if (!instance.Shield && instance.ShieldsUsed < fractions.Length && boss.Hp.Current <= boss.Hp.Max * fractions[instance.ShieldsUsed])
        {
            instance.Shield = true;
            instance.ShieldsUsed++;
            boss.Invulnerable = true;
            instance.Servants.Clear();
            for (var i = 0; i < rules.ServantsPerShield; i++)
            {
                var angle = i * MathF.PI * 2 / rules.ServantsPerShield;
                var data = rules.Servant();
                Tune(data);
                var spawn = new SpawnDefinition { Data = data, Count = 1 };
                var servant = CreateMonster(spawn, boss.X + MathF.Cos(angle) * rules.ServantRadius,
                    boss.Z + MathF.Sin(angle) * rules.ServantRadius, instance.Id);
                instance.Servants.Add(servant.Id);
                instance.RoomMonsters[instance.Room].Add(servant.Id);
            }
            instance.Dirty = true;
            foreach (var p in InstancePlayers(instance))
                Notify(p, LT("dungeon.boss_shield", Loc.Ref("monster." + boss.Data.Id)), announcement: true);
        }
        else if (instance.Shield && instance.Servants.All(id => FindMonster(id)?.IsDead ?? true))
        {
            instance.Shield = false;
            boss.Invulnerable = false;
            instance.Dirty = true;
            foreach (var p in InstancePlayers(instance)) Notify(p, LT("dungeon.shield_down"), announcement: true);
        }

        // Golpe no chão: avisa onde vai cair e machuca quem não saiu do círculo.
        if (instance.PendingSlam is { } slam)
        {
            if (now < slam.LandsAt) return;
            instance.PendingSlam = null;
            instance.NextSlamAt = now + rules.SlamIntervalSeconds;
            var damage = Math.Max(1, (int)(rules.SlamDamage * _dungeonTuning.DamageScale));
            foreach (var p in InstancePlayers(instance).ToList())
            {
                if (!p.IsDead && Distance(p.X, p.Z, slam.X, slam.Z) <= rules.SlamRadius + PlayerRules.BodyRadius)
                    DamageMonsterToPlayer(boss, p, damage, CombatSource.BossArea, now);
            }
            return;
        }
        var fighting = boss.Brain.State is MonsterState.Chase or MonsterState.WindUp;
        var target = ChooseTarget(boss);
        if (!fighting || target == null || now < instance.NextSlamAt) return;
        instance.PendingSlam = (target.X, target.Z, now + rules.SlamWarningSeconds);
        SendNearby(boss.X, boss.Z, new AreaWarning
        {
            SourceId = boss.Id, X = target.X, Z = target.Z, Radius = rules.SlamRadius, Seconds = rules.SlamWarningSeconds,
        });
    }

    private void CompleteDungeon(DungeonInstance instance, double now)
    {
        instance.Completed = true;
        instance.CompletedAt = now;
        instance.Elapsed = now - instance.StartedAt;
        instance.Medal = DungeonRules.Medal(instance.Elapsed, instance.MaxCombo);
        instance.Dirty = true;
        var definition = instance.Definition;
        var rolls = DungeonRules.ChestRolls(instance.Medal);
        var players = InstancePlayers(instance).ToList();

        // Baú: cada um tem os próprios sorteios (só o dono pega o que saiu para ele).
        for (var p = 0; p < players.Count; p++)
        {
            var player = players[p];
            long alz = 0;
            var index = 0;
            for (var r = 0; r < rolls; r++)
            {
                var loot = definition.Chest.Roll(_random);
                alz += loot.Alz;
                foreach (var drop in loot.Items)
                {
                    var angle = p * 1.6f + index * 0.7f;
                    var distance = 1.2f + 0.35f * index++;
                    var id = _nextLootId++;
                    _loot.Add(id, new ServerLoot
                    {
                        Id = id, Drop = drop, OwnerId = player.Id, InstanceId = instance.Id,
                        X = instance.OriginX + MathF.Cos(angle) * distance, Z = instance.OriginZ + definition.ChestZ + MathF.Sin(angle) * distance,
                        OwnerUntil = now + DungeonRules.CloseAfterCompletionSeconds + 1, ExpiresAt = now + DungeonRules.CloseAfterCompletionSeconds + 1,
                    });
                }
            }
            player.Alz += alz;
            player.Session.Dungeons++;
            player.StatsDirty = true;
            Log(player, "bau_dungeon", null, 0, alz, $"{definition.Id} medalha {instance.Medal}, {rolls} sorteios");
            Notify(player, LT(rolls > 1 ? "dungeon.victory" : "dungeon.victory_one", DungeonRules.FormatTime(instance.Elapsed),
                Loc.Ref("medal." + instance.Medal), rolls, alz), announcement: true);
        }

        var run = new DungeonRunRecord(definition.Id, Math.Round(instance.Elapsed, 1), (int)instance.Medal, instance.MaxCombo,
            string.Join(",", instance.Members), DateTimeOffset.UtcNow);
        AddToRanking(run);
        if (instance.AllAccounts) RunCompleted?.Invoke(run);
        var info = InfoMessage(definition.Id);
        foreach (var player in players) _outbox.Send(player.Id, info);
        SendDungeonState(instance);
    }

    /// <summary>Guarda o maior combo do grupo na dungeon (entra na medalha).</summary>
    private void TrackCombo(ServerPlayer player, int hits)
    {
        var instance = FindDungeon(player.DungeonId);
        if (instance != null && !instance.Completed && hits > instance.MaxCombo) instance.MaxCombo = hits;
    }

    /// <summary>
    /// Paredes e portões: o jogador não passa das laterais nem do portão de uma sala que ainda tem monstros.
    /// Retorna verdadeiro se precisou corrigir a posição.
    /// </summary>
    private bool ClampToDungeon(ServerPlayer player, double now)
    {
        var instance = FindDungeon(player.DungeonId);
        if (instance == null) return false;
        var definition = instance.Definition;
        var minX = instance.OriginX - definition.HalfWidth + 0.5f;
        var maxX = instance.OriginX + definition.HalfWidth - 0.5f;
        var minZ = instance.OriginZ - 1f;
        var maxZ = instance.OriginZ + (instance.InBossRoom ? definition.Length : definition.Rooms[instance.Room].GateZ - 0.6f);
        var x = Math.Clamp(player.X, minX, maxX);
        var z = Math.Clamp(player.Z, minZ, maxZ);
        if (x == player.X && z == player.Z) return false;
        player.Movement.Teleport(x, z, now);
        return true;
    }

    private (float X, float Z)? DungeonRespawnPoint(ServerPlayer player)
    {
        var instance = FindDungeon(player.DungeonId);
        return instance == null ? null : (instance.EntranceX, instance.EntranceZ);
    }

    private IEnumerable<ServerPlayer> InstancePlayers(DungeonInstance instance)
    {
        foreach (var id in instance.PlayerIds)
            if (_players.TryGetValue(id, out var p)) yield return p;
    }

    private int MonstersLeft(DungeonInstance instance) =>
        instance.RoomMonsters[instance.Room].Count(id => FindMonster(id) is { IsDead: false });

    private void SendDungeonState(DungeonInstance instance)
    {
        if (!instance.Dirty) return;
        instance.Dirty = false;
        var state = StateMessage(instance);
        foreach (var player in InstancePlayers(instance)) _outbox.Send(player.Id, state);
    }

    private DungeonState StateMessage(DungeonInstance instance) => new()
    {
        Active = true, DungeonId = instance.Definition.Id, OriginX = instance.OriginX, OriginZ = instance.OriginZ,
        Room = (byte)instance.Room, Rooms = (byte)instance.Definition.Rooms.Count, MonstersLeft = (ushort)Math.Max(0, MonstersLeft(instance)),
        StartedAt = instance.StartedAt, LimitSeconds = (float)DungeonRules.TimeLimitSeconds, BossShield = instance.Shield,
        Completed = instance.Completed, Failed = instance.Failed, ElapsedSeconds = (float)instance.Elapsed,
        Medal = (byte)instance.Medal, MaxCombo = (ushort)Math.Min(instance.MaxCombo, ushort.MaxValue),
    };

    private DungeonInfo InfoMessage(string dungeonId)
    {
        var info = new DungeonInfo { DungeonId = dungeonId };
        foreach (var run in Ranking(dungeonId))
            info.Ranking.Add(new DungeonRankEntry { Seconds = (float)run.Seconds, Medal = (byte)run.Medal, MaxCombo = (ushort)run.MaxCombo, Members = run.Members });
        return info;
    }
}
