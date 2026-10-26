using System.Numerics;
using Game.Server.Persistence;
using Game.Shared.Combat;
using Game.Shared.Items;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Pvp;
using Game.Shared.Social;
using Game.Shared.Text;
using ComboPhase = Game.Shared.Combat.ComboState;
using Game.Shared.World;

namespace Game.Server.World;

/// <summary>Combate, monstros, drops e chefe — tudo decidido aqui, com as regras do Shared.</summary>
public sealed partial class GameWorld
{
    /// <summary>
    /// Maior compensação de latência aceita para o timing do combo. O cliente informa quando apertou;
    /// o servidor aceita esse horário se não for no futuro nem mais antigo que isto.
    /// </summary>
    public const double MaxLagCompensationSeconds = 0.35;

    /// <summary>Horário do aperto que o servidor usa para julgar o combo.</summary>
    public static double PressTime(double clientTime, double now) =>
        double.IsNaN(clientTime) ? now : Math.Clamp(clientTime, now - MaxLagCompensationSeconds, now);

    // ===== Jogador =====

    private void TickPlayer(ServerPlayer player, double now, float dt)
    {
        TrackSuspicion(player, now);
        ReleaseQueuedSkill(player, now);
        if (player.IsDead)
        {
            if (now >= player.RespawnAt) Respawn(player, now);
            return;
        }

        if (player.Combo.Tick(now))
        {
            player.ComboDirty = true;
            Notify(player, LT("combo.ended_timeout", player.Combo.HitCount));
        }

        player.MpRegenCarry += PlayerRules.MpRegenPerSecond * dt;
        var whole = (int)player.MpRegenCarry;
        if (whole > 0)
        {
            player.MpRegenCarry -= whole;
            if (player.Mp.Add(whole) != 0) player.StatsDirty = true;
        }
    }

    private void HandleStartCombo(ServerPlayer player, StartCombo message, double now)
    {
        if (player.IsDead || player.Combo.State == ComboPhase.Active) return;
        if (!player.ComboUnlocked)
        {
            Notify(player, LT("combo.locked", Loc.Ref("quest." + ClassCatalog.ComboQuestId)));
            return;
        }
        var target = FindMonster(message.TargetId);
        if ((target == null || target.IsDead) && HostilePlayer(player, message.TargetId) == null)
        {
            Notify(player, LT("combo.need_target"));
            return;
        }
        var cost = player.Combo.Config.ActivationSpCost;
        if (!player.Sp.TrySpend(cost))
        {
            Notify(player, LT("combo.no_sp", player.Sp.Current, cost));
            return;
        }
        player.Combo.TryStart(PressTime(message.ClientTime, now));
        player.ComboDirty = true;
        player.StatsDirty = true;
    }

    /// <summary>A conjuração acabou: a skill da fila é usada agora (se não esperou demais).</summary>
    private void ReleaseQueuedSkill(ServerPlayer player, double now)
    {
        if (player.QueuedSkill is not { } queued || now < player.CastUntil - CastSlackSeconds) return;
        player.QueuedSkill = null;
        if (player.IsDead || now - player.QueuedAt > SkillQueueSeconds) return;
        HandleUseSkill(player, new UseSkill { Slot = queued.Slot, TargetId = queued.TargetId, ClientTime = double.NaN }, now);
    }

    /// <summary>Folga para o atraso da rede: o cliente pede a próxima skill quando a conjuração acaba no relógio dele.</summary>
    public const double CastSlackSeconds = 0.12;

    /// <summary>Quanto tempo uma skill pode esperar na fila antes de ser esquecida.</summary>
    public const double SkillQueueSeconds = 1.5;

    private void HandleUseSkill(ServerPlayer player, UseSkill message, double now)
    {
        if (player.IsDead || message.Slot >= player.Skills.Count) return;
        var skill = player.Skills[message.Slot];
        if (!player.SkillUnlocked(message.Slot))
        {
            Notify(player, LT("skill.locked", Loc.Ref("skill." + skill.Id)));
            return;
        }
        // Conjurando outra skill (fora do combo): esta entra na fila e sai quando a atual acabar.
        var press = PressTime(message.ClientTime, now);
        if (player.Combo.State != ComboPhase.Active && press < player.CastUntil - CastSlackSeconds)
        {
            player.QueuedSkill = message;
            player.QueuedAt = now;
            return;
        }
        var target = TargetMonster(player, message.TargetId);
        // Sem monstro: pode ser um jogador hostil (duelo começado ou time inimigo na arena).
        var enemy = target == null ? HostilePlayer(player, message.TargetId) : null;
        double? distance = target != null && !target.IsDead ? EdgeDistance(player, target)
            : enemy != null ? Distance(player.X, player.Z, enemy.X, enemy.Z) - PlayerRules.BodyRadius
            : (double?)null;

        switch (SkillRules.Check(skill, message.Slot, player.Cooldowns, player.Mp, distance, now))
        {
            case SkillUseCheck.NoTarget:
                Notify(player, LT("combat.need_target"));
                return;
            case SkillUseCheck.OnCooldown:
                if (player.Combo.State == ComboPhase.Active)
                {
                    // Skill em recarga durante o combo quebra a corrente.
                    player.Combo.Cancel();
                    player.ComboDirty = true;
                    Notify(player, LT("combo.ended_cooldown", Loc.Ref("skill." + skill.Id)));
                }
                else
                {
                    Notify(player, LT("skill.cooldown", Loc.Ref("skill." + skill.Id)));
                }
                return;
            case SkillUseCheck.OutOfRange:
                Notify(player, LT("combat.out_of_range"));
                return;
            case SkillUseCheck.NotEnoughMp:
                Notify(player, LT("combat.no_mp", player.Mp.Current, skill.MpCost));
                return;
        }

        var multiplier = 1.0;
        byte grade = 255;
        var routeId = "";
        var inCombo = player.Combo.State == ComboPhase.Active;
        if (inCombo)
        {
            var result = player.Combo.Press(PressTime(message.ClientTime, now), skill.Kind);
            player.ComboDirty = true;
            TrackComboGrade(player, result.Grade, now);
            if (result.ComboEnded)
            {
                Notify(player, LT("combo.ended_window", result.HitCount));
            }
            else
            {
                multiplier = result.DamageMultiplier;
                grade = (byte)result.Grade;
                TrackCombo(player, result.HitCount);
                var gauge = player.Awakening.Gauge;
                player.Awakening.AddFromGrade(result.Grade, now);
                if (player.Awakening.Gauge != gauge) player.SheetDirty = true;
                routeId = result.Route?.Id ?? "";
            }
        }

        SkillRules.TryUse(skill, message.Slot, player.Cooldowns, player.Mp, distance, now);
        player.StatsDirty = true;
        if (!inCombo) player.CastUntil = press + skill.CastSeconds;

        var power = ClassCatalog.SkillPower(skill, player.Stats);
        var hits = 0;
        if (enemy != null)
        {
            var pvpMultiplier = multiplier * PvpRules.AwakeningMultiplier(player.Awakening.DamageMultiplier(now));
            foreach (var victim in PvpTargets(player, skill, enemy))
            {
                var hit = DamageFormula.Calculate(
                    new DamageInput(skill.BaseDamage, power, pvpMultiplier, CritChance(player),
                        PlayerRules.CritMultiplier, victim.Stats.Defense), _random);
                hits++;
                DamagePlayerToPlayer(player, victim, hit, message.Slot, grade, routeId, now);
            }
            if (!inCombo && hits > 0) player.Sp.Add(PlayerRules.SpPerHit);
            return;
        }

        multiplier *= player.Awakening.DamageMultiplier(now);
        foreach (var victim in SkillTargets(skill, target!))
        {
            var hit = DamageFormula.Calculate(
                new DamageInput(skill.BaseDamage, power, multiplier, CritChance(player),
                    PlayerRules.CritMultiplier, victim.Data.Defense), _random);
            hits++;
            DamagePlayerToMonster(player, victim, hit, message.Slot, grade, routeId, now);
        }
        if (!inCombo && hits > 0) player.Sp.Add(PlayerRules.SpPerHit);
    }

    private void HandleFinisher(ServerPlayer player, Finisher message, double now)
    {
        if (player.IsDead || !player.Combo.CanFinish) return;
        var target = TargetMonster(player, message.TargetId);
        var enemy = target == null ? HostilePlayer(player, message.TargetId) : null;
        var inReach = target != null && !target.IsDead && EdgeDistance(player, target) <= PlayerRules.FinisherRange ||
                      enemy != null && Distance(player.X, player.Z, enemy.X, enemy.Z) - PlayerRules.BodyRadius <= PlayerRules.FinisherRange;
        if (!inReach)
        {
            Notify(player, LT("combo.finisher_closer"));
            return;
        }

        var chain = player.Combo.HitCount;
        player.Combo.TryFinish(out var multiplier);
        player.ComboDirty = true;
        // O finalizador usa o poder principal da classe (o maior dos dois).
        var power = Math.Max(player.Stats.AttackPower, player.Stats.MagicPower);
        if (enemy != null)
        {
            var pvpHit = DamageFormula.Calculate(
                new DamageInput(PlayerRules.FinisherBaseDamage, power, multiplier * PvpRules.AwakeningMultiplier(player.Awakening.DamageMultiplier(now)),
                    CritChance(player), PlayerRules.CritMultiplier, enemy.Stats.Defense), _random);
            DamagePlayerToPlayer(player, enemy, pvpHit, CombatSource.Finisher, 255, "", now);
            Notify(player, LT("combo.finisher", multiplier, chain));
            return;
        }
        target = target!;
        var hit = DamageFormula.Calculate(
            new DamageInput(PlayerRules.FinisherBaseDamage, power, multiplier * player.Awakening.DamageMultiplier(now),
                CritChance(player), PlayerRules.CritMultiplier, target.Data.Defense), _random);
        DamagePlayerToMonster(player, target, hit, CombatSource.Finisher, 255, "", now);
        Notify(player, LT("combo.finisher", multiplier, chain));
    }

    private IEnumerable<ServerMonster> SkillTargets(SkillData skill, ServerMonster main)
    {
        if (skill.AreaRadius <= 0)
        {
            yield return main;
            yield break;
        }
        foreach (var monster in _monsters.Values.ToList())
        {
            if (!monster.IsDead && monster.InstanceId == main.InstanceId && InRange(monster.X, monster.Z, main.X, main.Z, skill.AreaRadius))
                yield return monster;
        }
    }

    /// <summary>
    /// Monstro que o jogador pode atacar: só os da instância dele (mundo aberto ou a dungeon do grupo). Sem isso, um
    /// cliente modificado que andasse até a área das dungeons bateria nos monstros de outros grupos.
    /// </summary>
    private ServerMonster? TargetMonster(ServerPlayer player, int id) =>
        FindMonster(id) is { } monster && SameInstance(monster, player) ? monster : null;

    private void DamagePlayerToMonster(ServerPlayer player, ServerMonster monster, DamageResult hit, byte source,
        byte grade, string routeId, double now)
    {
        // Escudo: o golpe acerta, mas não tira vida (o cliente mostra 0).
        var dealt = monster.Invulnerable ? 0 : -monster.Hp.Add(-hit.Amount);
        monster.DamageBy[player.Id] = monster.DamageBy.GetValueOrDefault(player.Id) + dealt;
        monster.AggroPlayerId = player.Id;
        if (monster.Data.Behavior == MonsterBehavior.Pack && !monster.Brain.Provoked) CallPack(monster, player);
        monster.Brain.Provoke();

        SendNearby(monster.X, monster.Z, new CombatEvent
        {
            AttackerId = player.Id, TargetId = monster.Id, Source = source, Damage = dealt, Critical = hit.Critical,
            Grade = grade, RouteId = routeId,
        });

        if (monster.Hp.Current == 0) KillMonster(monster, now);
    }

    private void Respawn(ServerPlayer player, double now)
    {
        player.IsDead = false;
        player.Hp.Add(player.Hp.Max);
        player.Mp.Add(player.Mp.Max);
        var dungeon = ArenaRespawnPoint(player) ?? DungeonRespawnPoint(player);
        // Mundo aberto: a fogueira mais perto de onde morreu (a vila é a primeira delas).
        var camp = dungeon == null ? WorldContent.NearestRespawn(player.X, player.Z) : null;
        var village = camp == WorldContent.RespawnPoints[0];
        var (spawnX, spawnZ) = dungeon ?? (village ? SpawnPointFor(player.Id) : AroundCamp(camp!, player.Id));
        player.Movement.Teleport(spawnX, spawnZ, now);
        player.StatsDirty = true;
        _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
        Notify(player, player.ArenaId != 0 ? LT("respawn.arena_base") : dungeon != null ? LT("respawn.crypt")
            : village ? LT("respawn.village") : LT("respawn.camp", camp!.Name));
    }

    /// <summary>Um lugar em volta da fogueira (fora do fogo), espalhando quem renasce junto.</summary>
    private static (float X, float Z) AroundCamp(RespawnPoint camp, int playerId)
    {
        var angle = playerId * 2.39996f;
        var radius = 2.5f + playerId % 3 * 0.75f;
        return (camp.X + MathF.Cos(angle) * radius, camp.Z + MathF.Sin(angle) * radius);
    }

    private void DamageMonsterToPlayer(ServerMonster monster, ServerPlayer player, int baseDamage, byte source, double now)
    {
        if (player.IsDodging(now))
        {
            SendNearby(player.X, player.Z, new CombatEvent { AttackerId = monster.Id, TargetId = player.Id, Source = source, Dodged = true });
            return;
        }

        var hit = DamageFormula.Calculate(
            new DamageInput(baseDamage, monster.Data.AttackPower, 1.0, 0.05, 1.5, player.Stats.Defense), _random);
        var dealt = -player.Hp.Add(-(monster.IsElite ? EliteRules.Damage(hit.Amount) : hit.Amount));
        player.StatsDirty = true;
        SendNearby(player.X, player.Z, new CombatEvent
        {
            AttackerId = monster.Id, TargetId = player.Id, Source = source, Damage = dealt, Critical = hit.Critical,
        });

        if (player.Hp.Current > 0) return;
        player.IsDead = true;
        player.Session.Deaths++;
        player.RespawnAt = now + PlayerRules.RespawnSeconds;
        if (player.Combo.State == ComboPhase.Active)
        {
            player.Combo.Cancel();
            player.ComboDirty = true;
        }
        foreach (var m in _monsters.Values)
        {
            if (m.AggroPlayerId == player.Id) m.AggroPlayerId = 0;
        }
        Notify(player, LT("player.died"), announcement: true);
    }

    // ===== Monstros =====

    private void TickMonster(ServerMonster monster, double now, float dt)
    {
        if (monster.IsDead)
        {
            if (monster.Spawn.Scheduled) TickScheduledBoss(monster, now);
            else if (monster.InstanceId == 0 && now >= monster.RespawnAt) RespawnMonster(monster, now);
            return;
        }

        var target = ChooseTarget(monster);
        var action = monster.Brain.Tick(now, new Vector2(monster.X, monster.Z),
            target != null ? new Vector2(target.X, target.Z) : (Vector2?)null, monster.Hp.Current / (float)monster.Hp.Max);

        switch (action.Kind)
        {
            case MonsterActionKind.MoveTo:
                MoveMonster(monster, action.Point, dt, action.SpeedFactor);
                break;
            case MonsterActionKind.Face:
                Face(monster, action.Point);
                break;
            case MonsterActionKind.AttackStarted:
                Face(monster, action.Point);
                // Golem: o golpe acerta em volta dele; o chão avisa onde, pelo tempo do preparo.
                if (monster.Data.Behavior == MonsterBehavior.AreaSlam)
                    SendNearby(monster.X, monster.Z, new AreaWarning
                    {
                        SourceId = monster.Id, X = monster.X, Z = monster.Z, Radius = MonsterBehaviors.SlamRadius,
                        Seconds = monster.Data.AttackWindupSeconds,
                    });
                break;
            case MonsterActionKind.AttackLanded:
                if (monster.Data.Behavior == MonsterBehavior.AreaSlam)
                {
                    foreach (var player in _players.Values.ToList())
                    {
                        if (!player.IsDead && SameInstance(monster, player) &&
                            Distance(monster.X, monster.Z, player.X, player.Z) <= MonsterBehaviors.SlamRadius + PlayerRules.BodyRadius)
                            DamageMonsterToPlayer(monster, player, monster.Data.AttackDamage, CombatSource.MonsterAttack, now);
                    }
                }
                else if (target != null && !target.IsDead &&
                    Distance(monster.X, monster.Z, target.X, target.Z) - PlayerRules.BodyRadius
                    <= monster.Data.AttackRange * PlayerRules.MonsterReachTolerance)
                {
                    DamageMonsterToPlayer(monster, target, monster.Data.AttackDamage, CombatSource.MonsterAttack, now);
                }
                break;
            case MonsterActionKind.Blink:
                // Espectro: some e reaparece (o cliente vê o salto entre um retrato e outro).
                monster.X = action.Point.X;
                monster.Z = action.Point.Y;
                if (target != null) Face(monster, new Vector2(target.X, target.Z));
                break;
            case MonsterActionKind.ResetHealth:
                ResetMonster(monster);
                break;
        }

        if (monster.Spawn.IsBoss) TickBoss(monster, target, now);
    }

    /// <summary>Quem o monstro persegue: quem bateu nele por último; se ninguém, o jogador vivo mais perto.</summary>
    private ServerPlayer? ChooseTarget(ServerMonster monster)
    {
        if (monster.AggroPlayerId != 0 && _players.TryGetValue(monster.AggroPlayerId, out var aggro) && !aggro.IsDead)
            return aggro;

        ServerPlayer? nearest = null;
        var best = float.MaxValue;
        foreach (var player in _players.Values)
        {
            if (player.IsDead) continue;
            var distance = Distance(monster.X, monster.Z, player.X, player.Z);
            if (distance < best)
            {
                best = distance;
                nearest = player;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Lobo apanhou: os lobos da mesma espécie por perto (e no mesmo lugar: mundo aberto ou a mesma dungeon) entram na
    /// briga contra quem bateu.
    /// </summary>
    private void CallPack(ServerMonster caller, ServerPlayer player)
    {
        foreach (var other in _monsters.Values)
        {
            if (other == caller || other.IsDead || other.Data.Id != caller.Data.Id || other.InstanceId != caller.InstanceId) continue;
            if (other.Brain.Provoked || !InRange(other.X, other.Z, caller.X, caller.Z, MonsterBehaviors.PackCallRadius)) continue;
            other.AggroPlayerId = player.Id;
            other.Brain.Provoke();
        }
    }

    private static bool SameInstance(ServerMonster monster, ServerPlayer player) =>
        player.DungeonId == monster.InstanceId;

    private static void MoveMonster(ServerMonster monster, Vector2 point, float dt, float speedFactor = 1f)
    {
        var dx = point.X - monster.X;
        var dz = point.Y - monster.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance < 0.01f) return;
        var step = MathF.Min(distance, monster.Data.MoveSpeed * speedFactor * dt);
        monster.X += dx / distance * step;
        monster.Z += dz / distance * step;
        monster.Yaw = MathF.Atan2(dx, dz) * 180f / MathF.PI;
    }

    private static void Face(ServerMonster monster, Vector2 point)
    {
        var dx = point.X - monster.X;
        var dz = point.Y - monster.Z;
        if (dx * dx + dz * dz > 0.0001f) monster.Yaw = MathF.Atan2(dx, dz) * 180f / MathF.PI;
    }

    private void ResetMonster(ServerMonster monster)
    {
        monster.Hp.Add(monster.Hp.Max);
        monster.DamageBy.Clear();
        monster.AggroPlayerId = 0;
        if (monster.Spawn.IsBoss) EndBossPhaseTwo(monster);
    }

    private void KillMonster(ServerMonster monster, double now)
    {
        monster.Brain.Kill();
        monster.RespawnAt = now + monster.Data.RespawnSeconds;
        monster.PendingArea = null;

        // Quem causou mais dano (somando o grupo) fica com o XP e o drop; dentro do grupo, quem bateu mais é o "dono".
        var killer = TopDamager(monster);
        // Chefe de mundo: todo mundo que bateu (e está online) ganha o crédito, cada um com a XP inteira.
        var fighters = monster.Spawn.Scheduled
            ? monster.DamageBy.Where(d => d.Value > 0).Select(d => _players.GetValueOrDefault(d.Key)).OfType<ServerPlayer>().ToList()
            : null;
        var helpers = Helpers(monster);
        monster.DamageBy.Clear();
        monster.AggroPlayerId = 0;
        if (killer == null) return;

        // Divide com os membros vivos perto do monstro; quem matou vem primeiro (fica com o resto do Alz).
        var sharing = fighters != null
            ? fighters.OrderBy(m => m == killer ? 0 : 1).ToList()
            : PartyMembers(killer)
                .Where(m => m == killer || (!m.IsDead && InRange(m.X, m.Z, monster.X, monster.Z, PartyRules.ShareRadius)))
                .OrderBy(m => m == killer ? 0 : 1)
                .ToList();
        if (fighters != null) AnnounceBossDefeated(monster, killer, sharing.Count);
        var loot = monster.IsElite ? EliteRules.RollLoot(monster.Data.Drops, _random) : monster.Data.Drops.Roll(_random);
        var xpReward = monster.IsElite ? EliteRules.Xp(monster.Data.XpReward) : monster.Data.XpReward;
        var alzParts = PartyRules.SplitAlz(loot.Alz, sharing.Count);
        for (var i = 0; i < sharing.Count; i++)
        {
            var member = sharing[i];
            var xp = sharing.Count == 1 || fighters != null
                ? XpRules.Adjusted(xpReward, member.Progress.Level, monster.Data.Level)
                : PartyRules.SharedXp(xpReward, monster.Data.Level, member.Progress.Level, sharing.Count);
            GrantXp(member, xp);
            member.Session.Kills++;
            if (member.Quests.OnKill(monster.Data.Id).Count > 0) member.QuestsDirty = true;
            member.Alz += alzParts[i];
            member.StatsDirty = true;
            if (member.CharacterId != 0 && alzParts[i] > 0)
                member.PendingLogs.Add(new ItemLogEntry(member.CharacterId, DateTimeOffset.UtcNow, "alz_monstro", null, 0, alzParts[i],
                    $"{monster.Data.Id} (id {monster.Id}){(sharing.Count > 1 ? $", grupo de {sharing.Count}" : "")}"));
            Notify(member, LT("reward.xp_alz", xp, alzParts[i]));
        }

        // Quem ajudou de fora do grupo vencedor (com dano suficiente) também ganha o XP e conta na missão; o Alz e o
        // drop ficam com quem matou. Sem isso, em acampamento cheio o abate ia todo para quem bateu mais.
        foreach (var helper in helpers.Where(h => !sharing.Contains(h) && !h.IsDead))
        {
            var xp = XpRules.Adjusted(xpReward, helper.Progress.Level, monster.Data.Level);
            GrantXp(helper, xp);
            helper.Session.Kills++;
            if (helper.Quests.OnKill(monster.Data.Id).Count > 0) helper.QuestsDirty = true;
            helper.StatsDirty = true;
            Notify(helper, LT("reward.xp_alz", xp, 0));
        }

        for (var i = 0; i < loot.Items.Count; i++)
        {
            var angle = i * 2.4f;
            var id = _nextLootId++;
            _loot.Add(id, new ServerLoot
            {
                Id = id, Drop = loot.Items[i], OwnerId = killer.Id, OwnerPartyId = killer.PartyId,
                X = monster.X + MathF.Cos(angle) * (0.6f + 0.25f * i), Z = monster.Z + MathF.Sin(angle) * (0.6f + 0.25f * i),
                OwnerUntil = now + ServerLoot.OwnerSeconds, ExpiresAt = now + ServerLoot.LifetimeSeconds,
            });
        }
    }

    /// <summary>
    /// Quem leva o crédito: soma o dano por grupo (ou por jogador sozinho), o maior vence, e dentro dele
    /// quem causou mais dano. Nulo se ninguém que bateu está online.
    /// </summary>
    private ServerPlayer? TopDamager(ServerMonster monster)
    {
        var online = monster.DamageBy
            .Where(kv => _players.ContainsKey(kv.Key))
            .Select(kv => (Player: _players[kv.Key], Damage: kv.Value))
            .ToList();
        if (online.Count == 0) return null;
        var best = online
            .GroupBy(e => e.Player.PartyId != 0 ? e.Player.PartyId : -e.Player.Id)
            .MaxBy(g => g.Sum(e => (long)e.Damage))!;
        return best.MaxBy(e => e.Damage).Player;
    }

    /// <summary>Parte do dano total que dá direito ao XP de quem ajudou a matar sem estar no grupo vencedor.</summary>
    public const double HelperDamageShare = 0.15;

    /// <summary>Jogadores online que causaram pelo menos <see cref="HelperDamageShare"/> do dano no monstro.</summary>
    private List<ServerPlayer> Helpers(ServerMonster monster)
    {
        var total = monster.DamageBy.Values.Sum(d => (long)d);
        if (total <= 0) return new List<ServerPlayer>();
        return monster.DamageBy
            .Where(kv => kv.Value >= total * HelperDamageShare && _players.ContainsKey(kv.Key))
            .Select(kv => _players[kv.Key])
            .ToList();
    }

    private void RespawnMonster(ServerMonster monster, double now)
    {
        monster.X = monster.HomeX;
        monster.Z = monster.HomeZ;
        var wasElite = monster.IsElite;
        RollElite(monster);
        monster.Brain.Revive(now);
        if (monster.Spawn.IsBoss) EndBossPhaseTwo(monster);
        if (monster.IsElite != wasElite) ForgetMonster(monster);
    }

    /// <summary>Sorteia se o monstro (re)nasce elite, ajusta a vida máxima e enche a vida.</summary>
    private void RollElite(ServerMonster monster)
    {
        monster.IsElite = EliteRules.Roll(monster.Spawn, monster.InstanceId, _random, _eliteChance);
        monster.Hp.SetMax(monster.IsElite ? EliteRules.MaxHp(monster.Data.MaxHp) : monster.Data.MaxHp);
        monster.Hp.Add(monster.Hp.Max);
    }

    /// <summary>
    /// O monstro mudou de vida máxima e de aparência (virou ou deixou de ser elite): quem o via o esquece, e o próximo
    /// SendView manda o EntitySpawned de novo.
    /// </summary>
    private void ForgetMonster(ServerMonster monster)
    {
        foreach (var player in _players.Values)
        {
            if (player.KnownEntities.Remove(monster.Id)) _outbox.Send(player.Id, new EntityDespawned { Id = monster.Id });
        }
    }

    // ===== Chefe de mundo com horário =====

    /// <summary>Dormindo: avisa o servidor 10 min antes e desperta na hora marcada.</summary>
    private void TickScheduledBoss(ServerMonster boss, double now)
    {
        var clock = Clock();
        // Primeira conferência (o servidor acabou de ligar): o horário atual já passou; espera o próximo.
        if (boss.LastSlot == default) boss.LastSlot = WorldBossRules.CurrentSlot(clock);
        var next = WorldBossRules.NextSlot(clock);
        if (next != boss.WarnedSlot && next - clock <= TimeSpan.FromMinutes(WorldBossRules.WarningMinutes))
        {
            boss.WarnedSlot = next;
            var minutes = Math.Max(1, (int)Math.Ceiling((next - clock).TotalMinutes));
            foreach (var player in _players.Values)
                Notify(player, LT("boss.warning", Loc.Ref("monster." + boss.Data.Id), minutes), announcement: true);
        }
        var slot = WorldBossRules.CurrentSlot(clock);
        if (slot <= boss.LastSlot) return;
        WakeScheduledBoss(boss, now, slot);
    }

    private void WakeScheduledBoss(ServerMonster boss, double now, DateTimeOffset slot)
    {
        boss.LastSlot = slot;
        RespawnMonster(boss, now);
        foreach (var player in _players.Values)
            Notify(player, LT("boss.awake", Loc.Ref("monster." + boss.Data.Id)), announcement: true);
    }

    /// <summary>/gm chefe: desperta o chefe de mundo agora (para testar ou para um evento).</summary>
    private bool SummonWorldBoss(double now)
    {
        var boss = _monsters.Values.FirstOrDefault(m => m.Spawn.Scheduled);
        if (boss == null || !boss.IsDead) return false;
        WakeScheduledBoss(boss, now, WorldBossRules.CurrentSlot(Clock()));
        return true;
    }

    private void AnnounceBossDefeated(ServerMonster boss, ServerPlayer top, int fighters)
    {
        var text = fighters > 1
            ? LT("boss.defeated", Loc.Ref("monster." + boss.Data.Id), top.Name, fighters - 1)
            : LT("boss.defeated_solo", Loc.Ref("monster." + boss.Data.Id), top.Name);
        foreach (var player in _players.Values) Notify(player, text, announcement: true);
    }

    // ===== Chefe =====

    private void TickBoss(ServerMonster boss, ServerPlayer? target, double now)
    {
        var fraction = boss.Hp.Current / (float)boss.Hp.Max;
        if (!boss.PhaseTwo && fraction <= BossRules.PhaseTwoAtHpFraction)
        {
            boss.PhaseTwo = true;
            boss.Data.AttackCooldownSeconds = (float)(boss.BaseAttackCooldown * BossRules.PhaseTwoCooldownFactor);
            boss.NextAreaAt = now + 1.5;
            foreach (var player in _players.Values)
            {
                if (InRange(player.X, player.Z, boss.X, boss.Z, InterestRadius))
                    Notify(player, LT("boss.enraged", Loc.Ref("monster." + boss.Data.Id)), announcement: true);
            }
        }
        if (!boss.PhaseTwo) return;
        if (!boss.PhaseThree && fraction <= BossRules.PhaseThreeAtHpFraction)
        {
            boss.PhaseThree = true;
            boss.NextAreaAt = Math.Min(boss.NextAreaAt, now + 1.0);
            foreach (var player in _players.Values)
            {
                if (InRange(player.X, player.Z, boss.X, boss.Z, InterestRadius))
                    Notify(player, LT("boss.desperate", Loc.Ref("monster." + boss.Data.Id)), announcement: true);
            }
        }

        if (boss.PendingArea is { } area)
        {
            if (now < area.LandsAt) return;
            var second = boss.PendingSecondArea;
            boss.PendingArea = null;
            boss.PendingSecondArea = null;
            boss.NextAreaAt = now + BossRules.AreaIntervalSeconds * (boss.PhaseThree ? BossRules.PhaseThreeIntervalFactor : 1f);
            foreach (var player in _players.Values.ToList())
            {
                if (player.IsDead) continue;
                var inFirst = Distance(player.X, player.Z, area.X, area.Z) <= BossRules.AreaRadius + PlayerRules.BodyRadius;
                var inSecond = second is { } s && Distance(player.X, player.Z, s.X, s.Z) <= BossRules.AreaRadius + PlayerRules.BodyRadius;
                // Quem está nos dois círculos leva um golpe só.
                if (inFirst || inSecond) DamageMonsterToPlayer(boss, player, BossRules.AreaDamage, CombatSource.BossArea, now);
            }
            return;
        }

        var fighting = boss.Brain.State is MonsterState.Chase or MonsterState.WindUp;
        if (!fighting || target == null || now < boss.NextAreaAt) return;
        boss.PendingArea = (target.X, target.Z, now + BossRules.AreaWarningSeconds);
        SendNearby(boss.X, boss.Z, new AreaWarning
        {
            SourceId = boss.Id, X = target.X, Z = target.Z, Radius = BossRules.AreaRadius, Seconds = BossRules.AreaWarningSeconds,
        });
        if (!boss.PhaseThree) return;
        // Terceira fase: um segundo círculo ao lado do primeiro, num ângulo sorteado.
        var angle = _random.NextDouble() * Math.PI * 2;
        var secondX = target.X + (float)Math.Cos(angle) * BossRules.PhaseThreeSecondAreaOffset;
        var secondZ = target.Z + (float)Math.Sin(angle) * BossRules.PhaseThreeSecondAreaOffset;
        boss.PendingSecondArea = (secondX, secondZ);
        SendNearby(boss.X, boss.Z, new AreaWarning
        {
            SourceId = boss.Id, X = secondX, Z = secondZ, Radius = BossRules.AreaRadius, Seconds = BossRules.AreaWarningSeconds,
        });
    }

    private static void EndBossPhaseTwo(ServerMonster boss)
    {
        boss.PhaseTwo = false;
        boss.PhaseThree = false;
        boss.PendingArea = null;
        boss.PendingSecondArea = null;
        boss.Data.AttackCooldownSeconds = (float)boss.BaseAttackCooldown;
    }

    // ===== Itens no chão =====

    private void HandlePickup(ServerPlayer player, PickupLoot message, double now)
    {
        if (player.IsDead || !_loot.TryGetValue(message.LootId, out var loot)) return;
        if (Distance(player.X, player.Z, loot.X, loot.Z) > PlayerRules.PickupRange + 0.5f)
        {
            Notify(player, LT("loot.too_far"));
            return;
        }
        if (!loot.CanBePickedBy(player.Id, player.PartyId, now))
        {
            Notify(player, LT("loot.not_yours"));
            return;
        }

        var drop = loot.Drop;
        if (ItemCatalog.Find(drop.ItemId) == null)
        {
            RemoveLoot(loot, 0); // item que não existe mais no catálogo: só some do chão
            return;
        }
        if (!player.Bag.TryAdd(drop.ItemId, drop.Quantity, _uids.Next))
        {
            Notify(player, LT("bag.full_short"));
            return;
        }
        player.InventoryDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        // O item sai do chão no mesmo passo em que entra no inventário: não existe momento com dois.
        RemoveLoot(loot, player.Id);
        if (player.CharacterId != 0)
            player.PendingLogs.Add(new ItemLogEntry(player.CharacterId, DateTimeOffset.UtcNow, "coleta", drop.ItemId, drop.Quantity, 0,
                $"item no chão {loot.Id}, dono {loot.OwnerId}"));
        Notify(player, LT("loot.picked", Loc.Ref("item." + drop.ItemId), drop.Quantity));
    }

    private void ExpireLoot(double now)
    {
        foreach (var loot in _loot.Values.Where(l => now >= l.ExpiresAt).ToList()) RemoveLoot(loot, 0);
    }

    private void RemoveLoot(ServerLoot loot, int pickedBy)
    {
        _loot.Remove(loot.Id);
        foreach (var player in _players.Values)
        {
            if (player.KnownLoot.Remove(loot.Id))
                _outbox.Send(player.Id, new LootRemoved { LootId = loot.Id, PickedById = pickedBy });
        }
    }

    /// <summary>
    /// Soma XP e trata a subida de nível (vida e MP cheios, novos máximos, pontos de atributo). Retorna os níveis ganhos.
    /// </summary>
    private int GrantXp(ServerPlayer player, long xp)
    {
        var levels = player.Progress.AddXp((int)Math.Min(int.MaxValue, Math.Max(0, xp)));
        player.StatsDirty = true;
        if (levels == 0) return 0;
        player.RecalculateStats();
        player.Hp.Add(player.Hp.Max);
        player.Mp.Add(player.Mp.Max);
        Notify(player, LT("level.up", player.Progress.Level, levels * Attributes.PointsPerLevel), announcement: true);
        NotifyWatchers(player, null); // o nível aparece na lista de amigos
        if (player.CharacterId != 0)
            player.PendingLogs.Add(new ItemLogEntry(player.CharacterId, DateTimeOffset.UtcNow, "nivel", null, 0, 0, $"nível {player.Progress.Level}"));
        return levels;
    }

    private static double CritChance(ServerPlayer player) => player.Stats.CritPercent / 100.0;

    private static double EdgeDistance(ServerPlayer player, ServerMonster monster) =>
        Distance(player.X, player.Z, monster.X, monster.Z) - monster.Data.BodyRadius;
}
