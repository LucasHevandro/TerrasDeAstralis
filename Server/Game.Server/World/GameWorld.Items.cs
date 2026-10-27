using Game.Server.Persistence;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;

namespace Game.Server.World;

/// <summary>Itens, melhoria, atributos, NPCs, missões e Despertar — tudo decidido aqui, com as regras do Shared.</summary>
public sealed partial class GameWorld
{
    // ===== Inventário =====

    private void HandleItemCommand(ServerPlayer player, ItemCommand command, double now)
    {
        var reason = command.Action switch
        {
            ItemActionKind.Move => MoveItem(player, command),
            ItemActionKind.Discard => DiscardItem(player, command),
            ItemActionKind.Use => UseItem(player, command, now),
            _ => LT("item.unknown_action"),
        };
        if (reason != null) Notify(player, reason);
        // Mesmo quando recusa, reenvia: o cliente pode ter previsto e precisa voltar ao estado certo.
        player.InventoryDirty = true;
    }

    private LocalText? MoveItem(ServerPlayer player, ItemCommand c)
    {
        if (c.From == ItemContainerKind.Bag && c.To == ItemContainerKind.Equipment)
        {
            var item = player.Bag[c.FromSlot];
            if (item == null) return LT("equip.empty_slot");
            if ((int)ItemCatalog.Get(item.ItemId).Slot != c.ToSlot && ItemCatalog.Get(item.ItemId).Kind == ItemKind.Equipment)
                return LT("item.wrong_slot");
            var reason = player.Equipment.Equip(player.Bag, c.FromSlot, player.Progress.Level, player.Class);
            if (reason == null) player.RecalculateStats();
            return reason;
        }

        if (c.From == ItemContainerKind.Equipment && c.To == ItemContainerKind.Bag)
        {
            var slot = (EquipSlot)c.FromSlot;
            var worn = player.Equipment[slot];
            if (worn == null) return LT("equip.nothing_worn");
            var target = player.Bag[c.ToSlot];
            if (target != null)
            {
                // Soltou em cima de outro item: troca se o outro puder ser vestido aí.
                var definition = ItemCatalog.Get(target.ItemId);
                if (definition.Kind != ItemKind.Equipment || definition.Slot != slot) return LT("item.drop_on_empty");
                var swap = player.Equipment.Equip(player.Bag, c.ToSlot, player.Progress.Level, player.Class);
                if (swap == null) player.RecalculateStats();
                return swap;
            }
            if (!player.Equipment.TryRemove(slot, out var removed) || !player.Bag.TryPut(c.ToSlot, removed!))
            {
                if (removed != null) player.Equipment.Put(slot, removed);
                return LT("item.bad_slot");
            }
            player.RecalculateStats();
            return null;
        }

        var from = ContainerOf(player, c.From);
        var to = ContainerOf(player, c.To);
        if (from == null || to == null) return LT("item.bad_move");
        if ((c.From == ItemContainerKind.Warehouse || c.To == ItemContainerKind.Warehouse) && !NearNpc(player, NpcRole.Warehouse))
            return LT("storage.go_to");
        if (from == to) return from.Move(c.FromSlot, c.ToSlot) ? null : LT("item.bad_move");
        return Transfer(from, c.FromSlot, to, c.ToSlot) ? null : LT("item.bad_move");
    }

    /// <summary>Move entre dois lugares diferentes: posição vazia recebe, mesmo item empilhável junta, diferente troca.</summary>
    private static bool Transfer(ItemContainer from, int fromSlot, ItemContainer to, int toSlot)
    {
        var source = from[fromSlot];
        if (source == null || toSlot < 0 || toSlot >= to.Size) return false;
        var target = to[toSlot];
        if (target == null)
        {
            from.Take(fromSlot);
            return to.TryPut(toSlot, source);
        }

        var max = ItemCatalog.Get(source.ItemId).MaxStack;
        if (target.ItemId == source.ItemId && max > 1)
        {
            var add = Math.Min(source.Quantity, max - target.Quantity);
            if (add <= 0) return false;
            target.Quantity += add;
            source.Quantity -= add;
            if (source.Quantity == 0) from.Take(fromSlot);
            return true;
        }

        from.Take(fromSlot);
        to.Take(toSlot);
        from.TryPut(fromSlot, target);
        to.TryPut(toSlot, source);
        return true;
    }

    private LocalText? DiscardItem(ServerPlayer player, ItemCommand c)
    {
        if (c.From != ItemContainerKind.Bag) return LT("item.discard_bag_only");
        var item = player.Bag[c.FromSlot];
        if (item == null) return LT("equip.empty_slot");
        var quantity = c.Quantity <= 0 || c.Quantity >= item.Quantity ? item.Quantity : c.Quantity;
        if (quantity == item.Quantity) player.Bag.Take(c.FromSlot);
        else item.Quantity -= quantity;
        Log(player, "descarte", item.ItemId, quantity, 0, $"uid {item.Uid} +{item.Upgrade}");
        return null;
    }

    private LocalText? UseItem(ServerPlayer player, ItemCommand c, double now)
    {
        if (c.From != ItemContainerKind.Bag) return LT("item.use_bag_only");
        var item = player.Bag[c.FromSlot];
        if (item == null) return LT("equip.empty_slot");
        var definition = ItemCatalog.Get(item.ItemId);
        if (!definition.IsPotion) return LT("item.not_usable");
        if (player.IsDead) return LT("player.you_are_dead");
        var mana = definition.HealMp > 0;
        if (now < (mana ? player.NextManaPotionAt : player.NextPotionAt)) return LT("item.potion_wait");
        if (mana ? player.Mp.Current >= player.Mp.Max : player.Hp.Current >= player.Hp.Max)
            return LT(mana ? "item.mp_full" : "item.hp_full");

        Drink(player, c.FromSlot, item, definition, now);
        return null;
    }

    private static void Drink(ServerPlayer player, int slot, ItemInstance item, ItemDefinition definition, double now)
    {
        if (definition.HealMp > 0)
        {
            player.NextManaPotionAt = now + ServerPlayer.PotionCooldownSeconds;
            player.Mp.Add(definition.HealMp);
        }
        else
        {
            player.NextPotionAt = now + ServerPlayer.PotionCooldownSeconds;
            player.Hp.Add(definition.HealHp);
        }
        if (--item.Quantity == 0) player.Bag.Take(slot);
        player.StatsDirty = true;
    }

    /// <summary>
    /// Faltou MP para a skill: bebe a menor poção de mana da bolsa que cobre o que falta (ou a maior que houver, se
    /// nenhuma cobre). Falso se não há poção, se ela está em recarga ou se nem bebendo dá para pagar a skill.
    /// </summary>
    private bool DrinkManaFor(ServerPlayer player, int cost, double now)
    {
        if (now < player.NextManaPotionAt) return false;
        var missing = cost - player.Mp.Current;
        var potions = Enumerable.Range(0, player.Bag.Size)
            .Select(slot => (Slot: slot, Item: player.Bag[slot]))
            .Where(p => p.Item != null && ItemCatalog.Find(p.Item.ItemId) is { HealMp: > 0 })
            .Select(p => (p.Slot, Item: p.Item!, Definition: ItemCatalog.Get(p.Item!.ItemId)))
            .ToList();
        if (potions.Count == 0) return false;
        var covering = potions.Where(p => p.Definition.HealMp >= missing).ToList();
        var chosen = covering.Count > 0
            ? covering.OrderBy(p => p.Definition.HealMp).First()
            : potions.OrderByDescending(p => p.Definition.HealMp).First();
        if (player.Mp.Current + chosen.Definition.HealMp < cost) return false;
        Drink(player, chosen.Slot, chosen.Item, chosen.Definition, now);
        Notify(player, LT("item.auto_mana", Loc.Ref("item." + chosen.Item.ItemId)));
        return true;
    }

    private static ItemContainer? ContainerOf(ServerPlayer player, ItemContainerKind kind) => kind switch
    {
        ItemContainerKind.Bag => player.Bag,
        ItemContainerKind.Warehouse => player.Warehouse,
        _ => null,
    };

    /// <summary>Item da bolsa ou vestido (o que a ferreira aceita).</summary>
    private static ItemInstance? ItemAt(ServerPlayer player, ItemContainerKind container, int slot) => container switch
    {
        ItemContainerKind.Bag => player.Bag[slot],
        ItemContainerKind.Equipment when Enum.IsDefined(typeof(EquipSlot), slot) => player.Equipment[(EquipSlot)slot],
        _ => null,
    };

    // ===== Ferreira =====

    private void HandleUpgrade(ServerPlayer player, UpgradeItem message)
    {
        var item = ItemAt(player, message.Container, message.Slot);
        LocalText? refused = !NearNpc(player, NpcRole.Smith) ? LT("smith.go_to")
            : item == null ? LT("smith.pick_item")
            : null;
        if (refused == null) refused = UpgradeRules.CanTry(item!, player.Bag, player.Alz);
        if (refused != null)
        {
            _outbox.Send(player.Id, new UpgradeResult { Outcome = (byte)UpgradeOutcome.Refused, Uid = item?.Uid ?? 0, Message = refused });
            return;
        }

        var before = item!.Upgrade;
        var cost = UpgradeRules.CostFor(ItemCatalog.Get(item.ItemId), before);
        var alz = player.Alz;
        var outcome = UpgradeRules.Try(item, player.Bag, ref alz, _random);
        var spent = player.Alz - alz;
        player.Alz = alz;
        player.InventoryDirty = true;
        player.StatsDirty = true;
        if (message.Container == ItemContainerKind.Equipment) player.RecalculateStats();

        var name = Loc.Ref("item." + item.ItemId);
        var text = outcome switch
        {
            UpgradeOutcome.Success => LT("upgrade.success", name, item.Upgrade),
            UpgradeOutcome.FailedKept => LT("upgrade.failed_kept", name, item.Upgrade),
            _ => LT("upgrade.failed_down", name, item.Upgrade),
        };
        Log(player, "melhoria", item.ItemId, cost.Cores, -spent, $"uid {item.Uid} +{before} → +{item.Upgrade} ({outcome})");
        _outbox.Send(player.Id, new UpgradeResult { Outcome = (byte)outcome, Uid = item.Uid, NewLevel = (byte)item.Upgrade, Message = text });
        if (outcome == UpgradeOutcome.Success && item.Upgrade >= 8)
        {
            foreach (var other in _players.Values) Notify(other, LT("upgrade.broadcast", player.Name, name, item.Upgrade), announcement: true);
        }
    }

    private void HandleApplyOption(ServerPlayer player, ApplyOption message)
    {
        var item = ItemAt(player, message.Container, message.Slot);
        var stone = player.Bag[message.StoneSlot];
        var reason = !NearNpc(player, NpcRole.Smith) ? LT("smith.go_to")
            : item == null ? LT("smith.pick_item")
            : stone == null ? LT("smith.pick_stone")
            : UpgradeRules.ApplyOption(item, player.Bag, stone.ItemId);
        if (reason != null)
        {
            Notify(player, reason);
            return;
        }
        player.InventoryDirty = true;
        if (message.Container == ItemContainerKind.Equipment) player.RecalculateStats();
        Log(player, "opcao", item!.ItemId, 1, 0, $"uid {item.Uid} recebeu {item.OptionId}");
        Notify(player, LT("option.applied", Loc.Ref("item." + item.ItemId), Loc.Ref("item." + item.OptionId)));
    }

    // ===== Atributos e Despertar =====

    private void HandleAllocate(ServerPlayer player, AllocateAttributes message)
    {
        var total = message.Strength + message.Dexterity + message.Intelligence;
        if (total == 0) return;
        if (total > player.FreeAttributePoints)
        {
            Notify(player, LT("attr.not_enough_points", player.FreeAttributePoints));
            player.SheetDirty = true;
            return;
        }
        var a = player.Attributes;
        a.Strength += message.Strength;
        a.Dexterity += message.Dexterity;
        a.Intelligence += message.Intelligence;
        player.Attributes = a;
        player.RecalculateStats();
    }

    private void HandleAwakening(ServerPlayer player, double now)
    {
        if (player.IsDead) return;
        if (!player.Awakening.TryActivate(now))
        {
            Notify(player, player.Awakening.IsActive(now) ? LT("awaken.already_active") : LT("awaken.not_full"));
            return;
        }
        player.SheetDirty = true;
        Notify(player, LT("awaken.activated", AwakeningState.DamageBonus, AwakeningState.DurationSeconds), announcement: true);
    }

    // ===== NPCs e missões =====

    private void HandleTalk(ServerPlayer player, TalkToNpc message)
    {
        var npc = NpcCatalog.Find(message.NpcId);
        if (npc == null) return;
        if (!NpcCatalog.InRange(npc, player.X, player.Z))
        {
            Notify(player, LT("npc.come_closer", Loc.Ref("npc." + npc.Id)));
            return;
        }
        if (player.Quests.OnTalk(npc.Id).Count > 0) player.QuestsDirty = true;
        _outbox.Send(player.Id, DialogMessage(player, npc));
        if (npc.Role == NpcRole.DungeonPortal)
            foreach (var id in Game.Shared.Dungeon.DungeonCatalog.Ids) _outbox.Send(player.Id, InfoMessage(id));
        if (npc.Role == NpcRole.Market) SendMarket(player);
    }

    private NpcDialog DialogMessage(ServerPlayer player, NpcDefinition npc)
    {
        var dialog = new NpcDialog { NpcId = npc.Id };
        dialog.Offered.AddRange(player.Quests.AvailableFrom(npc.Id, player.Progress.Level).Select(q => q.Id));
        dialog.ReadyToTurnIn.AddRange(player.Quests.Active
            .Where(p => QuestCatalog.Get(p.QuestId).GiverNpcId == npc.Id && QuestLog.IsReady(p, player.Bag))
            .Select(p => p.QuestId));
        return dialog;
    }

    private void HandleQuest(ServerPlayer player, QuestCommand message)
    {
        var quest = QuestCatalog.Find(message.QuestId);
        if (quest == null) return;
        var giver = NpcCatalog.Find(quest.GiverNpcId);
        var needsGiver = message.Action != QuestActionKind.Abandon;
        if (needsGiver && giver != null && !NpcCatalog.InRange(giver, player.X, player.Z))
        {
            Notify(player, LT("npc.talk_to", Loc.Ref("npc." + giver.Id)));
            return;
        }

        LocalText? reason;
        switch (message.Action)
        {
            case QuestActionKind.Accept:
                reason = player.Quests.Accept(quest.Id, player.Progress.Level);
                if (reason == null) Notify(player, LT("quest.accepted", Loc.Ref("quest." + quest.Id)));
                break;
            case QuestActionKind.TurnIn:
                reason = player.Quests.TurnIn(quest.Id, player.Bag, _uids.Next, out var reward);
                if (reason == null)
                {
                    player.Alz += reward.Alz;
                    player.Session.Quests++;
                    player.InventoryDirty = true;
                    player.SheetDirty = true; // pode ter liberado skills ou o combo
                    Log(player, "missao", null, 0, reward.Alz, $"{quest.Id}: +{reward.Xp} XP, itens {string.Join(", ", quest.RewardItems.Select(i => $"{i.ItemId} x{i.Quantity}"))}");
                    Notify(player, quest.Unlocks.Length > 0
                        ? LT("quest.completed_unlocks", Loc.Ref("quest." + quest.Id), Loc.Ref("quest." + quest.Id + ".unlocks"))
                        : LT("quest.completed", Loc.Ref("quest." + quest.Id)), announcement: true);
                    GrantXp(player, reward.Xp);
                }
                break;
            default:
                reason = player.Quests.Abandon(quest.Id) ? null : LT("quest.not_active");
                break;
        }

        if (reason != null) Notify(player, reason);
        player.QuestsDirty = true;
        if (giver != null && needsGiver) _outbox.Send(player.Id, DialogMessage(player, giver));
    }

    private static bool NearNpc(ServerPlayer player, NpcRole role) =>
        NpcCatalog.All.Any(n => n.Role == role && NpcCatalog.InRange(n, player.X, player.Z));

    private static void Log(ServerPlayer player, string kind, string? itemId, int quantity, long alzDelta, string details)
    {
        if (player.CharacterId == 0) return;
        player.PendingLogs.Add(new ItemLogEntry(player.CharacterId, DateTimeOffset.UtcNow, kind, itemId, quantity, alzDelta, details));
    }
}
