using System.Collections;
using System.Reflection;
using Game.Server.World;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>
/// Revisão de segurança automática: milhares de mensagens de cliente com valores aleatórios e extremos (NaN, infinito,
/// negativos, ids e posições que não existem). O servidor não pode lançar exceção nem deixar o estado inválido —
/// prova de que nada importante é decidido pelo que o cliente manda.
/// </summary>
public class FuzzTests
{
    private sealed class NullOutbox : IOutbox
    {
        public void Send(int playerId, INetMessage message) { }
    }

    private static readonly Type[] ClientMessages = typeof(Hello).Assembly.GetTypes()
        .Where(t => typeof(INetMessage).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
        .Where(t => (ushort)((INetMessage)Activator.CreateInstance(t)!).Type < 100 && t != typeof(Hello))
        .ToArray();

    private static readonly string[] Ids = ItemCatalog.All.Select(i => i.Id)
        .Concat(Game.Shared.Progression.NpcCatalog.All.Select(n => n.Id))
        .Concat(Game.Shared.Progression.QuestCatalog.All.Select(q => q.Id))
        .Concat(Game.Shared.Progression.ClassCatalog.SkillsFor(Game.Shared.Accounts.CharacterClass.Blade).Select(s => s.Skill.Id))
        .Concat(new[] { "cripta_esquecida", "", "nao_existe", new string('x', 300), "Bot01", "Ana", "Bia" })
        .ToArray();

    private static object? RandomValue(Type type, Random random, int depth)
    {
        // Metade valores plausíveis (slots e quantidades pequenas, ids de verdade), metade extremos: assim o fuzz passa
        // das checagens de entrada e chega às regras.
        if (type == typeof(int)) return random.Next(10) switch { 0 => -1, 1 => int.MaxValue, 2 => int.MinValue, 3 => random.Next(-50, 200), _ => random.Next(0, 6) };
        if (type == typeof(long)) return random.Next(6) switch { 0 => 0L, 1 => -1L, 2 => long.MaxValue, 3 => long.MinValue, _ => (long)random.Next(-1000, 1_000_000) };
        if (type == typeof(uint)) return (uint)random.Next();
        if (type == typeof(ushort)) return random.Next(6) == 0 ? ushort.MaxValue : (ushort)random.Next(0, 8);
        if (type == typeof(short)) return (short)random.Next(-10, 60);
        if (type == typeof(byte)) return random.Next(6) == 0 ? byte.MaxValue : (byte)random.Next(0, 6);
        if (type == typeof(sbyte)) return (sbyte)random.Next(-3, 5);
        if (type == typeof(bool)) return random.Next(2) == 0;
        if (type == typeof(float))
            return random.Next(8) switch { 0 => float.NaN, 1 => float.PositiveInfinity, 2 => float.NegativeInfinity, 3 => 1e9f, 4 => -1e9f, _ => (float)(random.NextDouble() * 400 - 200) };
        if (type == typeof(double))
            return random.Next(8) switch { 0 => double.NaN, 1 => double.PositiveInfinity, 2 => -1e12, 3 => 1e12, _ => random.NextDouble() * 1000 };
        if (type == typeof(string)) return Ids[random.Next(Ids.Length)];
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return random.Next(5) == 0 ? Enum.ToObject(type, 200) : values.GetValue(random.Next(values.Length));
        }
        if (depth > 2) return type.IsValueType ? Activator.CreateInstance(type) : null;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)Activator.CreateInstance(type)!;
            var count = random.Next(5) == 0 ? 40 : random.Next(4);
            for (var i = 0; i < count; i++) list.Add(RandomValue(type.GetGenericArguments()[0], random, depth + 1));
            return list;
        }
        if (type.IsClass && type.GetConstructor(Type.EmptyTypes) == null) return null;
        var instance = Activator.CreateInstance(type)!;
        Fill(instance, random, depth + 1);
        return instance;
    }

    private static void Fill(object target, Random random, int depth)
    {
        foreach (var field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsInitOnly) continue;
            var value = RandomValue(field.FieldType, random, depth);
            if (value != null || !field.FieldType.IsValueType) field.SetValue(target, value);
        }
    }

    private static IEnumerable<ItemInstance> AllItems(ServerPlayer player) =>
        player.Bag.Items().Select(i => i.Item)
            .Concat(player.Warehouse.Items().Select(i => i.Item))
            .Concat(player.Equipment.Worn.Select(w => w.Value));

    private static void AssertValid(GameWorld world, string context)
    {
        var uids = new HashSet<long>();
        foreach (var player in world.Players)
        {
            Assert.True(player.Alz >= 0, $"{context}: Alz negativo ({player.Alz})");
            Assert.True(float.IsFinite(player.X) && float.IsFinite(player.Z), $"{context}: posição inválida ({player.X}, {player.Z})");
            Assert.InRange(player.Hp.Current, 0, player.Hp.Max);
            Assert.InRange(player.Mp.Current, 0, player.Mp.Max);
            Assert.True(player.FreeAttributePoints >= 0, $"{context}: pontos de atributo negativos");
            foreach (var item in AllItems(player))
            {
                var definition = ItemCatalog.Find(item.ItemId);
                Assert.True(definition != null, $"{context}: item desconhecido {item.ItemId}");
                Assert.InRange(item.Quantity, 1, definition!.MaxStack);
                Assert.InRange(item.Upgrade, 0, UpgradeRules.MaxLevel);
                Assert.True(uids.Add(item.Uid), $"{context}: item duplicado (uid {item.Uid})");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Mensagens_aleatorias_nao_quebram_nem_estragam_o_estado(int seed)
    {
        var random = new Random(seed);
        var world = new GameWorld(new NullOutbox(), WorldContent.Spawns());
        var players = new List<ServerPlayer>
        {
            world.AddPlayer("Ana", 0), world.AddPlayer("Bia", 0), world.AddPlayer("Caio", 0),
        };
        foreach (var p in players)
        {
            p.Alz = 50_000;
            p.Bag.TryAdd("nucleo_baixo", 20, () => random.NextInt64(1, long.MaxValue));
            p.Bag.TryAdd("pedra_vigor", 2, () => random.NextInt64(1, long.MaxValue));
            p.Bag.TryAdd("chave_cripta", 3, () => random.NextInt64(1, long.MaxValue));
        }
        AssertValid(world, "início");

        var now = 1.0;
        var last = "";
        for (var i = 0; i < 6000; i++)
        {
            var type = ClientMessages[random.Next(ClientMessages.Length)];
            var message = (INetMessage)Activator.CreateInstance(type)!;
            Fill(message, random, 0);
            var player = players[random.Next(players.Count)];
            // De vez em quando o jogador "anda" até um NPC (como faria de verdade), para loja, ferreira e armazém aceitarem.
            if (random.Next(40) == 0)
            {
                var npc = Game.Shared.Progression.NpcCatalog.All[random.Next(Game.Shared.Progression.NpcCatalog.All.Count)];
                player.Movement.Teleport(npc.X, npc.Z - 1.5f, now);
            }
            last = $"{type.Name} #{i}";
            var ex = Record.Exception(() => world.Handle(player.Id, message, now));
            Assert.True(ex == null, $"{last} lançou: {ex}");
            if (i % 5 == 0)
            {
                now += 0.05;
                var tickEx = Record.Exception(() => world.Tick(now));
                Assert.True(tickEx == null, $"tick depois de {last} lançou: {tickEx}");
            }
            if (i % 250 == 0) AssertValid(world, last);
        }
        AssertValid(world, "fim");

        // O fuzz precisa chegar nas regras de verdade, não só ser recusado logo na entrada.
        Assert.True(ClientMessages.Length >= 20, $"só {ClientMessages.Length} tipos de mensagem do cliente");
        Assert.True(players.Any(p => p.Alz != 50_000) || players.Any(p => p.Bag.Count("nucleo_baixo") != 20),
            "nenhuma mensagem mexeu no estado: o fuzz não está exercitando as regras");
    }

    [Fact]
    public void Fuzz_cobre_todas_as_mensagens_do_cliente()
    {
        var names = ClientMessages.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Contains("MoveInput", names);
        Assert.Contains("ShopCommand", names);
        Assert.Contains("TradeCommand", names);
        Assert.Contains("ItemCommand", names);
        Assert.Equal(28, names.Count); // todos do 2 ao 29 (o Hello é tratado à parte, pelo ServerHost)
    }
}
