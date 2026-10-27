using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Items;
using Game.Shared.Loot;
using Game.Shared.Text;

namespace Game.Shared.Economy
{
    /// <summary>Preços de venda ao NPC. Números para discutir na revisão da economia.</summary>
    public static class EconomyRules
    {
        /// <summary>Quanto o NPC paga por uma unidade (a melhoria aumenta o preço do equipamento).</summary>
        public static long SellPrice(ItemDefinition definition, int upgrade = 0)
        {
            if (definition.Kind == ItemKind.Equipment)
                return 120L * definition.Grade * definition.Grade * (4 + Math.Max(0, upgrade)) / 4;
            if (definition.UpgradeCoreGrade > 0) return 250L * definition.UpgradeCoreGrade;
            if (definition.IsOptionStone) return 400;
            if (definition.Kind == ItemKind.Consumable)
            {
                var entry = ShopCatalog.Find(definition.Id);
                return entry != null ? entry.Price / 4 : 5;
            }
            return definition.Rarity switch
            {
                ItemRarity.Common => 12,
                ItemRarity.Uncommon => 45,
                ItemRarity.Rare => 200,
                ItemRarity.Epic => 1500,
                _ => 12,
            };
        }
    }

    public sealed class ShopEntry
    {
        public string ItemId = "";
        public long Price;

        public ShopEntry(string itemId, long price)
        {
            ItemId = itemId;
            Price = price;
        }
    }

    /// <summary>O que a mercadora da vila vende (sempre em estoque).</summary>
    public static class ShopCatalog
    {
        public static readonly IReadOnlyList<ShopEntry> Stock = new List<ShopEntry>
        {
            new ShopEntry("pocao_hp_p", 40),
            new ShopEntry("pocao_hp_m", 160),
            new ShopEntry("pocao_mp_p", 40),
            new ShopEntry("pocao_mp_m", 160),
            new ShopEntry(ItemCatalog.CoreLow, 1500),
            new ShopEntry("chave_cripta", 300),
            new ShopEntry("chave_santuario", 1500),
            new ShopEntry("armadura_ferro", 900),
            new ShopEntry("luvas_ferro", 450),
            new ShopEntry("botas_ferro", 450),
            new ShopEntry("anel_ferro", 600),
            // Aço Negro (Nv10): antes só caía de monstro, e a arma tinha ~57% de chance de ter caído até o Nv12.
            new ShopEntry("espada_aco_negro", 3500),
            new ShopEntry("cajado_aco_negro", 3500),
            new ShopEntry("arco_aco_negro", 3500),
            new ShopEntry("armadura_aco_negro", 3000),
            new ShopEntry("luvas_aco_negro", 1500),
            new ShopEntry("botas_aco_negro", 1500),
            new ShopEntry("anel_aco_negro", 2000),
        };

        public static ShopEntry? Find(string itemId) => Stock.FirstOrDefault(e => e.ItemId == itemId);
    }

    /// <summary>Regras da troca direta entre jogadores.</summary>
    /// <summary>
    /// Mercado da vila (quadro de ofertas): o jogador deixa um item à venda com preço; outro compra mesmo com o vendedor
    /// offline, e o vendedor recebe o Alz (menos a taxa) com a escrivã. Oferta vencida volta para quem pôs.
    /// </summary>
    public static class MarketRules
    {
        public const int MaxListings = 10;
        public const double ListingHours = 72;

        /// <summary>Taxa sobre a venda: tira Alz do jogo (o mercado não pode ser mais barato que vender ao NPC à toa).</summary>
        public const int FeePercent = 5;

        public const long MinPrice = 1;
        public const long MaxPrice = 999_999_999;

        /// <summary>Quantas ofertas o quadro mostra (as mais novas).</summary>
        public const int MaxShown = 200;

        public static bool ValidPrice(long price) => price >= MinPrice && price <= MaxPrice;

        public static long Fee(long price) => Math.Max(0, price) * FeePercent / 100;

        /// <summary>O que o vendedor recebe pela venda.</summary>
        public static long SellerGets(long price) => price - Fee(price);
    }

    /// <summary>Situação de uma oferta do mercado. Só anda para a frente (Ativa → Vendida → Recebida).</summary>
    public enum MarketListingState : byte
    {
        Active = 0,
        Sold = 1,

        /// <summary>Retirada pelo vendedor (o item voltou para a bolsa).</summary>
        Cancelled = 2,

        /// <summary>Vendida e o vendedor já recebeu o Alz.</summary>
        Collected = 3,

        /// <summary>Venceu sem vender e o item voltou para o vendedor.</summary>
        Returned = 4,
    }

    public static class TradeRules
    {
        public const int MaxItems = 8;

        /// <summary>Distância para pedir e para confirmar a troca.</summary>
        public const float Range = 6f;

        /// <summary>Se se afastarem mais que isto, a troca é cancelada.</summary>
        public const float KeepRange = 12f;

        public const double InviteSeconds = 30;
    }

    /// <summary>Um item oferecido: posição na bolsa, id único (confere que é o mesmo item) e quantidade.</summary>
    public struct TradeItem
    {
        public int Slot;
        public long Uid;
        public int Quantity;

        public TradeItem(int slot, long uid, int quantity)
        {
            Slot = slot;
            Uid = uid;
            Quantity = quantity;
        }
    }

    /// <summary>O que um lado oferece.</summary>
    public sealed class TradeOffer
    {
        public List<TradeItem> Items = new List<TradeItem>();
        public long Alz;
    }

    /// <summary>
    /// Troca entre dois jogadores, com dupla confirmação: cada um monta a oferta e aperta "Pronto" (trava); com os dois
    /// travados, cada um aperta "Confirmar". Qualquer mudança de oferta destrava os dois (ninguém confirma uma coisa e
    /// recebe outra).
    /// </summary>
    public sealed class TradeSession
    {
        public TradeSession(int id, int a, int b)
        {
            Id = id;
            PlayerA = a;
            PlayerB = b;
        }

        public int Id { get; }
        public int PlayerA { get; }
        public int PlayerB { get; }
        public TradeOffer OfferA { get; private set; } = new TradeOffer();
        public TradeOffer OfferB { get; private set; } = new TradeOffer();
        public bool LockedA { get; private set; }
        public bool LockedB { get; private set; }
        public bool ConfirmedA { get; private set; }
        public bool ConfirmedB { get; private set; }

        public bool Involves(int playerId) => playerId == PlayerA || playerId == PlayerB;
        public int PartnerOf(int playerId) => playerId == PlayerA ? PlayerB : PlayerA;
        public TradeOffer OfferOf(int playerId) => playerId == PlayerA ? OfferA : OfferB;
        public bool LockedOf(int playerId) => playerId == PlayerA ? LockedA : LockedB;
        public bool ConfirmedOf(int playerId) => playerId == PlayerA ? ConfirmedA : ConfirmedB;
        public bool BothLocked => LockedA && LockedB;
        public bool BothConfirmed => ConfirmedA && ConfirmedB;

        /// <summary>Troca a oferta de um lado; destrava e tira a confirmação dos dois.</summary>
        public void SetOffer(int playerId, TradeOffer offer)
        {
            if (playerId == PlayerA) OfferA = offer;
            else OfferB = offer;
            LockedA = LockedB = ConfirmedA = ConfirmedB = false;
        }

        public void Lock(int playerId)
        {
            if (playerId == PlayerA) LockedA = true;
            else LockedB = true;
        }

        /// <summary>Confirma (só com os dois travados). Retorna falso se ainda não pode.</summary>
        public bool Confirm(int playerId)
        {
            if (!BothLocked) return false;
            if (playerId == PlayerA) ConfirmedA = true;
            else ConfirmedB = true;
            return true;
        }
    }

    /// <summary>Executa a troca: tudo ou nada, conferindo itens, Alz e espaço nas bolsas.</summary>
    public static class TradeExecutor
    {
        /// <summary>Confere uma oferta contra a bolsa e o Alz de quem oferece. Nulo = tudo certo.</summary>
        public static LocalText? CheckOffer(TradeOffer offer, ItemContainer bag, long alz)
        {
            if (offer.Alz < 0) return new LocalText("trade.bad_alz");
            if (offer.Alz > alz) return new LocalText("trade.no_alz");
            if (offer.Items.Count > TradeRules.MaxItems) return new LocalText("trade.max_items", TradeRules.MaxItems);
            var slots = new HashSet<int>();
            foreach (var entry in offer.Items)
            {
                if (!slots.Add(entry.Slot)) return new LocalText("trade.repeated");
                var item = bag[entry.Slot];
                if (item == null || item.Uid != entry.Uid) return new LocalText("trade.item_gone");
                if (entry.Quantity <= 0 || entry.Quantity > item.Quantity) return new LocalText("trade.bad_quantity");
            }
            return null;
        }

        /// <summary>
        /// Faz a troca. Primeiro simula em cópias das bolsas (tira o que cada um dá, depois põe o que recebe); só mexe nas
        /// bolsas de verdade se tudo couber. Retorna nulo se trocou, ou o motivo.
        /// </summary>
        public static LocalText? Execute(TradeSession session, ItemContainer bagA, ref long alzA, ItemContainer bagB, ref long alzB,
            Func<long> newUid)
        {
            var problem = CheckOffer(session.OfferA, bagA, alzA) ?? CheckOffer(session.OfferB, bagB, alzB);
            if (problem != null) return problem;

            long fakeUid = 0;
            var testA = bagA.Clone();
            var testB = bagB.Clone();
            var fromA = TakeAll(testA, session.OfferA);
            var fromB = TakeAll(testB, session.OfferB);
            if (!PutAll(testA, fromB, () => ++fakeUid)) return new LocalText("trade.no_space_mine");
            if (!PutAll(testB, fromA, () => ++fakeUid)) return new LocalText("trade.no_space_other");

            fromA = TakeAll(bagA, session.OfferA);
            fromB = TakeAll(bagB, session.OfferB);
            PutAll(bagA, fromB, newUid);
            PutAll(bagB, fromA, newUid);
            alzA = alzA - session.OfferA.Alz + session.OfferB.Alz;
            alzB = alzB - session.OfferB.Alz + session.OfferA.Alz;
            return null;
        }

        /// <summary>Tira os itens oferecidos. Pilha inteira sai como está (com id e melhoria); parte de pilha vira item novo.</summary>
        private static List<ItemInstance> TakeAll(ItemContainer bag, TradeOffer offer)
        {
            var taken = new List<ItemInstance>();
            foreach (var entry in offer.Items)
            {
                var item = bag[entry.Slot]!;
                if (entry.Quantity >= item.Quantity)
                {
                    taken.Add(bag.Take(entry.Slot)!);
                    continue;
                }
                item.Quantity -= entry.Quantity;
                taken.Add(new ItemInstance { Uid = 0, ItemId = item.ItemId, Quantity = entry.Quantity });
            }
            return taken;
        }

        /// <summary>Equipamento entra como está; empilháveis juntam nas pilhas que já existem.</summary>
        private static bool PutAll(ItemContainer bag, List<ItemInstance> items, Func<long> newUid)
        {
            foreach (var item in items)
            {
                var definition = ItemCatalog.Get(item.ItemId);
                if (definition.MaxStack > 1)
                {
                    if (!bag.TryAdd(item.ItemId, item.Quantity, newUid)) return false;
                }
                else
                {
                    if (item.Uid == 0) item.Uid = newUid();
                    if (!bag.TryPlace(item, out _)) return false;
                }
            }
            return true;
        }
    }
}
