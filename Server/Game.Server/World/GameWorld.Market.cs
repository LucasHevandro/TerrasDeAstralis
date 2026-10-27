using Game.Server.Persistence;
using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;

namespace Game.Server.World;

/// <summary>Uma oferta do mercado na memória do servidor (a fonte da verdade; o banco acompanha pelos salvamentos).</summary>
public sealed class MarketListing
{
    public long Id { get; init; }

    /// <summary>Personagem do banco que vendeu (0 = convidado: a oferta nunca é salva).</summary>
    public int SellerCharacterId { get; init; }

    /// <summary>Quem é o dono para o mundo: o personagem do banco ou, no convidado, o id dele negativo.</summary>
    public int SellerKey { get; init; }

    public string SellerName { get; init; } = "";
    public required ItemInstance Item { get; init; }
    public long Price { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public MarketListingState State { get; set; }
    public string BuyerName { get; set; } = "";
    public DateTimeOffset? SoldAt { get; set; }

    public bool IsExpired(DateTimeOffset now) => State == MarketListingState.Active && now >= ExpiresAt;
}

/// <summary>
/// Mercado da vila (quadro de ofertas com a escrivã): pôr à venda, comprar, retirar e receber. Cada mudança entra no
/// salvamento de quem a fez (na mesma transação que a bolsa e o Alz dele) e pede esse salvamento na hora: assim a
/// oferta salva no banco nunca fica à frente do personagem, e uma queda não duplica nem some com itens.
/// </summary>
public sealed partial class GameWorld
{
    private readonly Dictionary<long, MarketListing> _market = new();

    /// <summary>Ofertas ativas e vendidas ainda não recebidas.</summary>
    public IReadOnlyCollection<MarketListing> MarketListings => _market.Values;

    /// <summary>Pede para salvar o personagem agora (o servidor põe na fila de salvamento).</summary>
    public event Action<ServerPlayer>? SaveRequested;

    /// <summary>Ofertas do banco ao ligar (ativas e vendidas ainda não recebidas).</summary>
    public void SeedMarket(IEnumerable<MarketListingRecord> records)
    {
        foreach (var record in records)
        {
            if (record.State is not (MarketListingState.Active or MarketListingState.Sold)) continue;
            _market[record.Id] = new MarketListing
            {
                Id = record.Id, SellerCharacterId = record.SellerCharacterId, SellerKey = record.SellerCharacterId,
                SellerName = record.SellerName, Item = FromRecord(record.Item), Price = record.Price,
                CreatedAt = record.CreatedAt, ExpiresAt = record.ExpiresAt, State = record.State, BuyerName = record.BuyerName,
                SoldAt = record.SoldAt,
            };
        }
    }

    private static int MarketKey(ServerPlayer player) => player.CharacterId != 0 ? player.CharacterId : -player.Id;

    private void HandleMarket(ServerPlayer player, MarketCommand command)
    {
        if (player.TradeId != 0)
        {
            SystemChat(player, LT("market.finish_trade"));
            return;
        }
        if (player.IsDead || !NearNpc(player, NpcRole.Market))
        {
            Notify(player, LT("market.go_to", Loc.Ref("npc." + NpcCatalog.Market)));
            return;
        }
        switch (command.Action)
        {
            case MarketActionKind.Sell:
                MarketSell(player, command.Slot, command.Quantity, command.Price);
                break;
            case MarketActionKind.Buy:
                MarketBuy(player, command.ListingId);
                break;
            case MarketActionKind.Cancel:
                MarketCancel(player, command.ListingId);
                break;
            case MarketActionKind.Collect:
                MarketCollect(player);
                break;
        }
        SendMarket(player);
    }

    private void MarketSell(ServerPlayer player, int slot, int quantity, long price)
    {
        var item = player.Bag[slot];
        if (item == null) return;
        if (ItemCatalog.Find(item.ItemId) is { IsQuestItem: true })
        {
            Notify(player, LT("item.quest_bound"));
            return;
        }
        if (!MarketRules.ValidPrice(price))
        {
            Notify(player, LT("market.bad_price", MarketRules.MinPrice, MarketRules.MaxPrice));
            return;
        }
        var key = MarketKey(player);
        if (_market.Values.Count(l => l.SellerKey == key) >= MarketRules.MaxListings)
        {
            Notify(player, LT("market.limit", MarketRules.MaxListings));
            return;
        }
        quantity = Math.Clamp(quantity <= 0 ? item.Quantity : quantity, 1, item.Quantity);
        ItemInstance listed;
        if (quantity == item.Quantity)
        {
            player.Bag.Take(slot);
            listed = item;
        }
        else
        {
            // Parte da pilha: o resto fica na bolsa e a parte vendida ganha um id novo.
            item.Quantity -= quantity;
            listed = new ItemInstance { Uid = _uids.Next(), ItemId = item.ItemId, Quantity = quantity };
        }
        var now = Clock();
        var listing = new MarketListing
        {
            Id = _uids.Next(), SellerCharacterId = player.CharacterId, SellerKey = key, SellerName = player.Name, Item = listed,
            Price = price, CreatedAt = now, ExpiresAt = now.AddHours(MarketRules.ListingHours), State = MarketListingState.Active,
        };
        _market[listing.Id] = listing;
        player.InventoryDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        Log(player, "mercado_oferta", listed.ItemId, quantity, 0, $"oferta {listing.Id} por {price}, uid {listed.Uid}");
        RecordMarket(player, listing);
        Notify(player, LT("market.listed", Loc.Ref("item." + listed.ItemId), quantity, price));
    }

    private void MarketBuy(ServerPlayer player, long listingId)
    {
        if (!_market.TryGetValue(listingId, out var listing) || listing.State != MarketListingState.Active || listing.IsExpired(Clock()))
        {
            Notify(player, LT("market.not_found"));
            return;
        }
        if (listing.SellerKey == MarketKey(player))
        {
            Notify(player, LT("market.own"));
            return;
        }
        if (player.Alz < listing.Price)
        {
            Notify(player, LT("market.no_alz", listing.Price));
            return;
        }
        if (!Receive(player, listing.Item))
        {
            Notify(player, LT("bag.full_short"));
            return;
        }
        player.Alz -= listing.Price;
        listing.State = MarketListingState.Sold;
        listing.BuyerName = player.Name;
        listing.SoldAt = Clock();
        player.InventoryDirty = true;
        player.StatsDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        var item = listing.Item;
        Log(player, "mercado_compra", item.ItemId, item.Quantity, -listing.Price, $"oferta {listing.Id} de {listing.SellerName}, uid {item.Uid}");
        RecordMarket(player, listing);
        Notify(player, LT("market.bought", Loc.Ref("item." + item.ItemId), item.Quantity, listing.Price));

        // Vendedor online: fica sabendo na hora (o Alz espera na escrivã, como para quem está offline).
        var seller = _players.Values.FirstOrDefault(p => MarketKey(p) == listing.SellerKey);
        if (seller != null)
            Notify(seller, LT("market.sold_notice", Loc.Ref("item." + item.ItemId), item.Quantity, player.Name,
                MarketRules.SellerGets(listing.Price), Loc.Ref("npc." + NpcCatalog.Market)), announcement: true);
    }

    private void MarketCancel(ServerPlayer player, long listingId)
    {
        if (!_market.TryGetValue(listingId, out var listing) || listing.SellerKey != MarketKey(player) ||
            listing.State != MarketListingState.Active)
        {
            Notify(player, LT("market.not_found"));
            return;
        }
        if (!Receive(player, listing.Item))
        {
            Notify(player, LT("bag.full_short"));
            return;
        }
        listing.State = MarketListingState.Cancelled;
        _market.Remove(listing.Id);
        player.InventoryDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        Log(player, "mercado_retirada", listing.Item.ItemId, listing.Item.Quantity, 0, $"oferta {listing.Id}");
        RecordMarket(player, listing);
        Notify(player, LT("market.cancelled", Loc.Ref("item." + listing.Item.ItemId)));
    }

    /// <summary>Recebe o Alz das vendas (menos a taxa) e os itens das ofertas vencidas (os que couberem na bolsa).</summary>
    private void MarketCollect(ServerPlayer player)
    {
        var key = MarketKey(player);
        var now = Clock();
        long alz = 0;
        var items = 0;
        var full = false;
        foreach (var listing in _market.Values.Where(l => l.SellerKey == key).OrderBy(l => l.CreatedAt).ToList())
        {
            if (listing.State == MarketListingState.Sold)
            {
                var gets = MarketRules.SellerGets(listing.Price);
                player.Alz += gets;
                alz += gets;
                listing.State = MarketListingState.Collected;
                Log(player, "mercado_venda", listing.Item.ItemId, listing.Item.Quantity, gets,
                    $"oferta {listing.Id} para {listing.BuyerName} por {listing.Price} (taxa {MarketRules.Fee(listing.Price)})");
            }
            else if (listing.IsExpired(now))
            {
                if (!Receive(player, listing.Item))
                {
                    full = true;
                    continue;
                }
                listing.State = MarketListingState.Returned;
                items++;
                Log(player, "mercado_vencida", listing.Item.ItemId, listing.Item.Quantity, 0, $"oferta {listing.Id}");
            }
            else continue;
            _market.Remove(listing.Id);
            RecordMarket(player, listing, save: false);
        }
        if (alz == 0 && items == 0)
        {
            Notify(player, full ? LT("market.collected_partial") : LT("market.nothing"));
            return;
        }
        player.InventoryDirty = true;
        player.StatsDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        SaveRequested?.Invoke(player);
        Notify(player, LT("market.collected", alz, items));
        if (full) Notify(player, LT("market.collected_partial"));
    }

    /// <summary>Põe o item na bolsa: pilhas juntam com as que já existem; equipamento vai inteiro (melhoria e opção).</summary>
    private bool Receive(ServerPlayer player, ItemInstance item)
    {
        var definition = ItemCatalog.Get(item.ItemId);
        if (definition.MaxStack > 1) return player.Bag.TryAdd(item.ItemId, item.Quantity, _uids.Next);
        return player.Bag.TryPlace(item, out _);
    }

    /// <summary>A oferta como está agora entra no próximo salvamento do jogador, que é pedido na hora.</summary>
    private void RecordMarket(ServerPlayer player, MarketListing listing, bool save = true)
    {
        if (player.CharacterId != 0 && listing.SellerCharacterId != 0) player.PendingMarket.Add(ToRecord(listing));
        if (save) SaveRequested?.Invoke(player);
    }

    private static MarketListingRecord ToRecord(MarketListing listing) => new(
        listing.Id, listing.SellerCharacterId, listing.SellerName, ToRecord(listing.Item, ItemContainerKind.Bag, 0), listing.Price,
        listing.State, listing.BuyerName, listing.CreatedAt, listing.ExpiresAt, listing.SoldAt);

    private static ItemInstance FromRecord(ItemRecord item) => new()
    {
        Uid = item.Uid, ItemId = item.ItemId, Quantity = item.Quantity, Upgrade = item.Upgrade, PityFails = item.PityFails,
        PityLevel = item.PityLevel, RegainGuaranteed = item.RegainGuaranteed, OptionId = item.OptionId,
    };

    /// <summary>O quadro para o jogador: o que está à venda (mais novas primeiro) e as ofertas dele.</summary>
    private void SendMarket(ServerPlayer player)
    {
        var key = MarketKey(player);
        var now = Clock();
        var state = new MarketState();
        foreach (var listing in _market.Values.Where(l => l.State == MarketListingState.Active && !l.IsExpired(now))
                     .OrderByDescending(l => l.CreatedAt).Take(MarketRules.MaxShown))
            state.Offers.Add(Entry(listing, now));
        foreach (var listing in _market.Values.Where(l => l.SellerKey == key).OrderByDescending(l => l.CreatedAt))
        {
            state.Mine.Add(Entry(listing, now));
            if (listing.State == MarketListingState.Sold) state.ToCollectAlz += MarketRules.SellerGets(listing.Price);
            else if (listing.IsExpired(now)) state.ToCollectItems++;
        }
        _outbox.Send(player.Id, state);
    }

    private static MarketEntry Entry(MarketListing listing, DateTimeOffset now) => new()
    {
        Id = listing.Id, ItemId = listing.Item.ItemId, Quantity = listing.Item.Quantity, Upgrade = (byte)Math.Clamp(listing.Item.Upgrade, 0, 255),
        OptionId = listing.Item.OptionId, Price = listing.Price, Seller = listing.SellerName, State = listing.State,
        Expired = listing.IsExpired(now), HoursLeft = (float)Math.Max(0, (listing.ExpiresAt - now).TotalHours), Buyer = listing.BuyerName,
    };
}
