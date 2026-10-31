using System;
using System.Collections.Generic;
using Game.Shared.Accounts;
using Game.Shared.Progression;
using Game.Shared.Text;

namespace Game.Shared.Items
{
    /// <summary>
    /// Inventário em grade (bolsa ou armazém): posições fixas, itens empilháveis juntam na mesma pilha.
    /// Só o servidor altera o inventário de verdade; o cliente usa a mesma classe para mostrar e prever.
    /// </summary>
    public sealed class ItemContainer
    {
        public const int BagSize = 40;

        private readonly ItemInstance?[] _slots;

        public ItemContainer(int size = BagSize)
        {
            _slots = new ItemInstance?[size];
        }

        public int Size => _slots.Length;

        /// <summary>Cópia independente (para testar uma operação antes de fazê-la de verdade).</summary>
        public ItemContainer Clone()
        {
            var copy = new ItemContainer(_slots.Length);
            for (var i = 0; i < _slots.Length; i++) copy._slots[i] = _slots[i]?.Clone();
            return copy;
        }

        public ItemInstance? this[int slot] => slot >= 0 && slot < _slots.Length ? _slots[slot] : null;

        public int FreeSlots
        {
            get
            {
                var free = 0;
                foreach (var item in _slots) if (item == null) free++;
                return free;
            }
        }

        public IEnumerable<(int Slot, ItemInstance Item)> Items()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null) yield return (i, _slots[i]!);
            }
        }

        /// <summary>Quantidade total de um tipo de item.</summary>
        public int Count(string itemId)
        {
            var total = 0;
            foreach (var item in _slots) if (item != null && item.ItemId == itemId) total += item.Quantity;
            return total;
        }

        /// <summary>Quanto deste item ainda cabe (completando pilhas e usando posições livres).</summary>
        public int SpaceFor(ItemDefinition definition)
        {
            var space = 0;
            foreach (var item in _slots)
            {
                if (item == null) space += definition.MaxStack;
                else if (item.ItemId == definition.Id && definition.MaxStack > 1) space += definition.MaxStack - item.Quantity;
            }
            return space;
        }

        /// <summary>
        /// Adiciona <paramref name="quantity"/> unidades: completa pilhas existentes e depois ocupa posições livres.
        /// Tudo ou nada: se não couber inteiro, não adiciona nada e retorna falso. <paramref name="newUid"/> dá o
        /// id único de cada item novo.
        /// </summary>
        public bool TryAdd(string itemId, int quantity, Func<long> newUid)
        {
            var definition = ItemCatalog.Get(itemId);
            if (quantity <= 0) return true;
            if (SpaceFor(definition) < quantity) return false;

            var left = quantity;
            if (definition.MaxStack > 1)
            {
                foreach (var item in _slots)
                {
                    if (left == 0) break;
                    if (item == null || item.ItemId != itemId) continue;
                    var add = Math.Min(left, definition.MaxStack - item.Quantity);
                    item.Quantity += add;
                    left -= add;
                }
            }
            for (var i = 0; i < _slots.Length && left > 0; i++)
            {
                if (_slots[i] != null) continue;
                var add = Math.Min(left, definition.MaxStack);
                _slots[i] = new ItemInstance { Uid = newUid(), ItemId = itemId, Quantity = add };
                left -= add;
            }
            return true;
        }

        /// <summary>Coloca um item já existente (com seu id e melhoria) na primeira posição livre.</summary>
        public bool TryPlace(ItemInstance item, out int slot)
        {
            for (slot = 0; slot < _slots.Length; slot++)
            {
                if (_slots[slot] != null) continue;
                _slots[slot] = item;
                return true;
            }
            slot = -1;
            return false;
        }

        /// <summary>Coloca um item numa posição específica (carregar do banco). Falso se ocupada ou inválida.</summary>
        public bool TryPut(int slot, ItemInstance item)
        {
            if (slot < 0 || slot >= _slots.Length || _slots[slot] != null) return false;
            _slots[slot] = item;
            return true;
        }

        /// <summary>Tira o item inteiro da posição.</summary>
        public ItemInstance? Take(int slot)
        {
            if (slot < 0 || slot >= _slots.Length) return null;
            var item = _slots[slot];
            _slots[slot] = null;
            return item;
        }

        /// <summary>Remove <paramref name="quantity"/> unidades do tipo, de qualquer pilha. Tudo ou nada.</summary>
        public bool TryRemove(string itemId, int quantity)
        {
            if (quantity <= 0) return true;
            if (Count(itemId) < quantity) return false;
            var left = quantity;
            for (var i = _slots.Length - 1; i >= 0 && left > 0; i--)
            {
                var item = _slots[i];
                if (item == null || item.ItemId != itemId) continue;
                var take = Math.Min(left, item.Quantity);
                item.Quantity -= take;
                left -= take;
                if (item.Quantity == 0) _slots[i] = null;
            }
            return true;
        }

        /// <summary>
        /// Move da posição <paramref name="from"/> para <paramref name="to"/>: posição vazia recebe; mesmo item empilhável
        /// junta até o limite; diferente troca de lugar.
        /// </summary>
        public bool Move(int from, int to)
        {
            if (from == to || from < 0 || to < 0 || from >= _slots.Length || to >= _slots.Length) return false;
            var source = _slots[from];
            if (source == null) return false;
            var target = _slots[to];

            if (target != null && target.ItemId == source.ItemId)
            {
                var max = ItemCatalog.Get(source.ItemId).MaxStack;
                if (max > 1 && target.Quantity < max)
                {
                    var add = Math.Min(source.Quantity, max - target.Quantity);
                    target.Quantity += add;
                    source.Quantity -= add;
                    if (source.Quantity == 0) _slots[from] = null;
                    return true;
                }
            }

            _slots[to] = source;
            _slots[from] = target;
            return true;
        }

        /// <summary>Encontra um item pelo id único.</summary>
        public int SlotOf(long uid)
        {
            for (var i = 0; i < _slots.Length; i++) if (_slots[i]?.Uid == uid) return i;
            return -1;
        }
    }

    /// <summary>Pontos de atributo distribuídos pelo jogador.</summary>
    [Serializable]
    public struct Attributes
    {
        public int Strength;
        public int Dexterity;
        public int Intelligence;

        public int Total => Strength + Dexterity + Intelligence;

        /// <summary>Pontos com que cada classe começa.</summary>
        public static Attributes StartingFor(CharacterClass characterClass) => characterClass switch
        {
            CharacterClass.Arcanist => new Attributes { Strength = 3, Dexterity = 5, Intelligence = 10 },
            CharacterClass.Ranger => new Attributes { Strength = 8, Dexterity = 8, Intelligence = 2 },
            CharacterClass.Guardian => new Attributes { Strength = 11, Dexterity = 5, Intelligence = 2 },
            _ => new Attributes { Strength = 10, Dexterity = 6, Intelligence = 2 },
        };

        public const int PointsPerLevel = 5;

        /// <summary>Pontos livres para distribuir: os ganhos por nível menos os já usados acima do inicial.</summary>
        public static int FreePoints(int level, CharacterClass characterClass, Attributes current) =>
            PointsPerLevel * (Math.Max(1, level) - 1) - (current.Total - StartingFor(characterClass).Total);
    }

    /// <summary>Equipamento vestido: no máximo um item por posição.</summary>
    public sealed class Equipment
    {
        private readonly Dictionary<EquipSlot, ItemInstance> _worn = new Dictionary<EquipSlot, ItemInstance>();

        public ItemInstance? this[EquipSlot slot] => _worn.TryGetValue(slot, out var item) ? item : null;

        public IEnumerable<KeyValuePair<EquipSlot, ItemInstance>> Worn => _worn;

        /// <summary>Nulo se pode usar; senão o motivo.</summary>
        public static LocalText? CanWear(ItemDefinition definition, int level, CharacterClass characterClass)
        {
            if (definition.Kind != ItemKind.Equipment) return new LocalText("equip.not_gear");
            if (level < definition.RequiredLevel) return new LocalText("equip.level", definition.RequiredLevel);
            if (definition.RequiredClass.HasValue && definition.RequiredClass.Value != characterClass)
                return new LocalText("equip.class", Loc.Ref(ClassInfo.NameKey(definition.RequiredClass.Value)));
            return null;
        }

        /// <summary>
        /// Veste o item da posição <paramref name="bagSlot"/> da bolsa; o que estava vestido vai para o lugar dele.
        /// Nulo se deu certo; senão o motivo (nada muda).
        /// </summary>
        public LocalText? Equip(ItemContainer bag, int bagSlot, int level, CharacterClass characterClass)
        {
            var item = bag[bagSlot];
            if (item == null) return new LocalText("equip.empty_slot");
            var definition = ItemCatalog.Get(item.ItemId);
            var reason = CanWear(definition, level, characterClass);
            if (reason != null) return reason;

            bag.Take(bagSlot);
            if (_worn.TryGetValue(definition.Slot, out var previous)) bag.TryPut(bagSlot, previous);
            _worn[definition.Slot] = item;
            return null;
        }

        /// <summary>Tira o item da posição e coloca na bolsa. Nulo se deu certo; senão o motivo.</summary>
        public LocalText? Unequip(EquipSlot slot, ItemContainer bag)
        {
            if (!_worn.TryGetValue(slot, out var item)) return new LocalText("equip.nothing_worn");
            if (!bag.TryPlace(item, out _)) return new LocalText("bag.full");
            _worn.Remove(slot);
            return null;
        }

        /// <summary>Tira o item da posição sem colocar em lugar nenhum (quem chama decide onde ele vai).</summary>
        public bool TryRemove(EquipSlot slot, out ItemInstance? item)
        {
            if (!_worn.TryGetValue(slot, out item)) return false;
            _worn.Remove(slot);
            return true;
        }

        /// <summary>Veste direto (carregar do banco).</summary>
        public void Put(EquipSlot slot, ItemInstance item) => _worn[slot] = item;
    }

    /// <summary>Cálculo dos atributos finais do personagem.</summary>
    public static class StatCalculator
    {
        /// <summary>Atributos de um item com melhoria e opção.</summary>
        public static StatBlock ItemStats(ItemInstance item)
        {
            var definition = ItemCatalog.Get(item.ItemId);
            var stats = definition.Stats + definition.UpgradeStats * UpgradeRules.EffectiveSteps(item.Upgrade);
            var option = ItemCatalog.Find(item.OptionId);
            if (option != null) stats += option.OptionStats;
            return stats;
        }

        /// <summary>O que os pontos de atributo dão.</summary>
        public static StatBlock FromAttributes(Attributes a) => new StatBlock
        {
            AttackPower = 2 * a.Strength,
            MaxHp = 5 * a.Strength,
            CritPercent = 0.2f * a.Dexterity,
            Defense = a.Dexterity,
            MagicPower = 2 * a.Intelligence,
            MaxMp = 4 * a.Intelligence,
        };

        /// <summary>Atributos finais: nível + pontos + tudo o que está vestido. Crítico limitado a 40%.</summary>
        public static StatBlock Compute(int level, Attributes attributes, Equipment equipment)
        {
            var levelStats = CharacterStats.ForLevel(level);
            var total = new StatBlock
            {
                AttackPower = levelStats.AttackPower,
                MagicPower = levelStats.AttackPower,
                Defense = levelStats.Defense,
                MaxHp = levelStats.MaxHp,
                MaxMp = levelStats.MaxMp,
                CritPercent = 5,
            };
            total += FromAttributes(attributes);
            foreach (var worn in equipment.Worn) total += ItemStats(worn.Value);
            total.CritPercent = Math.Min(40f, total.CritPercent);
            return total;
        }
    }
}
