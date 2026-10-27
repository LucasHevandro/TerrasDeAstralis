using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;

namespace Game.Server.World;

/// <summary>
/// Loja da mercadora (comprar consumíveis e equipamento básico, vender drops) e troca direta entre jogadores com dupla
/// confirmação. Tudo com registro de Alz e itens (item_log).
/// </summary>
public sealed partial class GameWorld
{
    private readonly Dictionary<int, TradeSession> _trades = new();
    private int _nextTradeId = 1;

    public TradeSession? FindTrade(int tradeId) => _trades.TryGetValue(tradeId, out var trade) ? trade : null;

    // ===== Loja =====

    private void HandleShop(ServerPlayer player, ShopCommand command)
    {
        if (player.TradeId != 0)
        {
            SystemChat(player, LT("trade.finish_first_shop"));
            return;
        }
        if (player.IsDead || !NearNpc(player, NpcRole.Merchant))
        {
            Notify(player, LT("shop.go_to_merchant"));
            return;
        }
        if (command.Action == ShopActionKind.Buy) Buy(player, command.ItemId, command.Quantity);
        else Sell(player, command.Slot, command.Quantity);
    }

    private void Buy(ServerPlayer player, string itemId, int quantity)
    {
        var entry = ShopCatalog.Find(itemId);
        if (entry == null) return;
        var definition = ItemCatalog.Get(entry.ItemId);
        quantity = Math.Clamp(quantity, 1, definition.MaxStack);
        var cost = entry.Price * quantity;
        if (player.Alz < cost)
        {
            Notify(player, LT("shop.no_alz", Loc.Ref("item." + definition.Id), quantity, cost));
            return;
        }
        if (!player.Bag.TryAdd(entry.ItemId, quantity, _uids.Next))
        {
            Notify(player, LT("bag.full_short"));
            return;
        }
        player.Alz -= cost;
        player.InventoryDirty = true;
        player.StatsDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        Log(player, "compra_loja", entry.ItemId, quantity, -cost, $"{quantity} x {entry.Price}");
        Notify(player, LT("shop.bought", Loc.Ref("item." + definition.Id), quantity, cost));
    }

    private void Sell(ServerPlayer player, int slot, int quantity)
    {
        var item = player.Bag[slot];
        if (item == null) return;
        var definition = ItemCatalog.Get(item.ItemId);
        if (definition.IsQuestItem)
        {
            Notify(player, LT("item.quest_bound"));
            return;
        }
        quantity = Math.Clamp(quantity <= 0 ? item.Quantity : quantity, 1, item.Quantity);
        var price = EconomyRules.SellPrice(definition, item.Upgrade) * quantity;
        if (quantity == item.Quantity) player.Bag.Take(slot);
        else item.Quantity -= quantity;
        player.Alz += price;
        player.InventoryDirty = true;
        player.StatsDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        Log(player, "venda_loja", item.ItemId, quantity, price, $"uid {item.Uid}{(item.Upgrade > 0 ? $" +{item.Upgrade}" : "")}");
        Notify(player, item.Upgrade > 0
            ? LT("shop.sold_upgraded", Loc.Ref("item." + definition.Id), item.Upgrade, quantity, price)
            : LT("shop.sold", Loc.Ref("item." + definition.Id), quantity, price));
    }

    // ===== Troca =====

    private void HandleTrade(ServerPlayer player, TradeCommand command, double now)
    {
        var trade = FindTrade(player.TradeId);
        switch (command.Action)
        {
            case TradeActionKind.Request:
                RequestTrade(player, command.TargetName, now);
                break;
            case TradeActionKind.Accept:
                AcceptTrade(player, command.TargetId, now);
                break;
            case TradeActionKind.Decline:
                if (player.PendingTradeInvite is { } invite && _players.TryGetValue(invite.FromId, out var from))
                    SystemChat(from, LT("trade.declined", player.Name));
                player.PendingTradeInvite = null;
                break;
            case TradeActionKind.Cancel:
                if (trade != null) CancelTrade(trade, LT("trade.cancelled_by", player.Name));
                break;
            case TradeActionKind.SetOffer when trade != null:
                var offer = new TradeOffer { Alz = command.Alz, Items = command.Items.ToList() };
                var problem = TradeExecutor.CheckOffer(offer, player.Bag, player.Alz);
                if (problem != null)
                {
                    SystemChat(player, problem);
                    return;
                }
                trade.SetOffer(player.Id, offer);
                MarkTrade(trade);
                break;
            case TradeActionKind.Lock when trade != null:
                var invalid = TradeExecutor.CheckOffer(trade.OfferOf(player.Id), player.Bag, player.Alz);
                if (invalid != null)
                {
                    SystemChat(player, invalid);
                    return;
                }
                trade.Lock(player.Id);
                MarkTrade(trade);
                break;
            case TradeActionKind.Confirm when trade != null:
                if (!trade.Confirm(player.Id))
                {
                    SystemChat(player, LT("trade.both_ready"));
                    return;
                }
                MarkTrade(trade);
                if (trade.BothConfirmed) CompleteTrade(trade);
                break;
        }
    }

    private void RequestTrade(ServerPlayer player, string name, double now)
    {
        var target = FindPlayerByName(name);
        LocalText? error =
            target == null ? LT("player.offline", name) :
            target == player ? LT("trade.self") :
            player.TradeId != 0 ? LT("trade.already_you") :
            target.TradeId != 0 ? LT("trade.already_other", target.Name) :
            player.IsDead || target.IsDead ? LT("trade.dead") :
            player.IsGuest != target.IsGuest ? LT("trade.guests_only") :
            target.HasBlocked(player) ? LT("trade.not_accepting", target.Name) :
            player.HasBlocked(target) ? LT("trade.unblock_to_trade", target.Name) :
            Distance(player.X, player.Z, target.X, target.Z) > TradeRules.Range ? LT("trade.come_closer", target.Name) :
            target.PendingTradeInvite is { } pending && pending.ExpiresAt > now && pending.FromId != player.Id
                ? LT("trade.request_pending", target.Name) :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        target!.PendingTradeInvite = (player.Id, now + TradeRules.InviteSeconds);
        _outbox.Send(target.Id, new TradeInvite { FromId = player.Id, FromName = player.Name, Seconds = (float)TradeRules.InviteSeconds });
        SystemChat(player, LT("trade.request_sent", target.Name));
    }

    private void AcceptTrade(ServerPlayer player, int fromId, double now)
    {
        var pending = player.PendingTradeInvite;
        player.PendingTradeInvite = null;
        if (pending is not { } invite || invite.FromId != fromId || invite.ExpiresAt < now || !_players.TryGetValue(fromId, out var from))
        {
            SystemChat(player, LT("trade.request_expired"));
            return;
        }
        LocalText? error =
            player.TradeId != 0 || from.TradeId != 0 ? LT("trade.someone_busy") :
            player.IsDead || from.IsDead ? LT("trade.dead") :
            Distance(player.X, player.Z, from.X, from.Z) > TradeRules.Range ? LT("trade.come_closer", from.Name) :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        var trade = new TradeSession(_nextTradeId++, from.Id, player.Id);
        _trades.Add(trade.Id, trade);
        from.TradeId = player.TradeId = trade.Id;
        MarkTrade(trade);
    }

    private void CompleteTrade(TradeSession trade)
    {
        var a = _players[trade.PlayerA];
        var b = _players[trade.PlayerB];
        if (Distance(a.X, a.Z, b.X, b.Z) > TradeRules.Range)
        {
            Unlock(trade, LT("trade.closer_to_finish"));
            return;
        }
        // Guarda o que sai antes de mexer (para os registros).
        var outA = Describe(a, trade.OfferA);
        var outB = Describe(b, trade.OfferB);
        var alzA = a.Alz;
        var alzB = b.Alz;
        var error = TradeExecutor.Execute(trade, a.Bag, ref alzA, b.Bag, ref alzB, _uids.Next);
        if (error != null)
        {
            Unlock(trade, LT("trade.failed", error));
            return;
        }
        a.Alz = alzA;
        b.Alz = alzB;

        foreach (var (player, partner, given, received, offer, partnerOffer) in new[]
                 {
                     (a, b, outA, outB, trade.OfferA, trade.OfferB),
                     (b, a, outB, outA, trade.OfferB, trade.OfferA),
                 })
        {
            player.InventoryDirty = true;
            player.StatsDirty = true;
            player.QuestsDirty |= player.Quests.Active.Any();
            var details = $"troca {trade.Id} com {partner.Name} (personagem {partner.CharacterId})";
            foreach (var (itemId, quantity, uid) in given) Log(player, "troca_saida", itemId, quantity, 0, $"{details}, uid {uid}");
            foreach (var (itemId, quantity, uid) in received) Log(player, "troca_entrada", itemId, quantity, 0, $"{details}, uid {uid}");
            var alzDelta = partnerOffer.Alz - offer.Alz;
            if (alzDelta != 0) Log(player, "troca_alz", null, 0, alzDelta, details);
            SystemChat(player, LT("trade.done", partner.Name));
        }
        CloseTrade(trade);
    }

    private static List<(string ItemId, int Quantity, long Uid)> Describe(ServerPlayer player, TradeOffer offer) =>
        offer.Items.Select(i => (player.Bag[i.Slot]?.ItemId ?? "?", i.Quantity, i.Uid)).ToList();

    /// <summary>A troca não pôde acontecer: destrava os dois (as ofertas continuam) e explica.</summary>
    private void Unlock(TradeSession trade, LocalText message)
    {
        trade.SetOffer(trade.PlayerA, trade.OfferA);
        MarkTrade(trade);
        foreach (var id in new[] { trade.PlayerA, trade.PlayerB })
            if (_players.TryGetValue(id, out var p)) SystemChat(p, message);
    }

    private void CancelTrade(TradeSession trade, LocalText message)
    {
        foreach (var id in new[] { trade.PlayerA, trade.PlayerB })
            if (_players.TryGetValue(id, out var p)) SystemChat(p, message);
        CloseTrade(trade);
    }

    private void CloseTrade(TradeSession trade)
    {
        _trades.Remove(trade.Id);
        foreach (var id in new[] { trade.PlayerA, trade.PlayerB })
        {
            if (!_players.TryGetValue(id, out var p)) continue;
            p.TradeId = 0;
            _outbox.Send(p.Id, new TradeState { Active = false });
        }
    }

    private void MarkTrade(TradeSession trade)
    {
        foreach (var id in new[] { trade.PlayerA, trade.PlayerB })
            if (_players.TryGetValue(id, out var p)) p.TradeDirty = true;
    }

    /// <summary>Cancela trocas de quem saiu, morreu ou se afastou.</summary>
    private void TickTrades()
    {
        foreach (var trade in _trades.Values.ToList())
        {
            var hasA = _players.TryGetValue(trade.PlayerA, out var a);
            var hasB = _players.TryGetValue(trade.PlayerB, out var b);
            if (!hasA || !hasB) CancelTrade(trade, LT("trade.cancelled_left"));
            else if (a!.IsDead || b!.IsDead) CancelTrade(trade, LT("trade.cancelled_died"));
            else if (Distance(a.X, a.Z, b.X, b.Z) > TradeRules.KeepRange) CancelTrade(trade, LT("trade.cancelled_far"));
        }
    }

    private void SendTradeState(ServerPlayer player)
    {
        if (!player.TradeDirty) return;
        player.TradeDirty = false;
        var trade = FindTrade(player.TradeId);
        if (trade == null) return;
        var partner = _players[trade.PartnerOf(player.Id)];
        _outbox.Send(player.Id, new TradeState
        {
            Active = true, PartnerId = partner.Id, PartnerName = partner.Name,
            MyItems = Infos(player, trade.OfferOf(player.Id)), MyAlz = trade.OfferOf(player.Id).Alz,
            TheirItems = Infos(partner, trade.OfferOf(partner.Id)), TheirAlz = trade.OfferOf(partner.Id).Alz,
            MyLocked = trade.LockedOf(player.Id), TheirLocked = trade.LockedOf(partner.Id),
            MyConfirmed = trade.ConfirmedOf(player.Id), TheirConfirmed = trade.ConfirmedOf(partner.Id),
        });
    }

    private static List<TradeItemInfo> Infos(ServerPlayer owner, TradeOffer offer) => offer.Items
        .Select(i => (Entry: i, Item: owner.Bag[i.Slot]))
        .Where(x => x.Item != null && x.Item.Uid == x.Entry.Uid)
        .Select(x => new TradeItemInfo
        {
            Slot = x.Entry.Slot, Uid = x.Entry.Uid, ItemId = x.Item!.ItemId, Quantity = x.Entry.Quantity,
            Upgrade = (byte)x.Item.Upgrade, OptionId = x.Item.OptionId,
        })
        .ToList();
}
