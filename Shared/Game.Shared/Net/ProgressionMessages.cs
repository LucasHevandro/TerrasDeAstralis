using System.Collections.Generic;
using Game.Shared.Text;

namespace Game.Shared.Net
{
    /// <summary>Onde um item está.</summary>
    public enum ItemContainerKind : byte
    {
        Bag = 0,

        /// <summary>Vestido; a posição é o <see cref="Items.EquipSlot"/>.</summary>
        Equipment = 1,

        /// <summary>Armazém (só mexe perto do NPC do armazém).</summary>
        Warehouse = 2,
    }

    public enum ItemActionKind : byte
    {
        /// <summary>
        /// Move de um lugar para outro. Bolsa → equipamento veste, equipamento → bolsa tira, bolsa ↔ armazém guarda e
        /// retira, dentro do mesmo lugar troca ou junta pilhas.
        /// </summary>
        Move = 0,

        /// <summary>Joga fora a quantidade pedida (0 = a pilha inteira).</summary>
        Discard = 1,

        /// <summary>Usa um consumível da bolsa.</summary>
        Use = 2,

        /// <summary>Larga no chão a quantidade pedida (0 = a pilha inteira), para qualquer um pegar.</summary>
        Drop = 3,
    }

    // ===== Cliente → servidor =====

    public sealed class ItemCommand : INetMessage
    {
        public MessageType Type => MessageType.ItemCommand;
        public ItemActionKind Action;
        public ItemContainerKind From;
        public ushort FromSlot;
        public ItemContainerKind To;
        public ushort ToSlot;
        public int Quantity;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteByte((byte)From);
            w.WriteUShort(FromSlot);
            w.WriteByte((byte)To);
            w.WriteUShort(ToSlot);
            w.WriteInt(Quantity);
        }

        public static ItemCommand Read(PacketReader r) => new ItemCommand
        {
            Action = (ItemActionKind)r.ReadByte(), From = (ItemContainerKind)r.ReadByte(), FromSlot = r.ReadUShort(),
            To = (ItemContainerKind)r.ReadByte(), ToSlot = r.ReadUShort(), Quantity = r.ReadInt(),
        };
    }

    /// <summary>Melhorar um item na ferreira (bolsa ou vestido).</summary>
    public sealed class UpgradeItem : INetMessage
    {
        public MessageType Type => MessageType.UpgradeItem;
        public ItemContainerKind Container;
        public ushort Slot;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Container);
            w.WriteUShort(Slot);
        }

        public static UpgradeItem Read(PacketReader r) =>
            new UpgradeItem { Container = (ItemContainerKind)r.ReadByte(), Slot = r.ReadUShort() };
    }

    /// <summary>Colocar a pedra de opção da bolsa (<see cref="StoneSlot"/>) num item.</summary>
    public sealed class ApplyOption : INetMessage
    {
        public MessageType Type => MessageType.ApplyOption;
        public ItemContainerKind Container;
        public ushort Slot;
        public ushort StoneSlot;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Container);
            w.WriteUShort(Slot);
            w.WriteUShort(StoneSlot);
        }

        public static ApplyOption Read(PacketReader r) =>
            new ApplyOption { Container = (ItemContainerKind)r.ReadByte(), Slot = r.ReadUShort(), StoneSlot = r.ReadUShort() };
    }

    /// <summary>Distribuir pontos de atributo (soma aos atuais).</summary>
    public sealed class AllocateAttributes : INetMessage
    {
        public MessageType Type => MessageType.AllocateAttributes;
        public ushort Strength;
        public ushort Dexterity;
        public ushort Intelligence;

        public void Write(PacketWriter w)
        {
            w.WriteUShort(Strength);
            w.WriteUShort(Dexterity);
            w.WriteUShort(Intelligence);
        }

        public static AllocateAttributes Read(PacketReader r) =>
            new AllocateAttributes { Strength = r.ReadUShort(), Dexterity = r.ReadUShort(), Intelligence = r.ReadUShort() };
    }

    /// <summary>Aprender uma skill da classe com o mestre de skills (perto dele, pagando em Alz).</summary>
    public sealed class LearnSkill : INetMessage
    {
        public MessageType Type => MessageType.LearnSkill;
        public string SkillId = "";

        public void Write(PacketWriter w) => w.WriteString(SkillId);

        public static LearnSkill Read(PacketReader r) => new LearnSkill { SkillId = r.ReadString() };
    }

    /// <summary>Nova arrumação da barra de atalhos: o índice da skill da classe em cada casa (255 = vazia).</summary>
    public sealed class SetHotbar : INetMessage
    {
        public MessageType Type => MessageType.SetHotbar;
        public List<byte> Slots = new List<byte>();

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Slots.Count);
            foreach (var slot in Slots) w.WriteByte(slot);
        }

        public static SetHotbar Read(PacketReader r)
        {
            var message = new SetHotbar();
            var count = System.Math.Min((int)r.ReadByte(), 32);
            for (var i = 0; i < count; i++) message.Slots.Add(r.ReadByte());
            return message;
        }
    }

    /// <summary>Falar com um NPC: conta para missões e abre a conversa (<see cref="NpcDialog"/>).</summary>
    public sealed class TalkToNpc : INetMessage
    {
        public MessageType Type => MessageType.TalkToNpc;
        public string NpcId = "";

        public void Write(PacketWriter w) => w.WriteString(NpcId);

        public static TalkToNpc Read(PacketReader r) => new TalkToNpc { NpcId = r.ReadString() };
    }

    public enum QuestActionKind : byte
    {
        Accept = 0,
        TurnIn = 1,
        Abandon = 2,
    }

    public sealed class QuestCommand : INetMessage
    {
        public MessageType Type => MessageType.QuestCommand;
        public QuestActionKind Action;
        public string QuestId = "";

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Action);
            w.WriteString(QuestId);
        }

        public static QuestCommand Read(PacketReader r) =>
            new QuestCommand { Action = (QuestActionKind)r.ReadByte(), QuestId = r.ReadString() };
    }

    /// <summary>Ativar o modo Despertar (barra cheia).</summary>
    public sealed class ActivateAwakening : INetMessage
    {
        public MessageType Type => MessageType.ActivateAwakening;

        public void Write(PacketWriter w) { }

        public static ActivateAwakening Read(PacketReader r) => new ActivateAwakening();
    }

    // ===== Servidor → cliente =====

    /// <summary>Ficha do personagem: classe, atributos, valores finais, skills liberadas e Despertar.</summary>
    public sealed class CharacterSheet : INetMessage
    {
        public MessageType Type => MessageType.CharacterSheet;
        public byte Class;
        public ushort Strength;
        public ushort Dexterity;
        public ushort Intelligence;
        public ushort FreePoints;
        public int AttackPower;
        public int MagicPower;
        public int Defense;
        public float CritPercent;
        public bool ComboUnlocked;

        /// <summary>Uma marca por skill da classe (na ordem do catálogo): aprendida ou não.</summary>
        public List<bool> SkillUnlocked = new List<bool>();

        /// <summary>Barra de atalhos: o índice da skill em cada casa (255 = vazia).</summary>
        public List<byte> Hotbar = new List<byte>();

        public byte AwakeningGauge;
        public double AwakeningUntil;

        public void Write(PacketWriter w)
        {
            w.WriteByte(Class);
            w.WriteUShort(Strength);
            w.WriteUShort(Dexterity);
            w.WriteUShort(Intelligence);
            w.WriteUShort(FreePoints);
            w.WriteInt(AttackPower);
            w.WriteInt(MagicPower);
            w.WriteInt(Defense);
            w.WriteFloat(CritPercent);
            w.WriteBool(ComboUnlocked);
            w.WriteByte((byte)SkillUnlocked.Count);
            foreach (var unlocked in SkillUnlocked) w.WriteBool(unlocked);
            w.WriteByte((byte)Hotbar.Count);
            foreach (var slot in Hotbar) w.WriteByte(slot);
            w.WriteByte(AwakeningGauge);
            w.WriteDouble(AwakeningUntil);
        }

        public static CharacterSheet Read(PacketReader r)
        {
            var sheet = new CharacterSheet
            {
                Class = r.ReadByte(), Strength = r.ReadUShort(), Dexterity = r.ReadUShort(), Intelligence = r.ReadUShort(),
                FreePoints = r.ReadUShort(), AttackPower = r.ReadInt(), MagicPower = r.ReadInt(), Defense = r.ReadInt(),
                CritPercent = r.ReadFloat(), ComboUnlocked = r.ReadBool(),
            };
            var count = r.ReadByte();
            for (var i = 0; i < count; i++) sheet.SkillUnlocked.Add(r.ReadBool());
            var slots = r.ReadByte();
            for (var i = 0; i < slots; i++) sheet.Hotbar.Add(r.ReadByte());
            sheet.AwakeningGauge = r.ReadByte();
            sheet.AwakeningUntil = r.ReadDouble();
            return sheet;
        }
    }

    public struct QuestEntry
    {
        public string QuestId;
        public List<int> Counts;
    }

    /// <summary>Diário de missões completo (ao entrar e sempre que muda).</summary>
    public sealed class QuestState : INetMessage
    {
        public MessageType Type => MessageType.QuestState;
        public List<string> Completed = new List<string>();
        public List<QuestEntry> Active = new List<QuestEntry>();

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)Completed.Count);
            foreach (var id in Completed) w.WriteString(id);
            w.WriteByte((byte)Active.Count);
            foreach (var quest in Active)
            {
                w.WriteString(quest.QuestId);
                w.WriteByte((byte)quest.Counts.Count);
                foreach (var count in quest.Counts) w.WriteInt(count);
            }
        }

        public static QuestState Read(PacketReader r)
        {
            var state = new QuestState();
            var completed = r.ReadUShort();
            for (var i = 0; i < completed; i++) state.Completed.Add(r.ReadString());
            var active = r.ReadByte();
            for (var i = 0; i < active; i++)
            {
                var entry = new QuestEntry { QuestId = r.ReadString(), Counts = new List<int>() };
                var counts = r.ReadByte();
                for (var j = 0; j < counts; j++) entry.Counts.Add(r.ReadInt());
                state.Active.Add(entry);
            }
            return state;
        }
    }

    /// <summary>Resposta a <see cref="TalkToNpc"/>: missões para aceitar e para entregar.</summary>
    public sealed class NpcDialog : INetMessage
    {
        public MessageType Type => MessageType.NpcDialog;
        public string NpcId = "";
        public List<string> Offered = new List<string>();
        public List<string> ReadyToTurnIn = new List<string>();

        public void Write(PacketWriter w)
        {
            w.WriteString(NpcId);
            w.WriteByte((byte)Offered.Count);
            foreach (var id in Offered) w.WriteString(id);
            w.WriteByte((byte)ReadyToTurnIn.Count);
            foreach (var id in ReadyToTurnIn) w.WriteString(id);
        }

        public static NpcDialog Read(PacketReader r)
        {
            var dialog = new NpcDialog { NpcId = r.ReadString() };
            var offered = r.ReadByte();
            for (var i = 0; i < offered; i++) dialog.Offered.Add(r.ReadString());
            var ready = r.ReadByte();
            for (var i = 0; i < ready; i++) dialog.ReadyToTurnIn.Add(r.ReadString());
            return dialog;
        }
    }

    /// <summary>Resultado de uma tentativa de melhoria (a janela da ferreira anima com isto).</summary>
    public sealed class UpgradeResult : INetMessage
    {
        public MessageType Type => MessageType.UpgradeResult;

        /// <summary><see cref="Items.UpgradeOutcome"/>.</summary>
        public byte Outcome;

        public long Uid;
        public byte NewLevel;
        public LocalText Message = LocalText.Empty;

        public void Write(PacketWriter w)
        {
            w.WriteByte(Outcome);
            w.WriteLong(Uid);
            w.WriteByte(NewLevel);
            Message.Write(w);
        }

        public static UpgradeResult Read(PacketReader r) =>
            new UpgradeResult { Outcome = r.ReadByte(), Uid = r.ReadLong(), NewLevel = r.ReadByte(), Message = LocalText.Read(r) };
    }
}
