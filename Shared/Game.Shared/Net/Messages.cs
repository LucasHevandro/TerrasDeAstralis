using System.Collections.Generic;
using Game.Shared.Text;

namespace Game.Shared.Net
{
    /// <summary>Tipos de mensagem. 1–99: cliente → servidor. 100+: servidor → cliente. Nunca reaproveitar números.</summary>
    public enum MessageType : ushort
    {
        // Cliente → servidor
        Hello = 1,
        MoveInput = 2,
        UseSkill = 3,
        StartCombo = 4,
        Finisher = 5,
        PickupLoot = 6,
        Ping = 7,
        ItemCommand = 8,
        UpgradeItem = 9,
        ApplyOption = 10,
        AllocateAttributes = 11,
        TalkToNpc = 12,
        QuestCommand = 13,
        ActivateAwakening = 14,
        PartyCommand = 15,
        ChatSend = 16,
        SocialCommand = 17,
        ShopCommand = 18,
        TradeCommand = 19,
        DungeonCommand = 20,
        DuelCommand = 21,
        ArenaCommand = 22,
        GmCommand = 23,
        ClientReport = 24,
        LearnSkill = 25,
        SetHotbar = 26,
        AppearanceCommand = 27,
        MarketCommand = 28,
        SetResting = 29,
        LootRollChoose = 30,

        // Servidor → cliente
        Welcome = 100,
        Reject = 101,
        EntitySpawned = 102,
        EntityDespawned = 103,
        Snapshot = 104,
        CombatEvent = 105,
        ComboState = 106,
        PlayerStats = 107,
        LootSpawned = 108,
        LootRemoved = 109,
        PositionCorrection = 110,
        Notice = 111,
        Pong = 112,
        AreaWarning = 113,
        Inventory = 114,
        CharacterSheet = 115,
        QuestState = 116,
        NpcDialog = 117,
        UpgradeResult = 118,
        PartyState = 119,
        PartyInvite = 120,
        ChatMessage = 121,
        SocialLists = 122,
        TradeState = 123,
        TradeInvite = 124,
        DungeonState = 125,
        DungeonInfo = 126,
        DuelInvite = 127,
        PvpState = 128,
        EntityLook = 129,
        MarketState = 130,
        FriendInvite = 131,
        LootRollStart = 132,
    }

    /// <summary>Uma mensagem do protocolo. Cada tipo sabe se escrever; a leitura fica em <see cref="NetCodec"/>.</summary>
    public interface INetMessage
    {
        MessageType Type { get; }
        void Write(PacketWriter writer);
    }

    /// <summary>
    /// Versão do protocolo. Subir sempre que uma mensagem mudar de formato ou de sentido (20: itens na barra; 21: convite
    /// de amizade, ameaça e descanso na fogueira).
    /// </summary>
    public static class Protocol
    {
        public const int Version = 21;
        public const int DefaultPort = 7777;
    }

    /// <summary>Tipo de entidade vista pelo cliente.</summary>
    public enum EntityKind : byte
    {
        Player = 0,
        Monster = 1,
    }

    /// <summary>Quem causou um evento de combate quando não foi uma skill numerada.</summary>
    public static class CombatSource
    {
        public const byte Finisher = 254;
        public const byte MonsterAttack = 253;
        public const byte BossArea = 252;
    }

    // ===== Cliente → servidor =====

    /// <summary>
    /// Primeira mensagem do cliente. Com conta: <see cref="Token"/> (da API de login) e <see cref="CharacterId"/>.
    /// Sem token, só entra se o servidor estiver aceitando convidados (testes e bots), com <see cref="Name"/>.
    /// </summary>
    public sealed class Hello : INetMessage
    {
        public MessageType Type => MessageType.Hello;
        public int ProtocolVersion = Protocol.Version;
        public string Name = "";
        public string Token = "";
        public int CharacterId;

        public void Write(PacketWriter w)
        {
            w.WriteInt(ProtocolVersion);
            w.WriteString(Name);
            w.WriteString(Token);
            w.WriteInt(CharacterId);
        }

        public static Hello Read(PacketReader r)
        {
            // A versão vem primeiro e nunca muda de lugar: o servidor sempre consegue recusar um cliente antigo com motivo.
            var hello = new Hello { ProtocolVersion = r.ReadInt() };
            if (hello.ProtocolVersion != Protocol.Version) return hello;
            hello.Name = r.ReadString();
            hello.Token = r.ReadString();
            hello.CharacterId = r.ReadInt();
            return hello;
        }
    }

    /// <summary>Posição do jogador (o servidor valida a velocidade).</summary>
    public sealed class MoveInput : INetMessage
    {
        public MessageType Type => MessageType.MoveInput;
        public uint Sequence;
        public float X;
        public float Z;
        public float Yaw;

        /// <summary>O jogador começou uma esquiva (o servidor limita pela recarga).</summary>
        public bool Dodging;

        public void Write(PacketWriter w)
        {
            w.WriteUInt(Sequence);
            w.WriteFloat(X);
            w.WriteFloat(Z);
            w.WriteFloat(Yaw);
            w.WriteBool(Dodging);
        }

        public static MoveInput Read(PacketReader r) => new MoveInput
        {
            Sequence = r.ReadUInt(), X = r.ReadFloat(), Z = r.ReadFloat(), Yaw = r.ReadFloat(), Dodging = r.ReadBool(),
        };
    }

    /// <summary>
    /// Pedido de skill. <see cref="ClientTime"/> é o instante do aperto no relógio sincronizado com o servidor:
    /// o servidor usa para julgar o timing do combo descontando a latência (com limite).
    /// </summary>
    public sealed class UseSkill : INetMessage
    {
        public MessageType Type => MessageType.UseSkill;
        public byte Slot;
        public int TargetId;
        public double ClientTime;

        public void Write(PacketWriter w)
        {
            w.WriteByte(Slot);
            w.WriteInt(TargetId);
            w.WriteDouble(ClientTime);
        }

        public static UseSkill Read(PacketReader r) =>
            new UseSkill { Slot = r.ReadByte(), TargetId = r.ReadInt(), ClientTime = r.ReadDouble() };
    }

    public sealed class StartCombo : INetMessage
    {
        public MessageType Type => MessageType.StartCombo;
        public int TargetId;
        public double ClientTime;

        public void Write(PacketWriter w)
        {
            w.WriteInt(TargetId);
            w.WriteDouble(ClientTime);
        }

        public static StartCombo Read(PacketReader r) => new StartCombo { TargetId = r.ReadInt(), ClientTime = r.ReadDouble() };
    }

    public sealed class Finisher : INetMessage
    {
        public MessageType Type => MessageType.Finisher;
        public int TargetId;

        public void Write(PacketWriter w) => w.WriteInt(TargetId);

        public static Finisher Read(PacketReader r) => new Finisher { TargetId = r.ReadInt() };
    }

    public sealed class PickupLoot : INetMessage
    {
        public MessageType Type => MessageType.PickupLoot;
        public int LootId;

        public void Write(PacketWriter w) => w.WriteInt(LootId);

        public static PickupLoot Read(PacketReader r) => new PickupLoot { LootId = r.ReadInt() };
    }

    /// <summary>Medição de latência e sincronia de relógio.</summary>
    public sealed class Ping : INetMessage
    {
        public MessageType Type => MessageType.Ping;
        public double ClientTime;

        public void Write(PacketWriter w) => w.WriteDouble(ClientTime);

        public static Ping Read(PacketReader r) => new Ping { ClientTime = r.ReadDouble() };
    }

    // ===== Servidor → cliente =====

    public sealed class Welcome : INetMessage
    {
        public MessageType Type => MessageType.Welcome;
        public int PlayerId;
        public double ServerTime;
        public byte TickRate;
        public float SpawnX;
        public float SpawnZ;

        public void Write(PacketWriter w)
        {
            w.WriteInt(PlayerId);
            w.WriteDouble(ServerTime);
            w.WriteByte(TickRate);
            w.WriteFloat(SpawnX);
            w.WriteFloat(SpawnZ);
        }

        public static Welcome Read(PacketReader r) => new Welcome
        {
            PlayerId = r.ReadInt(), ServerTime = r.ReadDouble(), TickRate = r.ReadByte(), SpawnX = r.ReadFloat(), SpawnZ = r.ReadFloat(),
        };
    }

    public sealed class Reject : INetMessage
    {
        public MessageType Type => MessageType.Reject;
        public LocalText Reason = LocalText.Empty;

        public void Write(PacketWriter w) => Reason.Write(w);

        public static Reject Read(PacketReader r) => new Reject { Reason = LocalText.Read(r) };
    }

    /// <summary>Dados fixos de uma entidade, enviados uma vez quando ela aparece para o cliente.</summary>
    public sealed class EntitySpawned : INetMessage
    {
        public MessageType Type => MessageType.EntitySpawned;
        public int Id;
        public EntityKind Kind;

        /// <summary>Id do tipo (ex.: "lobo_cinzento"); para jogadores, a classe ("blade" ou "arcanist").</summary>
        public string TypeId = "";

        public string Name = "";
        public int Level;
        public int MaxHp;
        public float X;
        public float Z;

        /// <summary>Jogadores: arma na mão (id do item; vazio = sem arma) e nível de melhoria (o brilho dela).</summary>
        public string WeaponId = "";

        public byte WeaponUpgrade;

        /// <summary>
        /// Jogadores: grau da couraça, das luvas e das botas vestidas (cor do metal do corpo, dos braços e das pernas) e as
        /// cores de cabelo e roupa, o penteado e o tom de pele da criação, e se o elmo está escondido.
        /// </summary>
        public byte ArmorGrade;
        public byte GlovesGrade;
        public byte BootsGrade;
        public byte Hair;
        public byte Cloth;
        public byte Head;
        public byte Skin;
        public bool HideHelmet;

        /// <summary>Monstros: elite (maior, dourado e com "(Elite)" no nome).</summary>
        public bool Elite;

        public void Write(PacketWriter w)
        {
            w.WriteInt(Id);
            w.WriteByte((byte)Kind);
            w.WriteString(TypeId);
            w.WriteString(Name);
            w.WriteInt(Level);
            w.WriteInt(MaxHp);
            w.WriteFloat(X);
            w.WriteFloat(Z);
            w.WriteString(WeaponId);
            w.WriteByte(WeaponUpgrade);
            w.WriteByte(ArmorGrade);
            w.WriteByte(GlovesGrade);
            w.WriteByte(BootsGrade);
            w.WriteByte(Hair);
            w.WriteByte(Cloth);
            w.WriteByte(Head);
            w.WriteByte(Skin);
            w.WriteBool(HideHelmet);
            w.WriteBool(Elite);
        }

        public static EntitySpawned Read(PacketReader r) => new EntitySpawned
        {
            Id = r.ReadInt(), Kind = (EntityKind)r.ReadByte(), TypeId = r.ReadString(), Name = r.ReadString(),
            Level = r.ReadInt(), MaxHp = r.ReadInt(), X = r.ReadFloat(), Z = r.ReadFloat(),
            WeaponId = r.ReadString(), WeaponUpgrade = r.ReadByte(),
            ArmorGrade = r.ReadByte(), GlovesGrade = r.ReadByte(), BootsGrade = r.ReadByte(), Hair = r.ReadByte(), Cloth = r.ReadByte(),
            Head = r.ReadByte(), Skin = r.ReadByte(), HideHelmet = r.ReadBool(), Elite = r.ReadBool(),
        };
    }

    /// <summary>
    /// A aparência de um jogador mudou (trocou de arma ou de armadura, melhorou a arma ou mostrou/escondeu o elmo): quem
    /// o vê troca o modelo, o brilho e a cor do metal.
    /// Mandado a quem já conhece o jogador (o EntitySpawned leva a aparência de quem acaba de aparecer).
    /// </summary>
    public sealed class EntityLook : INetMessage
    {
        public MessageType Type => MessageType.EntityLook;
        public int Id;
        public string WeaponId = "";
        public byte WeaponUpgrade;
        public byte ArmorGrade;
        public byte GlovesGrade;
        public byte BootsGrade;
        public bool HideHelmet;

        public void Write(PacketWriter w)
        {
            w.WriteInt(Id);
            w.WriteString(WeaponId);
            w.WriteByte(WeaponUpgrade);
            w.WriteByte(ArmorGrade);
            w.WriteByte(GlovesGrade);
            w.WriteByte(BootsGrade);
            w.WriteBool(HideHelmet);
        }

        public static EntityLook Read(PacketReader r) => new EntityLook
        {
            Id = r.ReadInt(), WeaponId = r.ReadString(), WeaponUpgrade = r.ReadByte(),
            ArmorGrade = r.ReadByte(), GlovesGrade = r.ReadByte(), BootsGrade = r.ReadByte(), HideHelmet = r.ReadBool(),
        };
    }

    /// <summary>Cliente → servidor: mostrar ou esconder o elmo (o chapéu, no Arcanista). Fica salvo no personagem.</summary>
    public sealed class AppearanceCommand : INetMessage
    {
        public MessageType Type => MessageType.AppearanceCommand;
        public bool HideHelmet;

        public void Write(PacketWriter w) => w.WriteBool(HideHelmet);

        public static AppearanceCommand Read(PacketReader r) => new AppearanceCommand { HideHelmet = r.ReadBool() };
    }

    public sealed class EntityDespawned : INetMessage
    {
        public MessageType Type => MessageType.EntityDespawned;
        public int Id;

        public void Write(PacketWriter w) => w.WriteInt(Id);

        public static EntityDespawned Read(PacketReader r) => new EntityDespawned { Id = r.ReadInt() };
    }

    /// <summary>Estado de uma entidade num snapshot.</summary>
    public struct EntityState
    {
        public int Id;
        public float X;
        public float Z;
        public float Yaw;
        public int Hp;

        /// <summary>Flags: bit 0 = morto, bit 1 = preparando golpe, bit 2 = chefe na fase 2, bit 3 = sentado na fogueira.</summary>
        public byte Flags;

        public const byte FlagDead = 1;
        public const byte FlagWindUp = 2;
        public const byte FlagEnraged = 4;
        public const byte FlagResting = 8;
    }

    /// <summary>Item raro do grupo foi a sorteio: o cliente mostra Preciso, Quero e Passar até o prazo.</summary>
    public sealed class LootRollStart : INetMessage
    {
        public MessageType Type => MessageType.LootRollStart;
        public int RollId;
        public string ItemId = "";
        public int Quantity;
        public float Seconds;

        /// <summary>Este jogador pode escolher "Preciso" (o item serve para a classe dele).</summary>
        public bool CanNeed;

        public void Write(PacketWriter w)
        {
            w.WriteInt(RollId);
            w.WriteString(ItemId);
            w.WriteInt(Quantity);
            w.WriteFloat(Seconds);
            w.WriteBool(CanNeed);
        }

        public static LootRollStart Read(PacketReader r) => new LootRollStart
        {
            RollId = r.ReadInt(), ItemId = r.ReadString(), Quantity = r.ReadInt(), Seconds = r.ReadFloat(), CanNeed = r.ReadBool(),
        };
    }

    /// <summary>Resposta do jogador no sorteio (<see cref="Loot.LootChoice"/>).</summary>
    public sealed class LootRollChoose : INetMessage
    {
        public MessageType Type => MessageType.LootRollChoose;
        public int RollId;
        public Loot.LootChoice Choice;

        public void Write(PacketWriter w)
        {
            w.WriteInt(RollId);
            w.WriteByte((byte)Choice);
        }

        public static LootRollChoose Read(PacketReader r) => new LootRollChoose { RollId = r.ReadInt(), Choice = (Loot.LootChoice)r.ReadByte() };
    }

    /// <summary>Sentar ou levantar (a fogueira da vila regenera quem está sentado perto dela).</summary>
    public sealed class SetResting : INetMessage
    {
        public MessageType Type => MessageType.SetResting;
        public bool Resting;

        public void Write(PacketWriter w) => w.WriteBool(Resting);

        public static SetResting Read(PacketReader r) => new SetResting { Resting = r.ReadBool() };
    }

    /// <summary>Estado das entidades perto do jogador, enviado a cada tick.</summary>
    public sealed class Snapshot : INetMessage
    {
        public MessageType Type => MessageType.Snapshot;
        public uint Tick;
        public double ServerTime;
        public List<EntityState> Entities = new List<EntityState>();

        public void Write(PacketWriter w)
        {
            w.WriteUInt(Tick);
            w.WriteDouble(ServerTime);
            w.WriteUShort((ushort)Entities.Count);
            foreach (var e in Entities)
            {
                w.WriteInt(e.Id);
                w.WriteFloat(e.X);
                w.WriteFloat(e.Z);
                w.WriteFloat(e.Yaw);
                w.WriteInt(e.Hp);
                w.WriteByte(e.Flags);
            }
        }

        public static Snapshot Read(PacketReader r)
        {
            var snapshot = new Snapshot { Tick = r.ReadUInt(), ServerTime = r.ReadDouble() };
            var count = r.ReadUShort();
            snapshot.Entities.Capacity = count;
            for (var i = 0; i < count; i++)
            {
                snapshot.Entities.Add(new EntityState
                {
                    Id = r.ReadInt(), X = r.ReadFloat(), Z = r.ReadFloat(), Yaw = r.ReadFloat(), Hp = r.ReadInt(), Flags = r.ReadByte(),
                });
            }
            return snapshot;
        }
    }

    /// <summary>Resultado de um golpe decidido pelo servidor (de jogador ou de monstro).</summary>
    public sealed class CombatEvent : INetMessage
    {
        public MessageType Type => MessageType.CombatEvent;
        public int AttackerId;
        public int TargetId;

        /// <summary>Posição da skill (0–3) ou um valor de <see cref="CombatSource"/>.</summary>
        public byte Source;

        public int Damage;
        public bool Critical;
        public bool Dodged;

        /// <summary><see cref="Combat.ComboGrade"/> como byte, ou 255 se o golpe não fazia parte de um combo.</summary>
        public byte Grade = 255;

        /// <summary>Rota de combo fechada pelo golpe (<see cref="Combat.ComboRoute.Id"/>; vazio = nenhuma).</summary>
        public string RouteId = "";

        public void Write(PacketWriter w)
        {
            w.WriteInt(AttackerId);
            w.WriteInt(TargetId);
            w.WriteByte(Source);
            w.WriteInt(Damage);
            w.WriteBool(Critical);
            w.WriteBool(Dodged);
            w.WriteByte(Grade);
            w.WriteString(RouteId);
        }

        public static CombatEvent Read(PacketReader r) => new CombatEvent
        {
            AttackerId = r.ReadInt(), TargetId = r.ReadInt(), Source = r.ReadByte(), Damage = r.ReadInt(),
            Critical = r.ReadBool(), Dodged = r.ReadBool(), Grade = r.ReadByte(), RouteId = r.ReadString(),
        };
    }

    /// <summary>
    /// Estado do combo do jogador segundo o servidor. O cliente desenha a barra com isso
    /// (convertendo o tempo do servidor para o relógio local).
    /// </summary>
    public sealed class ComboState : INetMessage
    {
        public MessageType Type => MessageType.ComboState;
        public bool Active;
        public double CycleStartServerTime;
        public float BarDurationSeconds;
        public float WindowCenter;
        public float WindowWidth;
        public float ExcellentZoneRatio;
        public ushort HitCount;
        public float ChainBonus;
        public bool CanFinish;

        /// <summary><see cref="Combat.ComboEndReason"/> do último combo encerrado.</summary>
        public byte LastEndReason;

        public void Write(PacketWriter w)
        {
            w.WriteBool(Active);
            w.WriteDouble(CycleStartServerTime);
            w.WriteFloat(BarDurationSeconds);
            w.WriteFloat(WindowCenter);
            w.WriteFloat(WindowWidth);
            w.WriteFloat(ExcellentZoneRatio);
            w.WriteUShort(HitCount);
            w.WriteFloat(ChainBonus);
            w.WriteBool(CanFinish);
            w.WriteByte(LastEndReason);
        }

        public static ComboState Read(PacketReader r) => new ComboState
        {
            Active = r.ReadBool(), CycleStartServerTime = r.ReadDouble(), BarDurationSeconds = r.ReadFloat(),
            WindowCenter = r.ReadFloat(), WindowWidth = r.ReadFloat(), ExcellentZoneRatio = r.ReadFloat(),
            HitCount = r.ReadUShort(), ChainBonus = r.ReadFloat(), CanFinish = r.ReadBool(), LastEndReason = r.ReadByte(),
        };
    }

    /// <summary>Atributos e recursos do próprio jogador.</summary>
    public sealed class PlayerStats : INetMessage
    {
        public MessageType Type => MessageType.PlayerStats;
        public int Level;
        public int Xp;
        public int XpToNext;
        public int Hp;
        public int MaxHp;
        public int Mp;
        public int MaxMp;
        public int Sp;
        public int MaxSp;
        public long Alz;

        public void Write(PacketWriter w)
        {
            w.WriteInt(Level);
            w.WriteInt(Xp);
            w.WriteInt(XpToNext);
            w.WriteInt(Hp);
            w.WriteInt(MaxHp);
            w.WriteInt(Mp);
            w.WriteInt(MaxMp);
            w.WriteInt(Sp);
            w.WriteInt(MaxSp);
            w.WriteLong(Alz);
        }

        public static PlayerStats Read(PacketReader r) => new PlayerStats
        {
            Level = r.ReadInt(), Xp = r.ReadInt(), XpToNext = r.ReadInt(), Hp = r.ReadInt(), MaxHp = r.ReadInt(),
            Mp = r.ReadInt(), MaxMp = r.ReadInt(), Sp = r.ReadInt(), MaxSp = r.ReadInt(), Alz = r.ReadLong(),
        };
    }

    public sealed class LootSpawned : INetMessage
    {
        public MessageType Type => MessageType.LootSpawned;
        public int LootId;
        public string ItemId = "";

        /// <summary>Nome do item no idioma do jogador.</summary>
        public string Name => Loc.T(Loc.Key("item.", ItemId));

        public byte Rarity;
        public int Quantity;
        public float X;
        public float Z;

        public void Write(PacketWriter w)
        {
            w.WriteInt(LootId);
            w.WriteString(ItemId);
            w.WriteByte(Rarity);
            w.WriteInt(Quantity);
            w.WriteFloat(X);
            w.WriteFloat(Z);
        }

        public static LootSpawned Read(PacketReader r) => new LootSpawned
        {
            LootId = r.ReadInt(), ItemId = r.ReadString(), Rarity = r.ReadByte(),
            Quantity = r.ReadInt(), X = r.ReadFloat(), Z = r.ReadFloat(),
        };
    }

    public sealed class LootRemoved : INetMessage
    {
        public MessageType Type => MessageType.LootRemoved;
        public int LootId;

        /// <summary>Quem pegou (0 = sumiu sozinho).</summary>
        public int PickedById;

        public void Write(PacketWriter w)
        {
            w.WriteInt(LootId);
            w.WriteInt(PickedById);
        }

        public static LootRemoved Read(PacketReader r) => new LootRemoved { LootId = r.ReadInt(), PickedById = r.ReadInt() };
    }

    /// <summary>O servidor recusou o movimento (rápido demais): o cliente volta para esta posição.</summary>
    public sealed class PositionCorrection : INetMessage
    {
        public MessageType Type => MessageType.PositionCorrection;
        public float X;
        public float Z;

        public void Write(PacketWriter w)
        {
            w.WriteFloat(X);
            w.WriteFloat(Z);
        }

        public static PositionCorrection Read(PacketReader r) => new PositionCorrection { X = r.ReadFloat(), Z = r.ReadFloat() };
    }

    /// <summary>Texto para o jogador (aviso, motivo de recusa de uma ação).</summary>
    public sealed class Notice : INetMessage
    {
        public MessageType Type => MessageType.Notice;
        public LocalText Text = LocalText.Empty;

        /// <summary>Verdadeiro para aviso grande no topo (ex.: chefe furioso).</summary>
        public bool Announcement;

        public void Write(PacketWriter w)
        {
            Text.Write(w);
            w.WriteBool(Announcement);
        }

        public static Notice Read(PacketReader r) => new Notice { Text = LocalText.Read(r), Announcement = r.ReadBool() };
    }

    public sealed class Pong : INetMessage
    {
        public MessageType Type => MessageType.Pong;
        public double ClientTime;
        public double ServerTime;

        public void Write(PacketWriter w)
        {
            w.WriteDouble(ClientTime);
            w.WriteDouble(ServerTime);
        }

        public static Pong Read(PacketReader r) => new Pong { ClientTime = r.ReadDouble(), ServerTime = r.ReadDouble() };
    }

    /// <summary>Círculo de aviso no chão: quem estiver dentro quando acabar o tempo leva o golpe.</summary>
    public sealed class AreaWarning : INetMessage
    {
        public MessageType Type => MessageType.AreaWarning;
        public int SourceId;
        public float X;
        public float Z;
        public float Radius;
        public float Seconds;

        public void Write(PacketWriter w)
        {
            w.WriteInt(SourceId);
            w.WriteFloat(X);
            w.WriteFloat(Z);
            w.WriteFloat(Radius);
            w.WriteFloat(Seconds);
        }

        public static AreaWarning Read(PacketReader r) => new AreaWarning
        {
            SourceId = r.ReadInt(), X = r.ReadFloat(), Z = r.ReadFloat(), Radius = r.ReadFloat(), Seconds = r.ReadFloat(),
        };
    }

    /// <summary>Um item (ou pilha) num lugar: bolsa, equipamento ou armazém.</summary>
    public struct InventoryStack
    {
        public string ItemId;

        /// <summary>Nome do item no idioma do jogador.</summary>
        public string Name => Loc.T(Loc.Key("item.", ItemId));

        public byte Rarity;
        public int Quantity;
        public ItemContainerKind Container;
        public ushort Slot;
        public long Uid;
        public byte Upgrade;
        public byte PityFails;
        public byte PityLevel;
        public bool RegainGuaranteed;
        public string OptionId;
    }

    /// <summary>Inventário completo do jogador segundo o servidor (ao entrar e sempre que muda).</summary>
    public sealed class Inventory : INetMessage
    {
        public MessageType Type => MessageType.Inventory;
        public List<InventoryStack> Items = new List<InventoryStack>();

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)Items.Count);
            foreach (var item in Items)
            {
                w.WriteString(item.ItemId);
                w.WriteByte(item.Rarity);
                w.WriteInt(item.Quantity);
                w.WriteByte((byte)item.Container);
                w.WriteUShort(item.Slot);
                w.WriteLong(item.Uid);
                w.WriteByte(item.Upgrade);
                w.WriteByte(item.PityFails);
                w.WriteByte(item.PityLevel);
                w.WriteBool(item.RegainGuaranteed);
                w.WriteString(item.OptionId);
            }
        }

        public static Inventory Read(PacketReader r)
        {
            var inventory = new Inventory();
            var count = r.ReadUShort();
            for (var i = 0; i < count; i++)
            {
                inventory.Items.Add(new InventoryStack
                {
                    ItemId = r.ReadString(), Rarity = r.ReadByte(), Quantity = r.ReadInt(),
                    Container = (ItemContainerKind)r.ReadByte(), Slot = r.ReadUShort(), Uid = r.ReadLong(),
                    Upgrade = r.ReadByte(), PityFails = r.ReadByte(), PityLevel = r.ReadByte(),
                    RegainGuaranteed = r.ReadBool(), OptionId = r.ReadString(),
                });
            }
            return inventory;
        }
    }

    /// <summary>Converte mensagens em bytes e de volta.</summary>
    public static class NetCodec
    {
        /// <summary>Mensagem pronta para enviar (com cabeçalho de tamanho).</summary>
        public static byte[] Encode(INetMessage message)
        {
            var writer = new PacketWriter();
            message.Write(writer);
            return Framing.Encode(message.Type, writer);
        }

        /// <summary>Lê o conteúdo de uma mensagem do tipo informado. Sobras ou tipo desconhecido lançam <see cref="ProtocolException"/>.</summary>
        public static INetMessage Decode(MessageType type, byte[] payload)
        {
            var reader = new PacketReader(payload);
            INetMessage message = type switch
            {
                MessageType.Hello => Hello.Read(reader),
                MessageType.MoveInput => MoveInput.Read(reader),
                MessageType.UseSkill => UseSkill.Read(reader),
                MessageType.StartCombo => StartCombo.Read(reader),
                MessageType.Finisher => Finisher.Read(reader),
                MessageType.PickupLoot => PickupLoot.Read(reader),
                MessageType.Ping => Ping.Read(reader),
                MessageType.Welcome => Welcome.Read(reader),
                MessageType.Reject => Reject.Read(reader),
                MessageType.EntitySpawned => EntitySpawned.Read(reader),
                MessageType.EntityDespawned => EntityDespawned.Read(reader),
                MessageType.Snapshot => Snapshot.Read(reader),
                MessageType.CombatEvent => CombatEvent.Read(reader),
                MessageType.ComboState => ComboState.Read(reader),
                MessageType.PlayerStats => PlayerStats.Read(reader),
                MessageType.LootSpawned => LootSpawned.Read(reader),
                MessageType.LootRemoved => LootRemoved.Read(reader),
                MessageType.PositionCorrection => PositionCorrection.Read(reader),
                MessageType.Notice => Notice.Read(reader),
                MessageType.Pong => Pong.Read(reader),
                MessageType.AreaWarning => AreaWarning.Read(reader),
                MessageType.Inventory => Inventory.Read(reader),
                MessageType.ItemCommand => ItemCommand.Read(reader),
                MessageType.UpgradeItem => UpgradeItem.Read(reader),
                MessageType.ApplyOption => ApplyOption.Read(reader),
                MessageType.AllocateAttributes => AllocateAttributes.Read(reader),
                MessageType.LearnSkill => LearnSkill.Read(reader),
                MessageType.SetHotbar => SetHotbar.Read(reader),
                MessageType.AppearanceCommand => AppearanceCommand.Read(reader),
                MessageType.TalkToNpc => TalkToNpc.Read(reader),
                MessageType.QuestCommand => QuestCommand.Read(reader),
                MessageType.ActivateAwakening => ActivateAwakening.Read(reader),
                MessageType.CharacterSheet => CharacterSheet.Read(reader),
                MessageType.QuestState => QuestState.Read(reader),
                MessageType.NpcDialog => NpcDialog.Read(reader),
                MessageType.UpgradeResult => UpgradeResult.Read(reader),
                MessageType.PartyCommand => PartyCommand.Read(reader),
                MessageType.ChatSend => ChatSend.Read(reader),
                MessageType.PartyState => PartyState.Read(reader),
                MessageType.PartyInvite => PartyInvite.Read(reader),
                MessageType.ChatMessage => ChatMessage.Read(reader),
                MessageType.SocialCommand => SocialCommand.Read(reader),
                MessageType.SocialLists => SocialLists.Read(reader),
                MessageType.ShopCommand => ShopCommand.Read(reader),
                MessageType.TradeCommand => TradeCommand.Read(reader),
                MessageType.TradeState => TradeState.Read(reader),
                MessageType.TradeInvite => TradeInvite.Read(reader),
                MessageType.DungeonCommand => DungeonCommand.Read(reader),
                MessageType.DungeonState => DungeonState.Read(reader),
                MessageType.DungeonInfo => DungeonInfo.Read(reader),
                MessageType.DuelCommand => DuelCommand.Read(reader),
                MessageType.ArenaCommand => ArenaCommand.Read(reader),
                MessageType.GmCommand => GmCommand.Read(reader),
                MessageType.ClientReport => ClientReport.Read(reader),
                MessageType.DuelInvite => DuelInvite.Read(reader),
                MessageType.PvpState => PvpState.Read(reader),
                MessageType.EntityLook => EntityLook.Read(reader),
                MessageType.MarketCommand => MarketCommand.Read(reader),
                MessageType.MarketState => MarketState.Read(reader),
                MessageType.FriendInvite => FriendInvite.Read(reader),
                MessageType.SetResting => SetResting.Read(reader),
                MessageType.LootRollStart => LootRollStart.Read(reader),
                MessageType.LootRollChoose => LootRollChoose.Read(reader),
                _ => throw new ProtocolException($"Tipo de mensagem desconhecido: {(ushort)type}."),
            };
            // Hello de outra versão pode ter mais campos: lê só a versão para o servidor poder recusar com motivo.
            var otherVersionHello = message is Hello hello && hello.ProtocolVersion != Protocol.Version;
            // A recusa também pode vir de um servidor de outra versão (com mais campos): o motivo vem primeiro e é lido.
            if (reader.Remaining != 0 && !otherVersionHello && message is not Reject) throw new ProtocolException($"{type}: sobraram {reader.Remaining} bytes.");
            return message;
        }
    }
}
