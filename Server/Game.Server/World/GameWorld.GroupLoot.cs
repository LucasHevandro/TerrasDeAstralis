using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Net;
using Game.Shared.Text;

namespace Game.Server.World;

/// <summary>Item raro do grupo esperando as respostas (Preciso, Quero, Passar) antes de cair no chão.</summary>
public sealed class ServerLootRoll
{
    public int Id { get; init; }
    public ItemDrop Drop { get; init; }
    public float X { get; init; }
    public float Z { get; init; }
    public int InstanceId { get; init; }
    public int PartyId { get; init; }
    public required List<int> Members { get; init; }
    public Dictionary<int, LootChoice> Answers { get; } = new();
    public double ExpiresAt { get; init; }
}

/// <summary>
/// Loot em grupo (<see cref="GroupLoot"/>): item comum vai para um membro por vez, já no nome dele (só ele vê e pega);
/// raro ou melhor vai a sorteio e só cai no chão, no nome de quem ganhou, depois das respostas ou do prazo.
/// </summary>
public sealed partial class GameWorld
{
    private readonly Dictionary<int, ServerLootRoll> _lootRolls = new();
    private readonly Dictionary<int, int> _lootTurn = new();
    private int _nextRollId = 1;

    public IReadOnlyCollection<ServerLootRoll> LootRolls => _lootRolls.Values;

    /// <summary>Um item do abate em grupo: sorteio se for raro, senão a vez do próximo do rodízio.</summary>
    private void DistributeGroupLoot(IReadOnlyList<ServerPlayer> group, int partyId, ItemDrop drop, float x, float z, int instanceId,
        double now)
    {
        var item = ItemCatalog.Get(drop.ItemId);
        if (!GroupLoot.NeedsRoll(item))
        {
            var owner = GiveByTurn(group, partyId, drop, x, z, instanceId, now);
            SystemChat(owner, LT("loot.turn", Loc.Ref("item." + drop.ItemId)));
            return;
        }
        var roll = new ServerLootRoll
        {
            Id = _nextRollId++, Drop = drop, X = x, Z = z, InstanceId = instanceId, PartyId = partyId,
            Members = group.Select(m => m.Id).ToList(), ExpiresAt = now + GroupLoot.RollSeconds,
        };
        _lootRolls.Add(roll.Id, roll);
        foreach (var member in group)
            _outbox.Send(member.Id, new LootRollStart
            {
                RollId = roll.Id, ItemId = drop.ItemId, Quantity = drop.Quantity, Seconds = GroupLoot.RollSeconds,
                CanNeed = GroupLoot.CanNeed(item, member.Class),
            });
    }

    /// <summary>Põe o item no nome do próximo da vez (o rodízio é por grupo e continua de um abate para o outro).</summary>
    private ServerPlayer GiveByTurn(IReadOnlyList<ServerPlayer> group, int partyId, ItemDrop drop, float x, float z, int instanceId,
        double now)
    {
        var turn = _lootTurn.GetValueOrDefault(partyId);
        var owner = group[turn % group.Count];
        _lootTurn[partyId] = turn + 1;
        SpawnPersonalLoot(owner, drop, x, z, instanceId, now);
        return owner;
    }

    private void SpawnPersonalLoot(ServerPlayer owner, ItemDrop drop, float x, float z, int instanceId, double now)
    {
        var id = _nextLootId++;
        _loot.Add(id, new ServerLoot
        {
            Id = id, Drop = drop, OwnerId = owner.Id, Personal = true, InstanceId = instanceId, X = x, Z = z,
            OwnerUntil = now + ServerLoot.LifetimeSeconds, ExpiresAt = now + ServerLoot.LifetimeSeconds,
        });
    }

    private void HandleLootRoll(ServerPlayer player, LootRollChoose message, double now)
    {
        if (!_lootRolls.TryGetValue(message.RollId, out var roll) || !roll.Members.Contains(player.Id) || roll.Answers.ContainsKey(player.Id))
            return;
        var choice = message.Choice;
        if (!Enum.IsDefined(choice)) choice = LootChoice.Pass;
        // "Preciso" de quem não pode usar o item vale como "Quero".
        if (choice == LootChoice.Need && !GroupLoot.CanNeed(ItemCatalog.Get(roll.Drop.ItemId), player.Class)) choice = LootChoice.Greed;
        roll.Answers[player.Id] = choice;
        if (roll.Members.All(id => roll.Answers.ContainsKey(id) || !_players.ContainsKey(id))) ResolveRoll(roll, now);
    }

    /// <summary>Prazo acabou: quem não respondeu passou.</summary>
    private void TickLootRolls(double now)
    {
        foreach (var roll in _lootRolls.Values.Where(r => now >= r.ExpiresAt).ToList()) ResolveRoll(roll, now);
    }

    private void ResolveRoll(ServerLootRoll roll, double now)
    {
        _lootRolls.Remove(roll.Id);
        var online = roll.Members.Select(id => _players.GetValueOrDefault(id)).OfType<ServerPlayer>().ToList();
        if (online.Count == 0) return;
        var answers = online.ToDictionary(p => p.Id, p => roll.Answers.GetValueOrDefault(p.Id, LootChoice.Pass));
        var result = GroupLoot.Resolve(answers, _random);
        var name = Loc.Ref("item." + roll.Drop.ItemId);
        if (result != null && _players.TryGetValue(result.WinnerId, out var winner))
        {
            SpawnPersonalLoot(winner, roll.Drop, roll.X, roll.Z, roll.InstanceId, now);
            var choice = Loc.Ref(result.Choice == LootChoice.Need ? "loot.need" : "loot.greed");
            foreach (var member in online) SystemChat(member, LT("loot.roll_won", winner.Name, name, choice, result.Roll));
            return;
        }
        var owner = GiveByTurn(online, roll.PartyId, roll.Drop, roll.X, roll.Z, roll.InstanceId, now);
        foreach (var member in online) SystemChat(member, LT("loot.roll_all_passed", name, owner.Name));
    }
}
