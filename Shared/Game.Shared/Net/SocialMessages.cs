using System.Collections.Generic;
using Game.Shared.Economy;
using Game.Shared.Social;
using Game.Shared.Text;

namespace Game.Shared.Net
{
    public enum PartyActionKind : byte
    {
        /// <summary>Convida pelo nome (<see cref="PartyCommand.TargetName"/>).</summary>
        Invite = 0,

        /// <summary>Aceita o convite de <see cref="PartyCommand.TargetId"/>.</summary>
        Accept = 1,

        Decline = 2,
        Leave = 3,

        /// <summary>Só o líder: tira um membro pelo nome.</summary>
        Kick = 4,

        /// <summary>Só o líder: passa a liderança pelo nome.</summary>
        Promote = 5,
    }

    public enum RelationActionKind : byte
    {
        /// <summary>Convida <see cref="SocialCommand.TargetName"/> para a amizade (só vale quando ele aceita).</summary>
        AddFriend = 0,
        RemoveFriend = 1,
        Block = 2,
        Unblock = 3,

        /// <summary>Denuncia <see cref="SocialCommand.TargetName"/>; <see cref="SocialCommand.Text"/> é o motivo.</summary>
        Report = 4,

        /// <summary>Aceita o convite de amizade de <see cref="SocialCommand.TargetName"/>: os dois viram amigos.</summary>
        AcceptFriend = 5,

        DeclineFriend = 6,
    }

    public enum ShopActionKind : byte
    {
        /// <summary>Compra <see cref="ShopCommand.Quantity"/> de <see cref="ShopCommand.ItemId"/>.</summary>
        Buy = 0,

        /// <summary>Vende <see cref="ShopCommand.Quantity"/> do item da posição <see cref="ShopCommand.Slot"/> da bolsa.</summary>
        Sell = 1,
    }

    public enum TradeActionKind : byte
    {
        /// <summary>Pede troca a <see cref="TradeCommand.TargetName"/>.</summary>
        Request = 0,

        /// <summary>Aceita o pedido de <see cref="TradeCommand.TargetId"/>.</summary>
        Accept = 1,

        Decline = 2,
        Cancel = 3,

        /// <summary>Troca a oferta inteira (itens e Alz); destrava os dois lados.</summary>
        SetOffer = 4,

        /// <summary>Primeira confirmação: "Pronto".</summary>
        Lock = 5,

        /// <summary>Segunda confirmação (com os dois travados).</summary>
        Confirm = 6,
    }

    // ===== Cliente → servidor =====

    /// <summary>Comprar ou vender na loja (perto do NPC).</summary>
    public sealed class ShopCommand : INetMessage
    {
        public MessageType Type => MessageType.ShopCommand;
        public ShopActionKind Action;
        public string ItemId = "";
        public ushort Slot;
        public int Quantity;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteString(ItemId);
            w.WriteUShort(Slot);
            w.WriteInt(Quantity);
        }

        public static ShopCommand Read(PacketReader r) =>
            new ShopCommand { Action = (ShopActionKind)r.ReadByte(), ItemId = r.ReadString(), Slot = r.ReadUShort(), Quantity = r.ReadInt() };
    }

    public sealed class TradeCommand : INetMessage
    {
        public MessageType Type => MessageType.TradeCommand;
        public TradeActionKind Action;
        public int TargetId;
        public string TargetName = "";

        /// <summary>Só em <see cref="TradeActionKind.SetOffer"/>.</summary>
        public List<TradeItem> Items = new List<TradeItem>();

        public long Alz;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteInt(TargetId);
            w.WriteString(TargetName);
            w.WriteByte((byte)Items.Count);
            foreach (var item in Items)
            {
                w.WriteUShort((ushort)item.Slot);
                w.WriteLong(item.Uid);
                w.WriteInt(item.Quantity);
            }
            w.WriteLong(Alz);
        }

        public static TradeCommand Read(PacketReader r)
        {
            var command = new TradeCommand { Action = (TradeActionKind)r.ReadByte(), TargetId = r.ReadInt(), TargetName = r.ReadString() };
            var count = r.ReadByte();
            for (var i = 0; i < count; i++) command.Items.Add(new TradeItem(r.ReadUShort(), r.ReadLong(), r.ReadInt()));
            command.Alz = r.ReadLong();
            return command;
        }
    }

    /// <summary>Amigos, bloqueio e denúncia, sempre pelo nome do personagem.</summary>
    public sealed class SocialCommand : INetMessage
    {
        public MessageType Type => MessageType.SocialCommand;
        public RelationActionKind Action;
        public string TargetName = "";
        public string Text = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteString(TargetName);
            w.WriteString(Text);
        }

        public static SocialCommand Read(PacketReader r) =>
            new SocialCommand { Action = (RelationActionKind)r.ReadByte(), TargetName = r.ReadString(), Text = r.ReadString() };
    }

    public sealed class PartyCommand : INetMessage
    {
        public MessageType Type => MessageType.PartyCommand;
        public PartyActionKind Action;
        public int TargetId;
        public string TargetName = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteInt(TargetId);
            w.WriteString(TargetName);
        }

        public static PartyCommand Read(PacketReader r) =>
            new PartyCommand { Action = (PartyActionKind)r.ReadByte(), TargetId = r.ReadInt(), TargetName = r.ReadString() };
    }

    /// <summary>Mensagem de chat. <see cref="Target"/> é o nome de quem recebe o sussurro.</summary>
    public sealed class ChatSend : INetMessage
    {
        public MessageType Type => MessageType.ChatSend;
        public ChatChannel Channel;
        public string Target = "";
        public string Text = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Channel);
            w.WriteString(Target);
            w.WriteString(Text);
        }

        public static ChatSend Read(PacketReader r) =>
            new ChatSend { Channel = (ChatChannel)r.ReadByte(), Target = r.ReadString(), Text = r.ReadString() };
    }

    // ===== Servidor → cliente =====

    public struct PartyMemberInfo
    {
        public int Id;
        public string Name;
        public int Level;
        public byte Class;
        public int Hp;
        public int MaxHp;
        public int Mp;
        public int MaxMp;
        public bool Dead;

        /// <summary>Perto o bastante para dividir XP e drop.</summary>
        public bool Near;
    }

    /// <summary>Grupo do jogador (lista vazia = sem grupo). Chega quando muda e quando a vida dos membros muda.</summary>
    public sealed class PartyState : INetMessage
    {
        public MessageType Type => MessageType.PartyState;
        public int LeaderId;
        public List<PartyMemberInfo> Members = new List<PartyMemberInfo>();

        public void Write(PacketWriter w)
        {
            w.WriteInt(LeaderId);
            w.WriteByte((byte)Members.Count);
            foreach (var m in Members)
            {
                w.WriteInt(m.Id);
                w.WriteString(m.Name);
                w.WriteInt(m.Level);
                w.WriteByte(m.Class);
                w.WriteInt(m.Hp);
                w.WriteInt(m.MaxHp);
                w.WriteInt(m.Mp);
                w.WriteInt(m.MaxMp);
                w.WriteBool(m.Dead);
                w.WriteBool(m.Near);
            }
        }

        public static PartyState Read(PacketReader r)
        {
            var state = new PartyState { LeaderId = r.ReadInt() };
            var count = r.ReadByte();
            for (var i = 0; i < count; i++)
            {
                state.Members.Add(new PartyMemberInfo
                {
                    Id = r.ReadInt(), Name = r.ReadString(), Level = r.ReadInt(), Class = r.ReadByte(), Hp = r.ReadInt(),
                    MaxHp = r.ReadInt(), Mp = r.ReadInt(), MaxMp = r.ReadInt(), Dead = r.ReadBool(), Near = r.ReadBool(),
                });
            }
            return state;
        }
    }

    public struct FriendInfo
    {
        public string Name;
        public bool Online;

        /// <summary>Nível e classe só quando online (0 = desconhecido).</summary>
        public int Level;

        public byte Class;
    }

    /// <summary>Lista de amigos (com quem está online) e de bloqueados. Chega ao entrar e quando muda.</summary>
    public sealed class SocialLists : INetMessage
    {
        public MessageType Type => MessageType.SocialLists;
        public List<FriendInfo> Friends = new List<FriendInfo>();
        public List<string> Blocked = new List<string>();

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)Friends.Count);
            foreach (var f in Friends)
            {
                w.WriteString(f.Name);
                w.WriteBool(f.Online);
                w.WriteInt(f.Level);
                w.WriteByte(f.Class);
            }
            w.WriteUShort((ushort)Blocked.Count);
            foreach (var name in Blocked) w.WriteString(name);
        }

        public static SocialLists Read(PacketReader r)
        {
            var lists = new SocialLists();
            var friends = r.ReadUShort();
            for (var i = 0; i < friends; i++)
                lists.Friends.Add(new FriendInfo { Name = r.ReadString(), Online = r.ReadBool(), Level = r.ReadInt(), Class = r.ReadByte() });
            var blocked = r.ReadUShort();
            for (var i = 0; i < blocked; i++) lists.Blocked.Add(r.ReadString());
            return lists;
        }
    }

    /// <summary>Um item na tela da troca (o do outro lado só se vê por aqui).</summary>
    public struct TradeItemInfo
    {
        public int Slot;
        public long Uid;
        public string ItemId;
        public int Quantity;
        public byte Upgrade;
        public string OptionId;
    }

    /// <summary>Estado da troca aberta (Active falso = fechou). Chega a cada mudança.</summary>
    public sealed class TradeState : INetMessage
    {
        public MessageType Type => MessageType.TradeState;
        public bool Active;
        public int PartnerId;
        public string PartnerName = "";
        public List<TradeItemInfo> MyItems = new List<TradeItemInfo>();
        public long MyAlz;
        public List<TradeItemInfo> TheirItems = new List<TradeItemInfo>();
        public long TheirAlz;
        public bool MyLocked;
        public bool TheirLocked;
        public bool MyConfirmed;
        public bool TheirConfirmed;

        public void Write(PacketWriter w)
        {
            w.WriteBool(Active);
            w.WriteInt(PartnerId);
            w.WriteString(PartnerName);
            WriteItems(w, MyItems);
            w.WriteLong(MyAlz);
            WriteItems(w, TheirItems);
            w.WriteLong(TheirAlz);
            w.WriteBool(MyLocked);
            w.WriteBool(TheirLocked);
            w.WriteBool(MyConfirmed);
            w.WriteBool(TheirConfirmed);
        }

        public static TradeState Read(PacketReader r)
        {
            var state = new TradeState { Active = r.ReadBool(), PartnerId = r.ReadInt(), PartnerName = r.ReadString() };
            ReadItems(r, state.MyItems);
            state.MyAlz = r.ReadLong();
            ReadItems(r, state.TheirItems);
            state.TheirAlz = r.ReadLong();
            state.MyLocked = r.ReadBool();
            state.TheirLocked = r.ReadBool();
            state.MyConfirmed = r.ReadBool();
            state.TheirConfirmed = r.ReadBool();
            return state;
        }

        private static void WriteItems(PacketWriter w, List<TradeItemInfo> items)
        {
            w.WriteByte((byte)items.Count);
            foreach (var item in items)
            {
                w.WriteUShort((ushort)item.Slot);
                w.WriteLong(item.Uid);
                w.WriteString(item.ItemId);
                w.WriteInt(item.Quantity);
                w.WriteByte(item.Upgrade);
                w.WriteString(item.OptionId);
            }
        }

        private static void ReadItems(PacketReader r, List<TradeItemInfo> items)
        {
            var count = r.ReadByte();
            for (var i = 0; i < count; i++)
                items.Add(new TradeItemInfo
                {
                    Slot = r.ReadUShort(), Uid = r.ReadLong(), ItemId = r.ReadString(), Quantity = r.ReadInt(), Upgrade = r.ReadByte(),
                    OptionId = r.ReadString(),
                });
        }
    }

    /// <summary>Alguém pediu para trocar com este jogador.</summary>
    public sealed class TradeInvite : INetMessage
    {
        public MessageType Type => MessageType.TradeInvite;
        public int FromId;
        public string FromName = "";
        public float Seconds;

        public void Write(PacketWriter w)
        {
            w.WriteInt(FromId);
            w.WriteString(FromName);
            w.WriteFloat(Seconds);
        }

        public static TradeInvite Read(PacketReader r) => new TradeInvite { FromId = r.ReadInt(), FromName = r.ReadString(), Seconds = r.ReadFloat() };
    }

    /// <summary>Alguém convidou este jogador para um grupo.</summary>
    public sealed class PartyInvite : INetMessage
    {
        public MessageType Type => MessageType.PartyInvite;
        public int InviterId;
        public string InviterName = "";
        public float Seconds;

        public void Write(PacketWriter w)
        {
            w.WriteInt(InviterId);
            w.WriteString(InviterName);
            w.WriteFloat(Seconds);
        }

        public static PartyInvite Read(PacketReader r) =>
            new PartyInvite { InviterId = r.ReadInt(), InviterName = r.ReadString(), Seconds = r.ReadFloat() };
    }

    /// <summary>Alguém pediu amizade: o cliente mostra Aceitar e Recusar até o prazo acabar.</summary>
    public sealed class FriendInvite : INetMessage
    {
        public MessageType Type => MessageType.FriendInvite;
        public string InviterName = "";
        public float Seconds;

        public void Write(PacketWriter w)
        {
            w.WriteString(InviterName);
            w.WriteFloat(Seconds);
        }

        public static FriendInvite Read(PacketReader r) => new FriendInvite { InviterName = r.ReadString(), Seconds = r.ReadFloat() };
    }

    /// <summary>
    /// Uma linha do chat. No sussurro, quem enviou recebe uma cópia com <see cref="ToName"/> preenchido
    /// ("Para Fulano: ..."). Linhas do sistema vêm em <see cref="SystemText"/> (traduzidas no cliente).
    /// </summary>
    public sealed class ChatMessage : INetMessage
    {
        public MessageType Type => MessageType.ChatMessage;
        public ChatChannel Channel;
        public int FromId;
        public string FromName = "";
        public string ToName = "";

        /// <summary>O que o jogador escreveu (já filtrado).</summary>
        public string Text = "";

        /// <summary>Linha do sistema (canal <see cref="ChatChannel.System"/>).</summary>
        public LocalText SystemText = LocalText.Empty;

        /// <summary>O texto para mostrar: o do jogador ou o do sistema no idioma carregado.</summary>
        public string Display => Channel == ChatChannel.System ? SystemText.ToString() : Text;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Channel);
            w.WriteInt(FromId);
            w.WriteString(FromName);
            w.WriteString(ToName);
            w.WriteString(Text);
            SystemText.Write(w);
        }

        public static ChatMessage Read(PacketReader r) => new ChatMessage
        {
            Channel = (ChatChannel)r.ReadByte(), FromId = r.ReadInt(), FromName = r.ReadString(), ToName = r.ReadString(),
            Text = r.ReadString(), SystemText = LocalText.Read(r),
        };
    }

    // ===== Mercado (quadro de ofertas) =====

    public enum MarketActionKind : byte
    {
        /// <summary>Pede o quadro de novo (atualizar).</summary>
        Open = 0,

        /// <summary>Põe à venda <see cref="MarketCommand.Quantity"/> unidades do item da posição da bolsa.</summary>
        Sell = 1,

        Buy = 2,

        /// <summary>Retira uma oferta própria (o item volta para a bolsa).</summary>
        Cancel = 3,

        /// <summary>Recebe o Alz das vendas e os itens das ofertas vencidas.</summary>
        Collect = 4,
    }

    /// <summary>Cliente → servidor: ação no mercado (perto da escrivã).</summary>
    public sealed class MarketCommand : INetMessage
    {
        public MessageType Type => MessageType.MarketCommand;
        public MarketActionKind Action;
        public ushort Slot;
        public int Quantity;
        public long Price;
        public long ListingId;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteUShort(Slot);
            w.WriteInt(Quantity);
            w.WriteLong(Price);
            w.WriteLong(ListingId);
        }

        public static MarketCommand Read(PacketReader r) => new MarketCommand
        {
            Action = (MarketActionKind)r.ReadByte(), Slot = r.ReadUShort(), Quantity = r.ReadInt(), Price = r.ReadLong(), ListingId = r.ReadLong(),
        };
    }

    /// <summary>Uma oferta como o cliente vê.</summary>
    public sealed class MarketEntry
    {
        public long Id;
        public string ItemId = "";
        public int Quantity;
        public byte Upgrade;
        public string OptionId = "";
        public long Price;
        public string Seller = "";
        public MarketListingState State;

        /// <summary>Venceu sem vender (o vendedor pega o item de volta com a escrivã).</summary>
        public bool Expired;

        /// <summary>Horas até vencer (ativas).</summary>
        public float HoursLeft;

        /// <summary>Quem comprou (vendidas).</summary>
        public string Buyer = "";

        public void Write(PacketWriter w)
        {
            w.WriteLong(Id);
            w.WriteString(ItemId);
            w.WriteInt(Quantity);
            w.WriteByte(Upgrade);
            w.WriteString(OptionId);
            w.WriteLong(Price);
            w.WriteString(Seller);
            w.WriteByte((byte)State);
            w.WriteBool(Expired);
            w.WriteFloat(HoursLeft);
            w.WriteString(Buyer);
        }

        public static MarketEntry Read(PacketReader r) => new MarketEntry
        {
            Id = r.ReadLong(), ItemId = r.ReadString(), Quantity = r.ReadInt(), Upgrade = r.ReadByte(), OptionId = r.ReadString(),
            Price = r.ReadLong(), Seller = r.ReadString(), State = (MarketListingState)r.ReadByte(),
            Expired = r.ReadBool(), HoursLeft = r.ReadFloat(), Buyer = r.ReadString(),
        };
    }

    /// <summary>
    /// Servidor → cliente: o quadro (ofertas à venda, as mais novas primeiro) e as ofertas do jogador (ativas, vencidas e
    /// vendidas ainda não recebidas), com o total a receber.
    /// </summary>
    public sealed class MarketState : INetMessage
    {
        public MessageType Type => MessageType.MarketState;
        public List<MarketEntry> Offers = new List<MarketEntry>();
        public List<MarketEntry> Mine = new List<MarketEntry>();
        public long ToCollectAlz;
        public int ToCollectItems;

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)Offers.Count);
            foreach (var entry in Offers) entry.Write(w);
            w.WriteUShort((ushort)Mine.Count);
            foreach (var entry in Mine) entry.Write(w);
            w.WriteLong(ToCollectAlz);
            w.WriteInt(ToCollectItems);
        }

        public static MarketState Read(PacketReader r)
        {
            var state = new MarketState();
            var offers = r.ReadUShort();
            for (var i = 0; i < offers; i++) state.Offers.Add(MarketEntry.Read(r));
            var mine = r.ReadUShort();
            for (var i = 0; i < mine; i++) state.Mine.Add(MarketEntry.Read(r));
            state.ToCollectAlz = r.ReadLong();
            state.ToCollectItems = r.ReadInt();
            return state;
        }
    }
}
