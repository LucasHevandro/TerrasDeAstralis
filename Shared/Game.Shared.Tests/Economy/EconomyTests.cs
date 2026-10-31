using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;

namespace Game.Shared.Tests.Economy;

public class EconomyTests
{
    private long _uid = 100;
    private long NewUid() => ++_uid;

    [Fact]
    public void Precos_de_venda_seguem_tipo_grau_e_melhoria()
    {
        Assert.Equal(120, EconomyRules.SellPrice(ItemCatalog.Get("espada_ferro")));
        Assert.Equal(1080, EconomyRules.SellPrice(ItemCatalog.Get("espada_astralita")));
        Assert.Equal(240, EconomyRules.SellPrice(ItemCatalog.Get("espada_ferro"), 4)); // +4 = dobro
        Assert.Equal(12, EconomyRules.SellPrice(ItemCatalog.Get("pelo_macio")));
        Assert.Equal(10, EconomyRules.SellPrice(ItemCatalog.Get("pocao_hp_p"))); // um quarto do preço da loja
        Assert.Equal(250, EconomyRules.SellPrice(ItemCatalog.Get(ItemCatalog.CoreLow)));

        // Nada da loja revende com lucro.
        foreach (var entry in ShopCatalog.Stock)
            Assert.True(EconomyRules.SellPrice(ItemCatalog.Get(entry.ItemId)) < entry.Price, entry.ItemId);
    }

    [Theory]
    [InlineData(Game.Shared.Accounts.CharacterClass.Blade)]
    [InlineData(Game.Shared.Accounts.CharacterClass.Arcanist)]
    [InlineData(Game.Shared.Accounts.CharacterClass.Ranger)]
    [InlineData(Game.Shared.Accounts.CharacterClass.Guardian)]
    public void Economia_fecha_positiva_em_cada_faixa_e_gasta_boa_parte(Game.Shared.Accounts.CharacterClass characterClass)
    {
        var bands = EconomySimulation.Run(characterClass);
        Assert.Equal(3, bands.Count);
        // Ninguém fica no vermelho jogando normalmente, mas a maior parte do Alz volta para o jogo (melhoria, poções, loja).
        Assert.All(bands, b => Assert.True(b.Balance >= 0, $"{b.Name}: saldo {b.Balance}"));
        var spentShare = bands.Sum(b => b.Spent) / (double)bands.Sum(b => b.Income);
        // Com a escala de poder das skills (2026-10-25) a caça ficou mais rápida e entra um pouco mais de Alz (~0,6 gasto).
        Assert.InRange(spentShare, 0.55, 0.9);
    }

    [Fact]
    public void Mudar_a_oferta_destrava_e_confirmar_exige_os_dois_prontos()
    {
        var trade = new TradeSession(1, 10, 20);
        Assert.False(trade.Confirm(10));
        trade.Lock(10);
        trade.Lock(20);
        Assert.True(trade.Confirm(10));
        Assert.True(trade.ConfirmedA);

        trade.SetOffer(20, new TradeOffer { Alz = 5 });
        Assert.False(trade.LockedA || trade.LockedB || trade.ConfirmedA || trade.ConfirmedB);
        Assert.Equal(20, trade.PartnerOf(10));
    }

    [Fact]
    public void Troca_move_equipamento_com_melhoria_pilhas_parciais_e_alz()
    {
        var bagA = new ItemContainer();
        var bagB = new ItemContainer();
        var sword = new ItemInstance { Uid = 7, ItemId = "espada_aco_negro", Upgrade = 5, OptionId = "pedra_furia" };
        bagA.TryPut(3, sword);
        bagA.TryAdd("pocao_hp_p", 30, NewUid);
        bagB.TryAdd("pocao_hp_p", 10, NewUid);
        bagB.TryAdd("pele_lobo", 5, NewUid);
        long alzA = 1000, alzB = 50;

        var trade = new TradeSession(1, 1, 2);
        var potionSlot = bagA.SlotOf(bagA.Items().First(i => i.Item.ItemId == "pocao_hp_p").Item.Uid);
        trade.SetOffer(1, new TradeOffer
        {
            Items = { new TradeItem(3, 7, 1), new TradeItem(potionSlot, bagA[potionSlot]!.Uid, 12) },
            Alz = 300,
        });
        var peleSlot = bagB.Items().First(i => i.Item.ItemId == "pele_lobo").Slot;
        trade.SetOffer(2, new TradeOffer { Items = { new TradeItem(peleSlot, bagB[peleSlot]!.Uid, 5) } });

        Assert.Null(TradeExecutor.Execute(trade, bagA, ref alzA, bagB, ref alzB, NewUid));
        Assert.Equal((700, 350), (alzA, alzB));
        Assert.Equal(18, bagA.Count("pocao_hp_p"));
        Assert.Equal(5, bagA.Count("pele_lobo"));
        Assert.Equal(22, bagB.Count("pocao_hp_p")); // juntou com a pilha que já tinha
        var moved = bagB.Items().Single(i => i.Item.ItemId == "espada_aco_negro").Item;
        Assert.Equal((7L, 5, "pedra_furia"), (moved.Uid, moved.Upgrade, moved.OptionId));
        Assert.Equal(0, bagA.Count("espada_aco_negro"));
    }

    [Fact]
    public void Troca_sem_espaco_ou_item_que_sumiu_nao_muda_nada()
    {
        var bagA = new ItemContainer();
        var bagB = new ItemContainer(size: 1);
        bagA.TryPut(0, new ItemInstance { Uid = 1, ItemId = "espada_ferro" });
        bagA.TryPut(1, new ItemInstance { Uid = 2, ItemId = "luvas_ferro" });
        bagB.TryPut(0, new ItemInstance { Uid = 3, ItemId = "anel_ferro" });
        long alzA = 0, alzB = 0;
        var trade = new TradeSession(1, 1, 2);
        trade.SetOffer(1, new TradeOffer { Items = { new TradeItem(0, 1, 1), new TradeItem(1, 2, 1) } });
        trade.SetOffer(2, new TradeOffer { Items = { new TradeItem(0, 3, 1) } });

        Assert.Equal("trade.no_space_other", TradeExecutor.Execute(trade, bagA, ref alzA, bagB, ref alzB, NewUid)?.Key);
        Assert.Equal(1, bagA.Count("espada_ferro"));
        Assert.Equal(1, bagA.Count("luvas_ferro"));
        Assert.Equal(1, bagB.Count("anel_ferro"));

        trade.SetOffer(1, new TradeOffer { Items = { new TradeItem(0, 99, 1) } });
        Assert.Equal("Um item da oferta não está mais na bolsa", TradeExecutor.Execute(trade, bagA, ref alzA, bagB, ref alzB, NewUid)?.ToString());
        trade.SetOffer(1, new TradeOffer { Alz = 10 });
        Assert.Equal("Alz insuficiente", TradeExecutor.Execute(trade, bagA, ref alzA, bagB, ref alzB, NewUid)?.ToString());
    }

    [Fact]
    public void Mensagens_da_economia_ida_e_volta()
    {
        static T RoundTrip<T>(T message) where T : INetMessage
        {
            var frame = NetCodec.Encode(message);
            var buffer = new FrameBuffer();
            buffer.Append(frame, 0, frame.Length);
            Assert.True(buffer.TryRead(out var type, out var payload));
            return Assert.IsType<T>(NetCodec.Decode(type, payload));
        }

        var shop = RoundTrip(new ShopCommand { Action = ShopActionKind.Sell, ItemId = "x", Slot = 4, Quantity = 9 });
        Assert.Equal((ShopActionKind.Sell, "x", (ushort)4, 9), (shop.Action, shop.ItemId, shop.Slot, shop.Quantity));

        var command = RoundTrip(new TradeCommand
        {
            Action = TradeActionKind.SetOffer, TargetId = 3, TargetName = "Bia", Items = { new TradeItem(2, 77, 5) }, Alz = 1234,
        });
        Assert.Equal((TradeActionKind.SetOffer, 3, "Bia", 1234L), (command.Action, command.TargetId, command.TargetName, command.Alz));
        Assert.Equal((2, 77L, 5), (command.Items[0].Slot, command.Items[0].Uid, command.Items[0].Quantity));

        var state = RoundTrip(new TradeState
        {
            Active = true, PartnerId = 4, PartnerName = "Caio", MyAlz = 10, TheirAlz = 20, MyLocked = true, TheirConfirmed = true,
            MyItems = { new TradeItemInfo { Slot = 1, Uid = 5, ItemId = "espada_ferro", Quantity = 1, Upgrade = 3, OptionId = "" } },
            TheirItems = { new TradeItemInfo { Slot = 0, Uid = 6, ItemId = "pocao_hp_p", Quantity = 7, OptionId = "" } },
        });
        Assert.True(state.Active && state.MyLocked && state.TheirConfirmed && !state.TheirLocked);
        Assert.Equal(("Caio", 10L, 20L), (state.PartnerName, state.MyAlz, state.TheirAlz));
        Assert.Equal(((byte)3, 7), (state.MyItems[0].Upgrade, state.TheirItems[0].Quantity));

        var invite = RoundTrip(new TradeInvite { FromId = 2, FromName = "Ana", Seconds = 30 });
        Assert.Equal((2, "Ana"), (invite.FromId, invite.FromName));
    }
}
