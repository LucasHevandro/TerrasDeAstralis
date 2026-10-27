using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Net;
using Game.Shared.Social;
using Game.Shared.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Game.Server.Tests;

/// <summary>Amigos, bloqueio e denúncia: regras no mundo e o que vai para o banco.</summary>
[Collection("Postgres")] // o teste do banco real não roda junto com os outros que recriam o banco
public class RelationWorldTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();

        public List<string> System(int playerId) =>
            To<ChatMessage>(playerId).Where(m => m.Channel == ChatChannel.System).Select(m => m.Display).ToList();
    }

    private long _uid = 1000;
    private long NewUid() => ++_uid;

    private CharacterRecord Record(int id, string name) =>
        AuthService.InitialCharacter(CharacterClass.Blade, NewUid) with { Id = id, AccountId = id, Name = name };

    private (GameWorld World, FakeOutbox Outbox) NewWorld()
    {
        var outbox = new FakeOutbox();
        return (new GameWorld(outbox, Array.Empty<SpawnDefinition>(), uids: new ItemUidAllocator(5000)), outbox);
    }

    private static void Social(GameWorld world, ServerPlayer player, RelationActionKind action, string name, string text = "", double now = 1) =>
        world.Handle(player.Id, new SocialCommand { Action = action, TargetName = name, Text = text }, now);

    [Fact]
    public void Amigo_aparece_online_e_offline_e_avisa_quando_entra()
    {
        var (world, outbox) = NewWorld();
        var ana = world.AddPlayer("", 0, null, Record(1, "Ana"));
        var bia = world.AddPlayer("", 0, null, Record(2, "Bia"));

        // Convidar não basta: a amizade só existe depois que a Bia aceita, e aí vale para as duas.
        Social(world, ana, RelationActionKind.AddFriend, "bia");
        Assert.Contains("Convite de amizade enviado a Bia", outbox.System(ana.Id));
        Assert.Equal("Ana", Assert.Single(outbox.To<FriendInvite>(bia.Id)).InviterName);
        Assert.Empty(ana.Relations);
        Social(world, bia, RelationActionKind.AcceptFriend, "Ana");
        Assert.Contains("Bia agora é seu amigo", outbox.System(ana.Id));
        Assert.Contains("Ana agora é seu amigo", outbox.System(bia.Id));
        Assert.True(bia.HasRelation(RelationKind.Friend, ana.CharacterId));
        world.Tick(1);
        var lists = outbox.To<SocialLists>(ana.Id).Last();
        Assert.Equal(("Bia", true, 1), (lists.Friends[0].Name, lists.Friends[0].Online, lists.Friends[0].Level));

        world.RemovePlayer(bia.Id);
        Assert.Contains("Bia saiu do jogo", outbox.System(ana.Id));
        world.Tick(2);
        Assert.False(outbox.To<SocialLists>(ana.Id).Last().Friends[0].Online);

        var bia2 = world.AddPlayer("", 3, null, Record(2, "Bia") with { Relations = bia.Relations.ToList() });
        Assert.Contains("Bia entrou no jogo", outbox.System(ana.Id));

        Social(world, ana, RelationActionKind.AddFriend, "Bia");
        Assert.Contains("Bia já é seu amigo", outbox.System(ana.Id));
        Social(world, ana, RelationActionKind.AddFriend, "Ninguem");
        Assert.Contains("Ninguem precisa estar online para ser adicionado", outbox.System(ana.Id));

        // Remover tira dos dois lados.
        Social(world, ana, RelationActionKind.RemoveFriend, "BIA");
        Assert.Empty(ana.Relations);
        Assert.DoesNotContain(bia2.Relations, r => r.Kind == RelationKind.Friend);
        Assert.Contains("Ana tirou você da lista de amigos", outbox.System(bia2.Id));
    }

    [Fact]
    public void Convite_de_amizade_recusado_ou_vencido_nao_vale()
    {
        var (world, outbox) = NewWorld();
        var ana = world.AddPlayer("", 0, null, Record(1, "Ana"));
        var bia = world.AddPlayer("", 0, null, Record(2, "Bia"));

        Social(world, ana, RelationActionKind.AddFriend, "Bia", now: 1);
        Social(world, bia, RelationActionKind.DeclineFriend, "Ana", now: 2);
        Assert.Contains("Bia recusou o convite de amizade", outbox.System(ana.Id));
        Social(world, bia, RelationActionKind.AcceptFriend, "Ana", now: 3);
        Assert.Contains("O convite de amizade de Ana não vale mais", outbox.System(bia.Id));

        Social(world, ana, RelationActionKind.AddFriend, "Bia", now: 10);
        Social(world, bia, RelationActionKind.AcceptFriend, "Ana", now: 10 + RelationRules.FriendInviteSeconds + 1);
        Assert.Empty(ana.Relations);
        Assert.Empty(bia.Relations);

        // Os dois convidando: o segundo convite já fecha a amizade.
        Social(world, ana, RelationActionKind.AddFriend, "Bia", now: 100);
        Social(world, bia, RelationActionKind.AddFriend, "Ana", now: 101);
        Assert.True(ana.HasRelation(RelationKind.Friend, bia.CharacterId));
        Assert.True(bia.HasRelation(RelationKind.Friend, ana.CharacterId));
    }

    [Fact]
    public void Bloqueado_some_do_chat_do_sussurro_e_dos_convites()
    {
        var (world, outbox) = NewWorld();
        var ana = world.AddPlayer("", 0, null, Record(1, "Ana"));
        var caio = world.AddPlayer("", 0, null, Record(3, "Caio"));
        var bia = world.AddPlayer("", 0, null, Record(2, "Bia"));

        Social(world, ana, RelationActionKind.AddFriend, "Caio");
        Social(world, ana, RelationActionKind.Block, "Caio");
        Assert.True(ana.HasBlocked(caio));
        Assert.DoesNotContain(ana.Relations, r => r.Kind == RelationKind.Friend); // bloquear tira dos amigos

        world.Handle(caio.Id, new ChatSend { Channel = ChatChannel.Map, Text = "oi geral" }, 2);
        Assert.DoesNotContain(outbox.To<ChatMessage>(ana.Id), m => m.FromName == "Caio");
        Assert.Contains(outbox.To<ChatMessage>(bia.Id), m => m.FromName == "Caio");

        world.Handle(caio.Id, new ChatSend { Channel = ChatChannel.Whisper, Target = "Ana", Text = "oi" }, 2);
        Assert.Contains("Ana não está recebendo suas mensagens", outbox.System(caio.Id));
        world.Handle(ana.Id, new ChatSend { Channel = ChatChannel.Whisper, Target = "Caio", Text = "oi" }, 2);
        Assert.Contains("Desbloqueie Caio para sussurrar", outbox.System(ana.Id));

        world.Handle(caio.Id, new PartyCommand { Action = PartyActionKind.Invite, TargetName = "Ana" }, 2);
        Assert.Contains("Ana não está aceitando convites", outbox.System(caio.Id));
        Assert.Empty(outbox.To<PartyInvite>(ana.Id));

        world.Tick(2);
        Assert.Equal(new[] { "Caio" }, outbox.To<SocialLists>(ana.Id).Last().Blocked);

        Social(world, ana, RelationActionKind.Unblock, "caio");
        Assert.False(ana.HasBlocked(caio));
        world.Handle(caio.Id, new ChatSend { Channel = ChatChannel.Map, Text = "voltei" }, 3);
        Assert.Contains(outbox.To<ChatMessage>(ana.Id), m => m.FromName == "Caio" && m.Text == "voltei");
    }

    [Fact]
    public void Convidado_nao_tem_lista_e_nao_pode_ser_adicionado()
    {
        var (world, outbox) = NewWorld();
        var ana = world.AddPlayer("", 0, null, Record(1, "Ana"));
        var guest = world.AddPlayer("Bot01", 0);

        Social(world, guest, RelationActionKind.AddFriend, "Ana");
        Assert.Contains(outbox.System(guest.Id), t => t.StartsWith("Convidados não têm"));
        Social(world, ana, RelationActionKind.AddFriend, "Bot01");
        Assert.Contains("Bot01 é um convidado e não pode ser adicionado", outbox.System(ana.Id));
    }

    [Fact]
    public void Denuncia_guarda_motivo_e_mensagens_originais_com_limite()
    {
        var (world, outbox) = NewWorld();
        var ana = world.AddPlayer("", 0, null, Record(1, "Ana"));
        var caio = world.AddPlayer("", 0, null, Record(3, "Caio"));
        for (var i = 0; i < 12; i++)
            world.Handle(caio.Id, new ChatSend { Channel = ChatChannel.Map, Text = $"merda {i}" }, 10 + i * 2);

        Social(world, ana, RelationActionKind.Report, "Caio", "xingando no mapa", now: 40);
        Assert.Contains("Denúncia de Caio enviada para a moderação. Obrigado!", outbox.System(ana.Id));
        var report = Assert.Single(ana.PendingReports);
        Assert.Equal((1, 3, "Caio", "xingando no mapa"), (report.ReporterId, report.TargetId, report.TargetName, report.Reason));
        var lines = report.ChatExcerpt.Split('\n');
        Assert.Equal(RelationRules.ReportChatLines, lines.Length);
        Assert.EndsWith("merda 11", lines[^1]); // texto original, sem os asteriscos do filtro

        Social(world, ana, RelationActionKind.Report, "Caio", "de novo", now: 60);
        Assert.Contains("Você já denunciou Caio há pouco tempo", outbox.System(ana.Id));
        Social(world, ana, RelationActionKind.Report, "Caio", "", now: 60 + RelationRules.SameTargetCooldownSeconds);
        Assert.Contains("Diga o motivo da denúncia", outbox.System(ana.Id));

        // Vai para o banco no próximo salvamento.
        var (snapshot, _) = world.CaptureForSave(ana);
        Assert.Single(snapshot.Reports);
        Assert.Empty(ana.PendingReports);
    }

    [Fact]
    public async Task Amigos_bloqueios_e_denuncias_sao_salvos_e_voltam()
    {
        var store = new InMemoryGameStore();
        await RoundTrip(store);
    }

    [Fact]
    public async Task Amigos_bloqueios_e_denuncias_no_banco_real()
    {
        var connection = Environment.GetEnvironmentVariable("GAME_DB_TEST");
        if (string.IsNullOrEmpty(connection)) return;
        Assert.Contains("test", connection, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(connection).Options;
        await using (var db = new GameDbContext(options)) await db.Database.EnsureDeletedAsync();
        var store = new EfGameStore(new PooledDbContextFactory<GameDbContext>(options));
        await store.MigrateAsync();
        await RoundTrip(store);
    }

    private async Task RoundTrip(IGameStore store)
    {
        var account = await store.CreateAccountAsync("lucas", "hash", "");
        var ana = await store.CreateCharacterAsync(account.Id, "Ana", CharacterClass.Blade, AuthService.InitialCharacter(CharacterClass.Blade, NewUid));
        var bia = await store.CreateCharacterAsync(account.Id, "Bia", CharacterClass.Blade, AuthService.InitialCharacter(CharacterClass.Blade, NewUid));
        var caio = await store.CreateCharacterAsync(account.Id, "Caio", CharacterClass.Blade, AuthService.InitialCharacter(CharacterClass.Blade, NewUid));

        var (world, _) = NewWorld();
        var pa = world.AddPlayer("", 0, null, ana);
        var pb = world.AddPlayer("", 0, null, bia);
        var pc = world.AddPlayer("", 0, null, caio);
        world.Handle(pc.Id, new ChatSend { Channel = ChatChannel.Map, Text = "spam spam" }, 1);
        Social(world, pa, RelationActionKind.AddFriend, "Bia");
        Social(world, pb, RelationActionKind.AcceptFriend, "Ana");
        Social(world, pa, RelationActionKind.Block, "Caio");
        Social(world, pa, RelationActionKind.Report, "Caio", "spam no mapa", now: 2);
        var (snapshot, logs) = world.CaptureForSave(pa);
        await store.SaveCharacterAsync(snapshot, logs);

        var loaded = (await store.LoadCharacterAsync(ana.Id))!;
        Assert.Equal(2, loaded.Relations.Count);
        Assert.Contains(loaded.Relations, r => r.Kind == RelationKind.Friend && r.OtherId == bia.Id && r.OtherName == "Bia");
        Assert.Contains(loaded.Relations, r => r.Kind == RelationKind.Blocked && r.OtherId == caio.Id);
        var report = Assert.Single(await store.ReportsAsync());
        Assert.Equal(("Ana", "Caio", "spam no mapa"), (report.ReporterName, report.TargetName, report.Reason));
        Assert.Contains("spam spam", report.ChatExcerpt);

        // Desfazer também chega ao banco.
        Social(world, pa, RelationActionKind.RemoveFriend, "Bia");
        (snapshot, logs) = world.CaptureForSave(pa);
        await store.SaveCharacterAsync(snapshot, logs);
        loaded = (await store.LoadCharacterAsync(ana.Id))!;
        Assert.Equal(RelationKind.Blocked, Assert.Single(loaded.Relations).Kind);

        // Ao entrar de novo, o bloqueio continua valendo.
        world.RemovePlayer(pa.Id);
        var again = world.AddPlayer("", 5, null, loaded);
        Assert.True(again.HasBlocked(pc));
    }
}
