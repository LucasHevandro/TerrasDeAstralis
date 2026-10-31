using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Items;
using Game.Shared.World;

namespace Game.Shared.Tests.Items;

public class CatalogTests
{
    [Fact]
    public void Todo_item_que_monstro_derruba_existe_no_catalogo()
    {
        foreach (var spawn in WorldContent.Spawns())
            foreach (var drop in spawn.Data.Drops.Entries)
                Assert.True(ItemCatalog.Find(drop.ItemId) != null, $"{spawn.Data.Id} derruba {drop.ItemId}, que não existe");
    }

    [Fact]
    public void Ha_tres_graus_de_equipamento_para_cada_posicao_e_classe()
    {
        var equipment = ItemCatalog.All.Where(i => i.Kind == ItemKind.Equipment).ToList();

        foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
            for (var grade = 1; grade <= 3; grade++)
                Assert.Contains(equipment, i => i.Slot == slot && i.Grade == grade);
        Assert.Equal(3, equipment.Count(i => i.RequiredClass == CharacterClass.Blade));
        Assert.Equal(3, equipment.Count(i => i.RequiredClass == CharacterClass.Arcanist));
        Assert.Equal(3, equipment.Count(i => i.RequiredClass == CharacterClass.Ranger));
        Assert.Equal(3, equipment.Count(i => i.RequiredClass == CharacterClass.Guardian));
    }

    [Fact]
    public void Ranger_comeca_com_atributos_no_mesmo_total_das_outras()
    {
        var total = Attributes.StartingFor(CharacterClass.Blade).Total;
        Assert.Equal(total, Attributes.StartingFor(CharacterClass.Ranger).Total);
        Assert.Equal(total, Attributes.StartingFor(CharacterClass.Arcanist).Total);
        Assert.Equal(total, Attributes.StartingFor(CharacterClass.Guardian).Total);
    }

    [Fact]
    public void Grau_maior_sempre_tem_atributos_maiores()
    {
        foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
        {
            // Cada classe (e as peças de todas) comparada só com ela mesma.
            foreach (var group in ItemCatalog.All.Where(i => i.Kind == ItemKind.Equipment && i.Slot == slot).GroupBy(i => i.RequiredClass))
            {
                var bySlot = group.OrderBy(i => i.Grade).Select(i => Score(i.Stats)).ToList();
                for (var i = 1; i < bySlot.Count; i++)
                    Assert.True(bySlot[i] > bySlot[i - 1], $"{slot} {group.Key}: grau {i + 1} não supera o {i}");
            }
        }
    }

    private static float Score(StatBlock s) => s.AttackPower + s.MagicPower + s.Defense + s.MaxHp / 5f + s.MaxMp / 5f + s.CritPercent * 10;
}

public class ContainerTests
{
    private long _uid;
    private long NextUid() => ++_uid;

    [Fact]
    public void Empilhaveis_juntam_ate_o_limite_e_equipamento_nao()
    {
        var bag = new ItemContainer(4);

        Assert.True(bag.TryAdd("pele_lobo", 150, NextUid));  // 99 + 51
        Assert.True(bag.TryAdd("espada_ferro", 1, NextUid));
        Assert.True(bag.TryAdd("espada_ferro", 1, NextUid));

        Assert.Equal(150, bag.Count("pele_lobo"));
        Assert.Equal(0, bag.FreeSlots);
        Assert.NotEqual(bag[2]!.Uid, bag[3]!.Uid);
    }

    [Fact]
    public void Sem_espaco_nao_adiciona_nada()
    {
        var bag = new ItemContainer(1);
        bag.TryAdd("pele_lobo", 98, NextUid);

        Assert.False(bag.TryAdd("pele_lobo", 5, NextUid)); // só cabe 1
        Assert.Equal(98, bag.Count("pele_lobo"));
        Assert.True(bag.TryAdd("pele_lobo", 1, NextUid));
    }

    [Fact]
    public void Remover_eh_tudo_ou_nada()
    {
        var bag = new ItemContainer(4);
        bag.TryAdd("seda", 10, NextUid);

        Assert.False(bag.TryRemove("seda", 11));
        Assert.Equal(10, bag.Count("seda"));
        Assert.True(bag.TryRemove("seda", 10));
        Assert.Equal(4, bag.FreeSlots);
    }

    [Fact]
    public void Mover_troca_ou_junta()
    {
        var bag = new ItemContainer(4);
        bag.TryAdd("espada_ferro", 1, NextUid);   // posição 0
        bag.TryAdd("seda", 5, NextUid);           // posição 1
        bag.TryPut(2, new ItemInstance { Uid = NextUid(), ItemId = "seda", Quantity = 3 });

        Assert.True(bag.Move(0, 3));
        Assert.Null(bag[0]);
        Assert.Equal("espada_ferro", bag[3]!.ItemId);

        Assert.True(bag.Move(2, 1)); // junta seda 3 + 5
        Assert.Equal(8, bag[1]!.Quantity);
        Assert.Null(bag[2]);
    }
}

public class EquipmentTests
{
    private long _uid;
    private long NextUid() => ++_uid;

    [Fact]
    public void Vestir_troca_com_o_que_estava_vestido()
    {
        var bag = new ItemContainer(4);
        var equipment = new Equipment();
        bag.TryAdd("espada_ferro", 1, NextUid);
        bag.TryAdd("espada_ferro", 1, NextUid);
        var second = bag[1]!.Uid;

        Assert.Null(equipment.Equip(bag, 0, 1, CharacterClass.Blade));
        Assert.Null(equipment.Equip(bag, 1, 1, CharacterClass.Blade));

        Assert.Equal(second, equipment[EquipSlot.Weapon]!.Uid);
        Assert.Equal("espada_ferro", bag[1]!.ItemId); // a primeira voltou para o lugar da segunda
    }

    [Fact]
    public void Nivel_e_classe_sao_conferidos()
    {
        var bag = new ItemContainer(4);
        var equipment = new Equipment();
        bag.TryAdd("espada_aco_negro", 1, NextUid);
        bag.TryAdd("cajado_ferro", 1, NextUid);

        Assert.Contains("nível 10", equipment.Equip(bag, 0, 5, CharacterClass.Blade)?.ToString());
        Assert.Contains("Arcanista", equipment.Equip(bag, 1, 30, CharacterClass.Blade)?.ToString());
        Assert.Null(equipment[EquipSlot.Weapon]);
        Assert.Equal(2, 4 - bag.FreeSlots);
    }

    [Fact]
    public void Tirar_precisa_de_espaco_na_bolsa()
    {
        var bag = new ItemContainer(1);
        var equipment = new Equipment();
        bag.TryAdd("anel_ferro", 1, NextUid);
        equipment.Equip(bag, 0, 1, CharacterClass.Blade);
        bag.TryAdd("seda", 1, NextUid);

        Assert.Equal("Bolsa cheia.", equipment.Unequip(EquipSlot.Accessory, bag)?.ToString());
        bag.TryRemove("seda", 1);
        Assert.Null(equipment.Unequip(EquipSlot.Accessory, bag));
    }

    [Fact]
    public void Atributos_somam_nivel_pontos_equipamento_melhoria_e_opcao()
    {
        var equipment = new Equipment();
        var sword = new ItemInstance { Uid = 1, ItemId = "espada_ferro", Upgrade = 2, OptionId = "pedra_furia" };
        equipment.Put(EquipSlot.Weapon, sword);
        var attributes = Attributes.StartingFor(CharacterClass.Blade);

        var stats = StatCalculator.Compute(1, attributes, equipment);

        // nível 1: 60 de ataque; força 10: +20; espada: 25 + 2×4; pedra: +15
        Assert.Equal(60 + 20 + 25 + 8 + 15, stats.AttackPower);
        Assert.Equal(2000 + 50, stats.MaxHp);
    }

    [Fact]
    public void Pontos_livres_vem_do_nivel()
    {
        var start = Attributes.StartingFor(CharacterClass.Arcanist);

        Assert.Equal(0, Attributes.FreePoints(1, CharacterClass.Arcanist, start));
        Assert.Equal(45, Attributes.FreePoints(10, CharacterClass.Arcanist, start));
        var spent = start;
        spent.Intelligence += 20;
        Assert.Equal(25, Attributes.FreePoints(10, CharacterClass.Arcanist, spent));
    }
}

public class UpgradeTests
{
    private sealed class FixedRandom : IRandomSource
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public double NextDouble() => _value;
    }

    private static readonly IRandomSource AlwaysFail = new FixedRandom(0.9999);
    private static readonly IRandomSource AlwaysSucceed = new FixedRandom(0.0);

    private static (ItemInstance Item, ItemContainer Bag) Setup(int upgrade, int cores = 100)
    {
        var bag = new ItemContainer();
        long uid = 0;
        bag.TryAdd(ItemCatalog.CoreLow, cores, () => ++uid);
        return (new ItemInstance { Uid = 999, ItemId = "espada_ferro", Upgrade = upgrade }, bag);
    }

    [Fact]
    public void Sucesso_sobe_um_nivel_e_cobra_nucleo_e_alz()
    {
        var (item, bag) = Setup(0);
        long alz = 1000;

        Assert.Equal(UpgradeOutcome.Success, UpgradeRules.Try(item, bag, ref alz, AlwaysSucceed));

        Assert.Equal(1, item.Upgrade);
        Assert.Equal(99, bag.Count(ItemCatalog.CoreLow));
        Assert.Equal(1000 - 100, alz);
    }

    [Fact]
    public void Abaixo_de_mais_5_falhar_mantem_o_nivel()
    {
        var (item, bag) = Setup(3);
        long alz = 100_000;

        Assert.Equal(UpgradeOutcome.FailedKept, UpgradeRules.Try(item, bag, ref alz, AlwaysFail));
        Assert.Equal(3, item.Upgrade);
        Assert.Equal(1, item.PityFails);
    }

    [Fact]
    public void De_mais_5_em_diante_falhar_perde_um_nivel_mas_nunca_quebra()
    {
        var (item, bag) = Setup(7);
        long alz = 1_000_000;

        Assert.Equal(UpgradeOutcome.FailedDowngraded, UpgradeRules.Try(item, bag, ref alz, AlwaysFail));
        Assert.Equal(6, item.Upgrade);

        // Recuperar o nível perdido é garantido, e a falha em +7 continua contando.
        Assert.True(UpgradeRules.IsGuaranteed(item));
        Assert.Equal(UpgradeOutcome.Success, UpgradeRules.Try(item, bag, ref alz, AlwaysFail));
        Assert.Equal(7, item.Upgrade);
        Assert.Equal(1, UpgradeRules.PityProgress(item));
    }

    [Fact]
    public void Protecao_garante_o_sucesso_depois_de_falhas_seguidas()
    {
        var (item, bag) = Setup(9); // +9 → +10: 30%, garantia depois de 4 falhas
        item.PityLevel = 9;
        item.PityFails = UpgradeRules.PityLimit(9);
        long alz = 1_000_000;

        Assert.Equal(4, UpgradeRules.PityLimit(9));
        Assert.Equal(UpgradeOutcome.Success, UpgradeRules.Try(item, bag, ref alz, AlwaysFail));
        Assert.Equal(10, item.Upgrade);
        Assert.Equal(0, item.PityFails);
    }

    [Fact]
    public void Sem_material_ou_alz_nao_tenta_e_nada_muda()
    {
        var (item, bag) = Setup(0, cores: 0);
        long alz = 1_000_000;

        Assert.Equal(UpgradeOutcome.Refused, UpgradeRules.Try(item, bag, ref alz, AlwaysSucceed));
        Assert.Equal(0, item.Upgrade);
        Assert.Equal(1_000_000, alz);

        var (item2, bag2) = Setup(0);
        long poor = 50;
        Assert.Equal(UpgradeOutcome.Refused, UpgradeRules.Try(item2, bag2, ref poor, AlwaysSucceed));
        Assert.Equal(100, bag2.Count(ItemCatalog.CoreLow));
    }

    [Fact]
    public void Nucleo_errado_nao_serve()
    {
        var bag = new ItemContainer();
        long uid = 0;
        bag.TryAdd(ItemCatalog.CoreLow, 10, () => ++uid);
        var steel = new ItemInstance { Uid = 1, ItemId = "espada_aco_negro" };
        long alz = 1_000_000;

        Assert.Contains("médio", UpgradeRules.CanTry(steel, bag, alz)?.ToString());
    }

    [Fact]
    public void Media_de_tentativas_ate_mais_10_fica_razoavel()
    {
        // Simulação com o sorteio real: quantas tentativas leva de +0 a +10 (com a proteção).
        var random = new SystemRandomSource(42);
        var attempts = new List<int>();
        for (var run = 0; run < 2000; run++)
        {
            var (item, bag) = Setup(0, cores: 99);
            long alz = long.MaxValue / 2;
            var tries = 0;
            while (item.Upgrade < 10)
            {
                if (bag.Count(ItemCatalog.CoreLow) < 3) bag.TryAdd(ItemCatalog.CoreLow, 50, () => 0);
                UpgradeRules.Try(item, bag, ref alz, random);
                tries++;
            }
            attempts.Add(tries);
        }

        // Sem proteção a cauda seria longa; com ela o pior caso fica limitado.
        var average = attempts.Average();
        Assert.True(average >= 12 && average <= 30, $"média de {average:0.0} tentativas (máx {attempts.Max()})");
        Assert.True(attempts.Max() <= 44, $"pior caso {attempts.Max()} tentativas (limite teórico 44)");
    }

    [Fact]
    public void Melhorias_altas_valem_mais()
    {
        Assert.Equal(6, UpgradeRules.EffectiveSteps(6));
        Assert.Equal(8, UpgradeRules.EffectiveSteps(7));
        Assert.Equal(14, UpgradeRules.EffectiveSteps(10));
    }

    [Fact]
    public void Pedra_de_opcao_so_entra_em_item_sem_opcao()
    {
        var bag = new ItemContainer();
        long uid = 0;
        bag.TryAdd("pedra_vigor", 2, () => ++uid);
        var armor = new ItemInstance { Uid = 1, ItemId = "armadura_ferro" };

        Assert.Null(UpgradeRules.ApplyOption(armor, bag, "pedra_vigor"));
        Assert.Equal("pedra_vigor", armor.OptionId);
        Assert.NotNull(UpgradeRules.ApplyOption(armor, bag, "pedra_vigor"));
        Assert.Equal(1, bag.Count("pedra_vigor"));
        Assert.NotNull(UpgradeRules.ApplyOption(new ItemInstance { ItemId = "armadura_ferro" }, bag, "seda"));
    }
}
