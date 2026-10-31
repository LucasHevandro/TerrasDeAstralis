using System.Numerics;
using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Core;
using Game.Shared.Items;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;
using ComboPhase = Game.Shared.Combat.ComboState;
using ComboStateMessage = Game.Shared.Net.ComboState;
using Game.Shared.World;

namespace Game.Server.World;

/// <summary>Para onde o mundo manda mensagens. A rede implementa; os testes usam uma lista.</summary>
public interface IOutbox
{
    void Send(int playerId, INetMessage message);
}

/// <summary>
/// O mundo do jogo no servidor, sem rede: recebe mensagens já decodificadas, avança no tempo
/// e responde pelo <see cref="IOutbox"/>. Tudo roda numa única thread (o loop de ticks).
/// Este arquivo cuida de entrada/saída, movimento e do que cada jogador enxerga;
/// combate, monstros e drops ficam em GameWorld.Combat.cs.
/// </summary>
public sealed partial class GameWorld
{
    public const byte TickRate = 20;

    /// <summary>Distância até onde um jogador recebe entidades e itens.</summary>
    public const float InterestRadius = 60f;

    private readonly IOutbox _outbox;
    private readonly IRandomSource _random;
    private readonly ItemUidAllocator _uids;
    private readonly DungeonTuning _dungeonTuning;
    private readonly int _arenaTeamSize;

    /// <summary>Chance de um monstro do mundo aberto nascer elite (os testes passam 0 ou 1).</summary>
    private readonly double _eliteChance;
    private readonly Dictionary<int, ServerPlayer> _players = new();
    private readonly Dictionary<int, ServerMonster> _monsters = new();
    private readonly Dictionary<int, ServerLoot> _loot = new();
    private int _nextEntityId = 1;
    private int _nextLootId = 1;
    private uint _tick;
    private double _lastTickAt = double.NaN;

    /// <summary>
    /// Cria o mundo. <paramref name="spawns"/> nulo usa o conteúdo padrão (<see cref="WorldContent"/>);
    /// os testes passam grupos próprios e uma fonte aleatória fixa.
    /// </summary>
    public GameWorld(IOutbox outbox, IEnumerable<SpawnDefinition>? spawns = null, IRandomSource? random = null,
        ItemUidAllocator? uids = null, DungeonTuning? dungeonTuning = null, int arenaTeamSize = Game.Shared.Pvp.ArenaRules.TeamSize,
        double eliteChance = EliteRules.Chance)
    {
        _outbox = outbox;
        _arenaTeamSize = Math.Max(1, arenaTeamSize);
        _dungeonTuning = dungeonTuning ?? DungeonTuning.Normal;
        _random = random ?? new SystemRandomSource();
        _eliteChance = eliteChance;
        _uids = uids ?? new ItemUidAllocator();

        var seed = 1;
        foreach (var spawn in spawns ?? WorldContent.Spawns())
        {
            spawn.Data.Validate();
            foreach (var (x, z) in spawn.SpawnPoints(seed++)) CreateMonster(spawn, x, z);
        }
    }

    public IReadOnlyCollection<ServerPlayer> Players => _players.Values;
    public IReadOnlyCollection<ServerMonster> Monsters => _monsters.Values;
    public IReadOnlyCollection<ServerLoot> Loot => _loot.Values;
    public uint CurrentTick => _tick;

    public ServerPlayer? FindPlayer(int id) => _players.TryGetValue(id, out var p) ? p : null;
    public ServerMonster? FindMonster(int id) => _monsters.TryGetValue(id, out var m) ? m : null;

    /// <summary>
    /// Entra um jogador. <paramref name="onIdAssigned"/> roda antes de qualquer mensagem sair, para a rede saber para
    /// onde mandar. As entidades por perto chegam no primeiro tick.
    /// Com <paramref name="character"/> (vindo do banco), volta exatamente como estava: nível, XP, recursos, Alz,
    /// itens, atributos, missões e posição. Sem ele, é um convidado novo (não é salvo) com a arma inicial.
    /// </summary>
    public ServerPlayer AddPlayer(string name, double now, Action<int>? onIdAssigned = null, CharacterRecord? character = null)
    {
        var cleanName = string.IsNullOrWhiteSpace(name) ? "Aventureiro" : name.Trim();
        if (character != null) cleanName = character.Name;
        var id = _nextEntityId++;
        var (spawnX, spawnZ) = SpawnPointFor(id);
        if (character != null && IsInsideWorld(character.X, character.Z)) (spawnX, spawnZ) = (character.X, character.Z);
        var player = new ServerPlayer(character?.Class ?? GuestClass)
        {
            Id = id,
            Name = cleanName.Length > 20 ? cleanName[..20] : cleanName,
            CharacterId = character?.Id ?? 0,
            // Convidados (testes e bots) variam as cores pelo id, para a multidão não sair toda igual.
            Hair = character != null ? Appearance.Hair(character.Hair) : id % Appearance.HairColors,
            Cloth = character != null ? Appearance.Cloth(character.Cloth) : id / 2 % Appearance.ClothColors,
            Head = character != null ? Appearance.Head(character.Head) : id / 3 % Appearance.Heads,
            Skin = character != null ? Appearance.Skin(character.Skin) : id / 5 % Appearance.SkinTones,
            HideHelmet = character?.HideHelmet ?? id % 4 == 0,
            Movement = new MovementValidator(spawnX, spawnZ, now),
        };
        if (character != null)
        {
            Restore(player, character);
            player.AccountId = character.AccountId;
            player.IsGm = character.IsGm;
            player.MutedUntil = character.MutedUntil;
        }
        else GiveGuestKit(player);
        player.JoinedAt = now;
        player.Session.StartLevel = player.Progress.Level;
        player.NextRouteSampleAt = now + RouteSampleSeconds;
        onIdAssigned?.Invoke(player.Id);

        _outbox.Send(player.Id, new Welcome
        {
            PlayerId = player.Id, ServerTime = now, TickRate = TickRate, SpawnX = spawnX, SpawnZ = spawnZ,
        });
        _players.Add(player.Id, player);
        NotifyWatchers(player, LT("social.friend_online", player.Name));
        return player;
    }

    /// <summary>Verdadeiro se o personagem do banco já está no mundo (impede entrar duas vezes ao mesmo tempo).</summary>
    public bool IsCharacterOnline(int characterId) => characterId != 0 && _players.Values.Any(p => p.CharacterId == characterId);

    /// <summary>
    /// Foto do estado para salvar, e os registros de itens desde o último salvamento (que saem da fila do jogador).
    /// Morto salva com a vida cheia e na vila, para não voltar morto.
    /// </summary>
    public (CharacterSnapshot Snapshot, List<ItemLogEntry> Logs) CaptureForSave(ServerPlayer player)
    {
        // Na dungeon (ou morto) salva fora: na volta aparece no portal ou na vila.
        var (x, z) = player.ArenaId != 0 ? SpawnPointFor(player.Id)
            : player.DungeonId != 0 ? (NpcCatalog.Find(NpcCatalog.DungeonPortal)!.X, NpcCatalog.Find(NpcCatalog.DungeonPortal)!.Z + 2.5f)
            : player.IsDead ? SpawnPointFor(player.Id) : (player.X, player.Z);
        var items = new List<ItemRecord>();
        foreach (var (slot, item) in player.Bag.Items()) items.Add(ToRecord(item, ItemContainerKind.Bag, slot));
        foreach (var (slot, item) in player.Warehouse.Items()) items.Add(ToRecord(item, ItemContainerKind.Warehouse, slot));
        foreach (var (slot, item) in player.Equipment.Worn) items.Add(ToRecord(item, ItemContainerKind.Equipment, (int)slot));
        // As diárias salvam o dia da entrega (no dia seguinte voltam a ficar disponíveis).
        var quests = player.Quests.Completed.Where(id => !QuestCatalog.Get(id).Daily).Select(id => new QuestRecord(id, true, Array.Empty<int>()))
            .Concat(player.Quests.DailyDone.Select(d => new QuestRecord(d.Key, true, Array.Empty<int>(), d.Value)))
            .Concat(player.Quests.Active.Select(q => new QuestRecord(q.QuestId, false, q.Counts.ToArray())))
            .ToList();
        var snapshot = new CharacterSnapshot(
            player.CharacterId, player.Progress.Level, player.Progress.Xp, player.Alz, x, z,
            player.IsDead ? player.Hp.Max : player.Hp.Current, player.IsDead ? player.Mp.Max : player.Mp.Current, player.Sp.Current,
            items, player.Attributes, player.Awakening.Gauge, quests)
        {
            Relations = player.Relations.ToList(),
            LearnedSkills = player.ClassSkills.Select(s => s.Skill.Id).Where(player.LearnedSkills.Contains).ToList(),
            Hotbar = SkillTraining.ToIds(player.Hotbar, player.ClassSkills),
            HideHelmet = player.HideHelmet,
            Reports = player.PendingReports.ToList(),
            Metrics = player.PendingMetrics.ToList(),
            Market = player.PendingMarket.ToList(),
        };
        player.PendingReports.Clear();
        player.PendingMetrics.Clear();
        player.PendingMarket.Clear();
        var logs = player.PendingLogs.ToList();
        player.PendingLogs.Clear();
        return (snapshot, logs);
    }

    public void RemovePlayer(int playerId)
    {
        if (!_players.TryGetValue(playerId, out var leaving)) return;
        _players.Remove(playerId);
        if (leaving.PartyId != 0) LeaveParty(leaving, LT("party.left_game", leaving.Name));
        NotifyWatchers(leaving, LT("social.friend_offline", leaving.Name));
        if (_arenaQueue.Remove(playerId)) MarkQueue();
        foreach (var monster in _monsters.Values)
        {
            if (monster.AggroPlayerId == playerId) monster.AggroPlayerId = 0;
            monster.DamageBy.Remove(playerId);
            monster.Threat.Remove(playerId);
        }
        foreach (var other in _players.Values)
        {
            if (other.KnownEntities.Remove(playerId)) _outbox.Send(other.Id, new EntityDespawned { Id = playerId });
        }
    }

    /// <summary>Trata uma mensagem do cliente.</summary>
    public void Handle(int playerId, INetMessage message, double now)
    {
        if (!_players.TryGetValue(playerId, out var player)) return;

        switch (message)
        {
            case MoveInput move:
                HandleMove(player, move, now);
                break;
            case Ping ping:
                _outbox.Send(playerId, new Pong { ClientTime = ping.ClientTime, ServerTime = now });
                break;
            case UseSkill use:
                player.Resting = false;
                HandleUseSkill(player, use, now);
                break;
            case StartCombo start:
                player.Resting = false;
                HandleStartCombo(player, start, now);
                break;
            case Finisher finisher:
                player.Resting = false;
                HandleFinisher(player, finisher, now);
                break;
            case SetResting rest:
                HandleRest(player, rest.Resting);
                break;
            case LootRollChoose choose:
                HandleLootRoll(player, choose, now);
                break;
            case PickupLoot pickup:
                HandlePickup(player, pickup, now);
                break;
            // Durante a troca a bolsa não se mexe (só usar poção): a oferta aponta para posições da bolsa.
            case ItemCommand command when player.TradeId != 0 && command.Action != ItemActionKind.Use:
            case UpgradeItem or ApplyOption when player.TradeId != 0:
                SystemChat(player, LT("trade.finish_first_bag"));
                break;
            case ItemCommand command:
                HandleItemCommand(player, command, now);
                break;
            case UpgradeItem upgrade:
                HandleUpgrade(player, upgrade);
                break;
            case ApplyOption option:
                HandleApplyOption(player, option);
                break;
            case ShopCommand shop:
                HandleShop(player, shop);
                break;
            case MarketCommand market:
                HandleMarket(player, market);
                break;
            case TradeCommand trade:
                HandleTrade(player, trade, now);
                break;
            case AllocateAttributes allocate:
                HandleAllocate(player, allocate);
                break;
            case LearnSkill learn:
                HandleLearnSkill(player, learn);
                break;
            case SetHotbar hotbar:
                HandleSetHotbar(player, hotbar);
                break;
            case AppearanceCommand appearance:
                // Quem já o vê recebe o EntityLook no próximo envio (a aparência mudou); ele mesmo também.
                player.HideHelmet = appearance.HideHelmet;
                _outbox.Send(player.Id, LookMessage(player));
                break;
            case TalkToNpc talk:
                HandleTalk(player, talk);
                break;
            case QuestCommand quest:
                HandleQuest(player, quest);
                break;
            case ActivateAwakening:
                HandleAwakening(player, now);
                break;
            case PartyCommand party:
                HandleParty(player, party, now);
                break;
            case ChatSend chat:
                HandleChat(player, chat, now);
                break;
            case SocialCommand social:
                HandleRelation(player, social, now);
                break;
            case DungeonCommand dungeon:
                HandleDungeon(player, dungeon, now);
                break;
            case DuelCommand duel:
                HandleDuel(player, duel, now);
                break;
            case ArenaCommand arena:
                HandleArena(player, arena, now);
                break;
            case GmCommand gm:
                HandleGm(player, gm, now);
                break;
            case ClientReport report:
                HandleClientReport(player, report);
                break;
        }
    }

    /// <summary>Um passo da simulação.</summary>
    public void Tick(double now)
    {
        var dt = double.IsNaN(_lastTickAt) ? 1.0 / TickRate : Math.Clamp(now - _lastTickAt, 0, 0.25);
        _lastTickAt = now;
        _tick++;

        foreach (var player in _players.Values) TickPlayer(player, now, (float)dt);
        foreach (var monster in _monsters.Values) TickMonster(monster, now, (float)dt);
        TickDungeons(now);
        TickDuels(now);
        TickArenas(now, (float)dt);
        ExpireLoot(now);
        TickLootRolls(now);
        TickTrades();
        TickDailyReset();

        foreach (var player in _players.Values) SendView(player, now);
    }

    /// <summary>Relógio de verdade (as diárias e o horário do chefe de mundo); os testes trocam.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>Classe dos convidados (sem personagem salvo). Lâmina, ou a da opção --guest-class nos testes.</summary>
    public CharacterClass GuestClass { get; set; } = CharacterClass.Blade;

    private int _questDay = DailyRules.Today();

    /// <summary>Meia-noite em Brasília: as diárias voltam para todo mundo que está online, com um aviso.</summary>
    private void TickDailyReset()
    {
        if (_tick % TickRate != 0) return; // uma conferência por segundo basta
        var today = DailyRules.Day(Clock());
        if (today == _questDay) return;
        _questDay = today;
        foreach (var player in _players.Values)
        {
            player.QuestsDirty = true;
            Notify(player, LT("quest.daily_reset"), announcement: true);
        }
    }

    private void HandleMove(ServerPlayer player, MoveInput move, double now)
    {
        if (player.IsDead) return;
        if (move.Sequence <= player.LastMoveSequence && player.LastMoveSequence != 0) return;
        player.LastMoveSequence = move.Sequence;

        if (move.Dodging && now >= player.NextDodgeAt)
        {
            player.DodgeUntil = now + PlayerRules.DodgeSeconds;
            player.NextDodgeAt = now + PlayerRules.DodgeCooldownSeconds;
        }

        if (player.Movement.TryMove(move.X, move.Z, now))
        {
            player.Yaw = move.Yaw;
            // Andou: levanta da fogueira.
            if (player.Resting && !InRange(player.X, player.Z, player.RestX, player.RestZ, 0.3f)) player.Resting = false;
            if ((player.DungeonId != 0 && ClampToDungeon(player, now)) || (player.ArenaId != 0 && ClampToArena(player, now)) ||
                (player.DungeonId == 0 && player.ArenaId == 0 && ClampToWorld(player, now)))
                _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
            return;
        }
        _outbox.Send(player.Id, new PositionCorrection { X = player.X, Z = player.Z });
    }

    /// <summary>Mundo aberto: passou da borda do mapa, volta para dentro (o cliente também segura, isto é a garantia).</summary>
    private static bool ClampToWorld(ServerPlayer player, double now)
    {
        if (!WorldBounds.InOpenWorld(player.Z) || WorldBounds.Inside(player.X, player.Z)) return false;
        var (x, z) = WorldBounds.Clamp(player.X, player.Z);
        player.Movement.Teleport(x, z, now);
        return true;
    }

    /// <summary>Envia o que o jogador enxerga: entidades que apareceram/sumiram, itens e o snapshot do tick.</summary>
    private void SendView(ServerPlayer player, double now)
    {
        var snapshot = new Snapshot { Tick = _tick, ServerTime = now };
        var visible = new HashSet<int>();

        foreach (var other in _players.Values)
        {
            if (!InRange(player.X, player.Z, other.X, other.Z, InterestRadius)) continue;
            visible.Add(other.Id);
            var look = other.Look;
            if (player.KnownEntities.Add(other.Id))
            {
                _outbox.Send(player.Id, SpawnMessage(other));
                player.KnownLooks[other.Id] = look;
            }
            else if (!player.KnownLooks.TryGetValue(other.Id, out var known) || known != look)
            {
                // Trocou de arma ou de armadura, ou melhorou: quem já o via atualiza o modelo, o brilho e o metal.
                player.KnownLooks[other.Id] = look;
                _outbox.Send(player.Id, LookMessage(other));
            }
            snapshot.Entities.Add(new EntityState
            {
                Id = other.Id, X = other.X, Z = other.Z, Yaw = other.Yaw, Hp = other.Hp.Current,
                Flags = other.IsDead ? EntityState.FlagDead : other.Resting ? EntityState.FlagResting : (byte)0,
            });
        }

        foreach (var monster in _monsters.Values)
        {
            if (!InRange(player.X, player.Z, monster.X, monster.Z, InterestRadius)) continue;
            visible.Add(monster.Id);
            if (player.KnownEntities.Add(monster.Id)) _outbox.Send(player.Id, SpawnMessage(monster));
            byte flags = 0;
            if (monster.IsDead) flags |= EntityState.FlagDead;
            if (monster.Brain.IsWindingUp) flags |= EntityState.FlagWindUp;
            if (monster.PhaseTwo || monster.Brain.Enraged) flags |= EntityState.FlagEnraged;
            snapshot.Entities.Add(new EntityState
            {
                Id = monster.Id, X = monster.X, Z = monster.Z, Yaw = monster.Yaw, Hp = monster.Hp.Current, Flags = flags,
            });
        }

        foreach (var id in player.KnownEntities.Where(id => !visible.Contains(id)).ToList())
        {
            player.KnownEntities.Remove(id);
            player.KnownLooks.Remove(id);
            _outbox.Send(player.Id, new EntityDespawned { Id = id });
        }

        foreach (var loot in _loot.Values)
        {
            var near = loot.VisibleTo(player.Id) && InRange(player.X, player.Z, loot.X, loot.Z, InterestRadius);
            if (near && player.KnownLoot.Add(loot.Id)) _outbox.Send(player.Id, LootMessage(loot));
            else if (!near && player.KnownLoot.Remove(loot.Id)) _outbox.Send(player.Id, new LootRemoved { LootId = loot.Id });
        }

        _outbox.Send(player.Id, snapshot);

        if (player.StatsDirty)
        {
            player.StatsDirty = false;
            _outbox.Send(player.Id, StatsMessage(player));
        }
        if (player.InventoryDirty)
        {
            player.InventoryDirty = false;
            _outbox.Send(player.Id, InventoryMessage(player));
        }
        if (player.SheetDirty)
        {
            player.SheetDirty = false;
            _outbox.Send(player.Id, SheetMessage(player));
        }
        if (player.QuestsDirty)
        {
            player.QuestsDirty = false;
            _outbox.Send(player.Id, QuestMessage(player));
        }
        if (player.ComboDirty)
        {
            player.ComboDirty = false;
            _outbox.Send(player.Id, ComboMessage(player));
        }
        SendPartyState(player);
        SendSocialLists(player);
        SendTradeState(player);
        SendPvpState(player, now);
    }

    /// <summary>Manda a mensagem a todos os jogadores perto do ponto.</summary>
    private void SendNearby(float x, float z, INetMessage message)
    {
        foreach (var player in _players.Values)
        {
            if (InRange(player.X, player.Z, x, z, InterestRadius)) _outbox.Send(player.Id, message);
        }
    }

    /// <summary>Sincroniza uma ação visual só com jogadores próximos na mesma instância.</summary>
    private void BroadcastActorAnimation(ServerPlayer actor, ActorAnimationKind action)
    {
        var message = new ActorAnimation { EntityId = actor.Id, Action = action };
        foreach (var observer in _players.Values)
        {
            if (observer.DungeonId == actor.DungeonId && observer.ArenaId == actor.ArenaId &&
                InRange(observer.X, observer.Z, actor.X, actor.Z, InterestRadius))
                _outbox.Send(observer.Id, message);
        }
    }

    private void Notify(ServerPlayer player, LocalText text, bool announcement = false) =>
        _outbox.Send(player.Id, new Notice { Text = text, Announcement = announcement });

    /// <summary>Atalho para um texto traduzido no cliente (chave do pt-BR.txt e argumentos).</summary>
    private static LocalText LT(string key, params object?[] args) => new(key, args);

    private ServerMonster CreateMonster(SpawnDefinition spawn, float x, float z, int instanceId = 0)
    {
        var monster = new ServerMonster
        {
            Id = _nextEntityId++,
            InstanceId = instanceId,
            Spawn = spawn,
            Brain = new MonsterBrain(spawn.Data, new Vector2(x, z), _random),
            Hp = new ResourcePool(spawn.Data.MaxHp),
            HomeX = x,
            HomeZ = z,
            X = x,
            Z = z,
            BaseAttackCooldown = spawn.Data.AttackCooldownSeconds,
        };
        monster.Brain.Revive(0);
        RollElite(monster);
        if (spawn.Scheduled)
        {
            // Chefe de mundo: começa dormindo e só desperta no próximo horário (ou pelo /gm chefe).
            monster.Brain.Kill();
        }
        _monsters.Add(monster.Id, monster);
        return monster;
    }

    private void Restore(ServerPlayer player, CharacterRecord character)
    {
        player.Progress = new CharacterProgress(Math.Clamp(character.Level, 1, LevelCurve.MaxLevel), Math.Max(0, character.Xp));
        // Atributos estragados (ou de antes dos atributos) voltam para os iniciais da classe.
        var attributes = character.Attributes;
        var starting = Attributes.StartingFor(player.Class);
        if (attributes.Strength < starting.Strength || attributes.Dexterity < starting.Dexterity ||
            attributes.Intelligence < starting.Intelligence || Attributes.FreePoints(player.Progress.Level, player.Class, attributes) < 0)
            attributes = starting;
        player.Attributes = attributes;
        player.Alz = Math.Max(0, character.Alz);
        player.Awakening.Restore(character.AwakeningGauge);

        // Itens: cada um volta para o lugar salvo; se o lugar não servir mais, vai para a primeira posição livre da bolsa.
        var misplaced = new List<ItemInstance>();
        var overflow = new List<(string ItemId, int Quantity)>();
        foreach (var record in character.Items)
        {
            var definition = ItemCatalog.Find(record.ItemId);
            if (definition == null || record.Quantity <= 0) continue;
            var item = new ItemInstance
            {
                Uid = record.Uid, ItemId = record.ItemId, Quantity = Math.Min(record.Quantity, definition.MaxStack),
                Upgrade = Math.Clamp(record.Upgrade, 0, UpgradeRules.MaxLevel), PityFails = Math.Max(0, record.PityFails),
                PityLevel = record.PityLevel, RegainGuaranteed = record.RegainGuaranteed, OptionId = record.OptionId ?? "",
            };
            var placed = record.Container switch
            {
                ItemContainerKind.Equipment when definition.Kind == ItemKind.Equipment && (int)definition.Slot == record.Slot
                    && player.Equipment[definition.Slot] == null => Wear(player, definition.Slot, item),
                ItemContainerKind.Warehouse => player.Warehouse.TryPut(record.Slot, item),
                ItemContainerKind.Bag => player.Bag.TryPut(record.Slot, item),
                _ => false,
            };
            if (!placed) misplaced.Add(item);
            if (record.Quantity > item.Quantity) overflow.Add((item.ItemId, record.Quantity - item.Quantity));
        }
        foreach (var item in misplaced)
        {
            if (!player.Bag.TryPlace(item, out _)) player.Warehouse.TryPlace(item, out _);
        }
        // Pilhas maiores que o limite (dados antigos) viram várias pilhas.
        foreach (var (itemId, quantity) in overflow)
        {
            if (!player.Bag.TryAdd(itemId, quantity, _uids.Next)) player.Warehouse.TryAdd(itemId, quantity, _uids.Next);
        }

        player.Quests.Restore(
            character.Quests.Where(q => q.Completed && q.CompletedDay == 0).Select(q => q.QuestId),
            character.Quests.Where(q => !q.Completed).Select(q => new QuestProgress(q.QuestId, q.Counts.ToArray())),
            character.Quests.Where(q => q.Completed && q.CompletedDay != 0).Select(q => new KeyValuePair<string, int>(q.QuestId, q.CompletedDay)));
        // De antes dos itens de missão: o material já juntado para uma coleta ativa vira item de missão.
        QuestItems.ConvertMaterials(player.Quests, player.Bag, _uids.Next);

        // Relações repetidas ou consigo mesmo (dados estragados) ficam de fora.
        foreach (var relation in character.Relations)
        {
            if (relation.OtherId == 0 || relation.OtherId == character.Id || player.HasRelation(relation.Kind, relation.OtherId)) continue;
            player.Relations.Add(relation);
        }

        // Skills: as aprendidas no mestre; personagem de antes do mestre fica com o que o nível e as missões liberavam.
        var learned = character.LearnedSkills.Count > 0
            ? character.LearnedSkills
            : SkillTraining.FromOldRules(player.Class, player.Progress.Level, player.QuestCompleted);
        foreach (var skillId in learned.Concat(SkillTraining.Starting(player.Class)))
            if (player.ClassSkills.Any(s => s.Skill.Id == skillId)) player.LearnedSkills.Add(skillId);
        player.Hotbar = character.Hotbar.Count > 0
            ? SkillTraining.Sanitize(SkillTraining.FromIds(character.Hotbar, player.ClassSkills), player.ClassSkills, player.LearnedSkills)
            : SkillTraining.DefaultHotbar(player.ClassSkills, player.LearnedSkills);

        player.RecalculateStats();
        player.Hp.Add(Math.Clamp(character.Hp, 1, player.Hp.Max) - player.Hp.Current);
        player.Mp.Add(Math.Clamp(character.Mp, 0, player.Mp.Max) - player.Mp.Current);
        player.Sp.Add(Math.Clamp(character.Sp, 0, player.Sp.Max) - player.Sp.Current);
    }

    private static bool Wear(ServerPlayer player, EquipSlot slot, ItemInstance item)
    {
        player.Equipment.Put(slot, item);
        return true;
    }

    /// <summary>Convidado começa com a arma da classe vestida e algumas poções.</summary>
    private void GiveGuestKit(ServerPlayer player)
    {
        player.Equipment.Put(EquipSlot.Weapon, new ItemInstance { Uid = _uids.Next(), ItemId = ClassInfo.StarterWeapon(player.Class) });
        player.Bag.TryAdd("pocao_hp_p", 5, _uids.Next);
        foreach (var skill in player.ClassSkills) player.LearnedSkills.Add(skill.Skill.Id);
        player.Hotbar = SkillTraining.DefaultHotbar(player.ClassSkills, player.LearnedSkills);
        player.Bag.TryAdd(Game.Shared.Dungeon.DungeonCatalog.CryptKey, 3, _uids.Next); // bots e testes entram nas dungeons
        player.Bag.TryAdd(Game.Shared.Dungeon.DungeonCatalog.SanctuaryKey, 3, _uids.Next);
        player.RecalculateStats();
        player.Hp.Add(player.Hp.Max);
        player.Mp.Add(player.Mp.Max);
    }

    private static ItemRecord ToRecord(ItemInstance item, ItemContainerKind container, int slot) => new(
        item.Uid, container, slot, item.ItemId, item.Quantity, item.Upgrade, item.PityFails, item.PityLevel,
        item.RegainGuaranteed, item.OptionId);

    /// <summary>Posição salva dentro do chão do mapa (protege contra dados estragados).</summary>
    private static bool IsInsideWorld(float x, float z) =>
        !float.IsNaN(x) && !float.IsNaN(z) && x >= -130 && x <= 130 && z >= -70 && z <= 230;

    /// <summary>
    /// Ponto de nascimento na vila: em volta da fogueira da praça (fora do fogo, dentro do raio de descanso), espalhado
    /// para jogadores não nascerem um dentro do outro.
    /// </summary>
    public static (float X, float Z) SpawnPointFor(int playerId)
    {
        var angle = playerId * 2.39996f; // ângulo áureo: pontos bem distribuídos
        var radius = RestRules.FireRadius + 1f + playerId % 3 * 0.75f;
        return (WorldContent.PlayerSpawnX + MathF.Cos(angle) * radius, WorldContent.PlayerSpawnZ + MathF.Sin(angle) * radius);
    }

    private static bool InRange(float ax, float az, float bx, float bz, float radius)
    {
        var dx = ax - bx;
        var dz = az - bz;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static float Distance(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    private static EntitySpawned SpawnMessage(ServerPlayer player) => new()
    {
        // Jogadores levam a classe no TypeId ("blade", "arcanist", "ranger" ou "guardian"): o cliente escolhe o modelo por ela.
        Id = player.Id, Kind = EntityKind.Player, TypeId = ClassInfo.TypeId(player.Class),
        Name = player.Name, Level = player.Progress.Level, MaxHp = player.Hp.Max, X = player.X, Z = player.Z,
        WeaponId = player.Look.WeaponId, WeaponUpgrade = (byte)player.Look.Upgrade, ArmorGrade = (byte)player.Look.ArmorGrade,
        GlovesGrade = (byte)player.Look.GlovesGrade, BootsGrade = (byte)player.Look.BootsGrade,
        Hair = (byte)player.Hair, Cloth = (byte)player.Cloth, Head = (byte)player.Head, Skin = (byte)player.Skin, HideHelmet = player.HideHelmet,
    };

    private static EntityLook LookMessage(ServerPlayer player)
    {
        var look = player.Look;
        return new EntityLook
        {
            Id = player.Id, WeaponId = look.WeaponId, WeaponUpgrade = (byte)look.Upgrade, ArmorGrade = (byte)look.ArmorGrade,
            GlovesGrade = (byte)look.GlovesGrade, BootsGrade = (byte)look.BootsGrade, HideHelmet = look.HideHelmet,
        };
    }

    private static EntitySpawned SpawnMessage(ServerMonster monster) => new()
    {
        Id = monster.Id, Kind = EntityKind.Monster, TypeId = monster.Data.Id, Name = monster.Data.Name,
        Level = monster.Data.Level, MaxHp = monster.Hp.Max, X = monster.X, Z = monster.Z, Elite = monster.IsElite,
    };

    private static LootSpawned LootMessage(ServerLoot loot) => new()
    {
        LootId = loot.Id, ItemId = loot.Drop.ItemId, Rarity = (byte)loot.Drop.Rarity,
        Quantity = loot.Drop.Quantity, X = loot.X, Z = loot.Z,
    };

    private static PlayerStats StatsMessage(ServerPlayer player) => new()
    {
        Level = player.Progress.Level, Xp = player.Progress.Xp, XpToNext = player.Progress.XpToNext,
        Hp = player.Hp.Current, MaxHp = player.Hp.Max, Mp = player.Mp.Current, MaxMp = player.Mp.Max,
        Sp = player.Sp.Current, MaxSp = player.Sp.Max, Alz = player.Alz,
    };

    private static Inventory InventoryMessage(ServerPlayer player)
    {
        var message = new Inventory();
        void Add(ItemInstance item, ItemContainerKind container, int slot)
        {
            var definition = ItemCatalog.Get(item.ItemId);
            message.Items.Add(new InventoryStack
            {
                ItemId = item.ItemId, Rarity = (byte)definition.Rarity, Quantity = item.Quantity,
                Container = container, Slot = (ushort)slot, Uid = item.Uid, Upgrade = (byte)item.Upgrade,
                PityFails = (byte)Math.Min(item.PityFails, 255), PityLevel = (byte)item.PityLevel,
                RegainGuaranteed = item.RegainGuaranteed, OptionId = item.OptionId,
            });
        }
        foreach (var (slot, item) in player.Bag.Items()) Add(item, ItemContainerKind.Bag, slot);
        foreach (var (slot, item) in player.Equipment.Worn) Add(item, ItemContainerKind.Equipment, (int)slot);
        foreach (var (slot, item) in player.Warehouse.Items()) Add(item, ItemContainerKind.Warehouse, slot);
        return message;
    }

    private static CharacterSheet SheetMessage(ServerPlayer player)
    {
        var sheet = new CharacterSheet
        {
            Class = (byte)player.Class, Strength = (ushort)player.Attributes.Strength, Dexterity = (ushort)player.Attributes.Dexterity,
            Intelligence = (ushort)player.Attributes.Intelligence, FreePoints = (ushort)Math.Max(0, player.FreeAttributePoints),
            AttackPower = player.Stats.AttackPower, MagicPower = player.Stats.MagicPower, Defense = player.Stats.Defense,
            CritPercent = player.Stats.CritPercent, ComboUnlocked = player.ComboUnlocked,
            AwakeningGauge = (byte)player.Awakening.Gauge,
            AwakeningUntil = double.IsInfinity(player.Awakening.ActiveUntil) ? 0 : player.Awakening.ActiveUntil,
        };
        for (var i = 0; i < player.ClassSkills.Count; i++) sheet.SkillUnlocked.Add(player.SkillUnlocked(i));
        sheet.Hotbar.AddRange(player.Hotbar);
        return sheet;
    }

    private static QuestState QuestMessage(ServerPlayer player)
    {
        var state = new QuestState();
        state.Completed.AddRange(player.Quests.Completed);
        foreach (var progress in player.Quests.Active)
        {
            // Coletar mostra o que está na bolsa agora (o diário não guarda contador para isso).
            var counts = new List<int>();
            for (var i = 0; i < progress.Counts.Length; i++) counts.Add(QuestLog.Done(progress, i, player.Bag));
            state.Active.Add(new QuestEntry { QuestId = progress.QuestId, Counts = counts });
        }
        return state;
    }

    private static ComboStateMessage ComboMessage(ServerPlayer player)
    {
        var combo = player.Combo;
        return new ComboStateMessage
        {
            Active = combo.State == ComboPhase.Active,
            CycleStartServerTime = combo.CycleStart,
            BarDurationSeconds = (float)combo.BarDurationSeconds,
            WindowCenter = (float)combo.Config.WindowCenter,
            WindowWidth = (float)combo.WindowWidth,
            ExcellentZoneRatio = (float)combo.Config.ExcellentZoneRatio,
            HitCount = (ushort)Math.Min(combo.HitCount, ushort.MaxValue),
            ChainBonus = (float)combo.ChainBonus,
            CanFinish = combo.CanFinish,
            LastEndReason = (byte)combo.LastEndReason,
        };
    }
}
