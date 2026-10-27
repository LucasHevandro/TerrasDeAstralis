using Game.Server.Persistence;
using Game.Shared.Accounts;
using Game.Shared.Net;
using Game.Shared.Social;
using Game.Shared.Text;

namespace Game.Server.World;

/// <summary>
/// Amigos (convite que o outro aceita; a amizade fica dos dois lados), bloqueio (some do chat e dos convites de quem
/// bloqueou) e denúncia (guarda o motivo e as últimas mensagens do denunciado para a moderação). Só personagens de
/// conta: convidados não são salvos.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>Convites de amizade pendentes: (quem convidou, quem foi convidado) por id de personagem → prazo.</summary>
    private readonly Dictionary<(int From, int To), double> _friendInvites = new();

    private void HandleRelation(ServerPlayer player, SocialCommand command, double now)
    {
        if (player.IsGuest)
        {
            SystemChat(player, LT("social.guest_no_relations"));
            return;
        }
        var name = (command.TargetName ?? "").Trim();
        switch (command.Action)
        {
            case RelationActionKind.AddFriend:
                InviteFriend(player, name, now);
                break;
            case RelationActionKind.AcceptFriend:
                AnswerFriend(player, name, true, now);
                break;
            case RelationActionKind.DeclineFriend:
                AnswerFriend(player, name, false, now);
                break;
            case RelationActionKind.RemoveFriend:
                RemoveFriend(player, name);
                break;
            case RelationActionKind.Block:
                Block(player, name);
                break;
            case RelationActionKind.Unblock:
                RemoveRelation(player, RelationKind.Blocked, name, "social.unblocked", "social.not_blocked");
                break;
            case RelationActionKind.Report:
                Report(player, name, command.Text, now);
                break;
        }
    }

    /// <summary>Pede amizade: o outro recebe o convite e precisa aceitar. Se ele já tinha convidado, vira amizade na hora.</summary>
    private void InviteFriend(ServerPlayer player, string name, double now)
    {
        var target = FindPlayerByName(name);
        LocalText? error =
            target == null ? LT("social.friend_must_be_online", name) :
            target == player ? LT("social.friend_self") :
            target.IsGuest ? LT("social.friend_guest", target.Name) :
            player.HasRelation(RelationKind.Friend, target.CharacterId) && target.HasRelation(RelationKind.Friend, player.CharacterId)
                ? LT("social.already_friend", target.Name) :
            player.HasRelation(RelationKind.Blocked, target.CharacterId) ? LT("social.unblock_to_add", target.Name) :
            FriendCount(player) >= RelationRules.MaxFriends ? LT("social.friends_full", RelationRules.MaxFriends) :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        if (_friendInvites.TryGetValue((target!.CharacterId, player.CharacterId), out var until) && now < until)
        {
            BecomeFriends(target, player);
            return;
        }
        foreach (var stale in _friendInvites.Where(i => now >= i.Value).Select(i => i.Key).ToList()) _friendInvites.Remove(stale);
        _friendInvites[(player.CharacterId, target.CharacterId)] = now + RelationRules.FriendInviteSeconds;
        SystemChat(player, LT("social.friend_invite_sent", target.Name));
        // Quem bloqueou não fica sabendo (e o convite expira sozinho).
        if (!target.HasBlocked(player))
            _outbox.Send(target.Id, new FriendInvite { InviterName = player.Name, Seconds = RelationRules.FriendInviteSeconds });
    }

    private void AnswerFriend(ServerPlayer player, string name, bool accept, double now)
    {
        var inviter = FindPlayerByName(name);
        var key = inviter != null ? (inviter.CharacterId, player.CharacterId) : (0, 0);
        if (inviter == null || !_friendInvites.TryGetValue(key, out var until) || now >= until)
        {
            _friendInvites.Remove(key);
            SystemChat(player, LT("social.friend_invite_gone", name));
            return;
        }
        _friendInvites.Remove(key);
        if (!accept)
        {
            SystemChat(inviter, LT("social.friend_declined", player.Name));
            return;
        }
        if (FriendCount(player) >= RelationRules.MaxFriends || FriendCount(inviter) >= RelationRules.MaxFriends)
        {
            SystemChat(player, LT("social.friends_full", RelationRules.MaxFriends));
            return;
        }
        BecomeFriends(inviter, player);
    }

    /// <summary>Põe cada um na lista do outro (quem já estava não repete) e avisa os dois.</summary>
    private void BecomeFriends(ServerPlayer a, ServerPlayer b)
    {
        _friendInvites.Remove((a.CharacterId, b.CharacterId));
        _friendInvites.Remove((b.CharacterId, a.CharacterId));
        foreach (var (me, other) in new[] { (a, b), (b, a) })
        {
            me.Relations.RemoveAll(r => r.Kind == RelationKind.Blocked && r.OtherId == other.CharacterId);
            if (!me.HasRelation(RelationKind.Friend, other.CharacterId))
                me.Relations.Add(new RelationRecord(RelationKind.Friend, other.CharacterId, other.Name));
            me.SocialDirty = true;
            SystemChat(me, LT("social.friend_added", other.Name));
        }
    }

    /// <summary>Tira dos dois lados se o outro estiver online (offline, ele continua vendo a pessoa até tirar também).</summary>
    private void RemoveFriend(ServerPlayer player, string name)
    {
        var key = AccountRules.NameKey(name);
        var relation = player.Relations.FirstOrDefault(r => r.Kind == RelationKind.Friend && AccountRules.NameKey(r.OtherName) == key);
        RemoveRelation(player, RelationKind.Friend, name, "social.not_friend_anymore", "social.not_in_friends");
        if (relation == null) return;
        var other = _players.Values.FirstOrDefault(p => p.CharacterId == relation.OtherId);
        if (other != null && other.Relations.RemoveAll(r => r.Kind == RelationKind.Friend && r.OtherId == player.CharacterId) > 0)
        {
            other.SocialDirty = true;
            SystemChat(other, LT("social.removed_you", player.Name));
        }
    }

    private static int FriendCount(ServerPlayer player) => player.Relations.Count(r => r.Kind == RelationKind.Friend);

    private void Block(ServerPlayer player, string name)
    {
        // Online pelo nome; offline só se já for amigo (o id está na lista).
        var target = FindPlayerByName(name);
        var key = AccountRules.NameKey(name);
        var known = player.Relations.FirstOrDefault(r => r.Kind == RelationKind.Friend && AccountRules.NameKey(r.OtherName) == key);
        var (id, display) = target != null ? (target.CharacterId, target.Name) : known != null ? (known.OtherId, known.OtherName) : (0, name);
        LocalText? error =
            target == player ? LT("social.block_self") :
            target == null && known == null ? LT("social.block_must_be_online", name) :
            id == 0 ? LT("social.block_guest", display) :
            player.HasRelation(RelationKind.Blocked, id) ? LT("social.already_blocked", display) :
            player.Relations.Count(r => r.Kind == RelationKind.Blocked) >= RelationRules.MaxBlocked
                ? LT("social.blocked_full", RelationRules.MaxBlocked) :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        player.Relations.RemoveAll(r => r.Kind == RelationKind.Friend && r.OtherId == id);
        player.Relations.Add(new RelationRecord(RelationKind.Blocked, id, display));
        player.SocialDirty = true;
        SystemChat(player, LT("social.blocked", display));
    }

    private void RemoveRelation(ServerPlayer player, RelationKind kind, string name, string doneKey, string missingKey)
    {
        var key = AccountRules.NameKey(name);
        var relation = player.Relations.FirstOrDefault(r => r.Kind == kind && AccountRules.NameKey(r.OtherName) == key);
        if (relation == null)
        {
            SystemChat(player, LT(missingKey, name));
            return;
        }
        player.Relations.Remove(relation);
        player.SocialDirty = true;
        SystemChat(player, LT(doneKey, relation.OtherName));
    }

    private void Report(ServerPlayer player, string name, string reasonText, double now)
    {
        var target = FindPlayerByName(name);
        var reason = ChatRules.Sanitize(reasonText);
        if (reason.Length > RelationRules.MaxReportReason) reason = reason[..RelationRules.MaxReportReason];
        player.ReportTimes.RemoveAll(t => now - t > 3600);
        var targetKey = target != null ? AccountRules.NameKey(target.Name) : "";
        LocalText? error =
            target == null ? LT("social.report_must_be_online", name) :
            target == player ? LT("social.report_self") :
            reason.Length == 0 ? LT("social.report_reason") :
            player.ReportedAt.TryGetValue(targetKey, out var last) && now - last < RelationRules.SameTargetCooldownSeconds
                ? LT("social.report_cooldown", target.Name) :
            player.ReportTimes.Count >= RelationRules.MaxReportsPerHour ? LT("social.report_limit") :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }

        player.ReportTimes.Add(now);
        player.ReportedAt[targetKey] = now;
        var excerpt = string.Join("\n", target!.RecentChat.Select(l => $"[{l.At:0} s · {ChannelName(l.Channel)}] {l.Text}"));
        player.PendingReports.Add(new ReportRecord(
            player.CharacterId, player.Name, target.CharacterId, target.Name, reason, excerpt, DateTimeOffset.UtcNow));
        SystemChat(player, LT("social.report_sent", target.Name));
    }

    /// <summary>Guarda a mensagem original (sem filtro) nas recentes do jogador, para uma eventual denúncia.</summary>
    private static void RememberChat(ServerPlayer player, ChatChannel channel, string text, double now)
    {
        player.RecentChat.Enqueue((now, channel, text));
        while (player.RecentChat.Count > RelationRules.ReportChatLines) player.RecentChat.Dequeue();
    }

    /// <summary>Avisa os amigos (quem tem este personagem na lista) que ele entrou ou saiu, e atualiza a lista deles.</summary>
    private void NotifyWatchers(ServerPlayer player, LocalText? message)
    {
        if (player.IsGuest) return;
        foreach (var other in _players.Values)
        {
            if (other == player || !other.HasRelation(RelationKind.Friend, player.CharacterId)) continue;
            other.SocialDirty = true;
            if (message != null) SystemChat(other, message);
        }
    }

    private void SendSocialLists(ServerPlayer player)
    {
        if (!player.SocialDirty || player.IsGuest) return;
        player.SocialDirty = false;
        var lists = new SocialLists();
        foreach (var relation in player.Relations.OrderBy(r => r.OtherName, StringComparer.OrdinalIgnoreCase))
        {
            if (relation.Kind == RelationKind.Blocked)
            {
                lists.Blocked.Add(relation.OtherName);
                continue;
            }
            var online = _players.Values.FirstOrDefault(p => p.CharacterId == relation.OtherId);
            lists.Friends.Add(new FriendInfo
            {
                Name = online?.Name ?? relation.OtherName, Online = online != null,
                Level = online?.Progress.Level ?? 0, Class = (byte)(online?.Class ?? 0),
            });
        }
        _outbox.Send(player.Id, lists);
    }

    private static string ChannelName(ChatChannel channel) => channel switch
    {
        ChatChannel.Party => "grupo",
        ChatChannel.Whisper => "sussurro",
        _ => "mapa",
    };
}
