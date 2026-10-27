using System.Collections.Generic;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Progression;
using Game.Shared.Text;
using UnityEngine;
using Msg = Game.Shared.Net;

namespace Game.Client.UI
{
    /// <summary>
    /// Skills: a janela das skills da classe (K), o mestre de skills (aprender pagando em Alz) e a barra de atalhos
    /// montada arrastando: da janela para a barra, entre casas da barra (troca) e para fora (tira).
    /// Clicar numa casa da barra usa a skill. A arrumação vai para o servidor e fica salva com o personagem.
    /// </summary>
    public sealed partial class GameWindows
    {
        private const float SkillRow = 52f;
        private const float SkillIcon = 42f;

        private bool _skillsOpen;

        // Arrastar para a barra: o valor da casa (índice da skill na classe ou poção, ver SkillTraining.ItemBase) e de que
        // casa saiu (-1 = da janela de skills ou do inventário).
        private int _skillDrag = -1;
        private int _skillDragSlot = -1;
        private Vector2 _skillDragStart;
        private bool _skillDragging;

        public void SetSkillsOpen(bool open) => _skillsOpen = open;

        private static IReadOnlyList<ClassSkill> ClassSkills =>
            ClassCatalog.SkillsFor(CharacterState.Sheet != null ? (CharacterClass)CharacterState.Sheet.Class : CharacterClass.Blade);

        private void DrawSkills(NpcDefinition npc)
        {
            var trainer = npc != null && npc.Role == NpcRole.Trainer;
            if (trainer) DrawSkillList(Window(new Rect(20, 156, 500, 560), Loc.T("skills.trainer_title", npc.Name)), true);
            else if (_skillsOpen) DrawSkillList(Window(new Rect(20, 156, 500, 520), Loc.T("skills.title", KeyBindings.Label(GameAction.Skills))), false);
            HandleHotbar();
        }

        private void DrawSkillList(Rect rect, bool trainer)
        {
            var x = rect.x + 14;
            var y = rect.y + 48;
            if (trainer)
            {
                GUI.Label(new Rect(x, y, rect.width - 28, 40), Loc.T("skills.trainer_hint"), _tiny);
                y += 40;
            }

            var skills = ClassSkills;
            var level = Player.Level;
            var loot = Player.GetComponent<PlayerLoot>();
            var alz = loot != null ? loot.Alz : 0;
            var e = Event.current;
            for (var i = 0; i < skills.Count; i++)
            {
                var classSkill = skills[i];
                var skill = classSkill.Skill;
                var learned = CharacterState.SkillUnlocked(i);
                var icon = new Rect(x, y + 4, SkillIcon, SkillIcon);

                UiTheme.Slot(icon, learned && icon.Contains(e.mousePosition));
                GUI.color = learned ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                var texture = SkillIcons.Get(skill);
                if (texture != null) GUI.DrawTexture(new Rect(icon.x + 2, icon.y + 2, icon.width - 4, icon.height - 4), texture);
                GUI.color = Color.white;

                var kind = skill.Kind == Game.Shared.Combat.SkillKind.Physical ? Loc.T("hud.kind_physical") : Loc.T("hud.kind_magical");
                if (skill.MpCost > 0) kind = Loc.T("hud.kind_with_mp", kind, skill.MpCost);
                GUI.Label(new Rect(x + 54, y + 2, 260, 24), $"<b>{skill.Name}</b>", _small);
                GUI.Label(new Rect(x + 54, y + 24, 260, 22), $"{kind} · {skill.CooldownSeconds:0.#} s", _tiny);

                var status = new Rect(rect.xMax - 194, y + 8, 180, 34);
                if (learned) GUI.Label(status, $"<color=#7fe07f>{Loc.T("skills.learned")}</color>", _small.WithAlignment(TextAnchor.MiddleRight));
                else if (trainer)
                {
                    var refused = SkillTraining.CanLearn(classSkill, level, CharacterState.QuestCompleted, alz, false);
                    var price = SkillTraining.PriceFor(classSkill);
                    if (refused == null)
                    {
                        if (GUI.Button(status, price > 0 ? Loc.T("skills.learn", price) : Loc.T("skills.learn_free"), _button))
                            Send(new Msg.LearnSkill { SkillId = skill.Id });
                    }
                    else GUI.Label(status, Loc.T("skills.need", refused.ToString()), _tiny.WithAlignment(TextAnchor.MiddleRight));
                }
                else GUI.Label(status, Loc.T("skills.need", Player.UnlockText(i)), _tiny.WithAlignment(TextAnchor.MiddleRight));

                // Arrastar da janela para a barra (só o que já foi aprendido).
                if (learned && e.type == EventType.MouseDown && e.button == 0 && icon.Contains(e.mousePosition))
                {
                    BeginSkillDrag(i, -1, e.mousePosition);
                    e.Use();
                }
                y += SkillRow;
            }

            if (trainer) GUI.Label(new Rect(x, rect.yMax - 34, rect.width - 28, 24), Loc.T("skills.your_alz", alz), _small);
            else GUI.Label(new Rect(x, rect.yMax - 46, rect.width - 28, 40), Loc.T("skills.bar_hint"), _tiny);
        }

        /// <summary>Casas da barra: começar a arrastar, clicar para usar e soltar o que está sendo arrastado.</summary>
        private void HandleHotbar()
        {
            if (Player == null) return;
            var count = CombatHud.HotbarCount(Player.Skills.Count);
            for (var slot = 0; slot < count; slot++) OpenRects.Add(CombatHud.HotbarRect(slot, count));

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var slot = HotbarSlotAt(e.mousePosition, count);
                var value = slot >= 0 ? CharacterState.HotbarValue(slot) : SkillTraining.Empty;
                if (value != SkillTraining.Empty)
                {
                    BeginSkillDrag(value, slot, e.mousePosition);
                    e.Use();
                }
            }

            if (_skillDrag < 0) return;
            if (e.type == EventType.MouseDrag && (e.mousePosition - _skillDragStart).sqrMagnitude > 36f) _skillDragging = true;
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (_skillDragging) DropSkill(HotbarSlotAt(e.mousePosition, count));
                else if (_skillDragSlot >= 0) Player.RequestHotbar(_skillDragSlot); // clique simples na casa: usa
                _skillDrag = -1;
                _skillDragging = false;
                e.Use();
            }
            else if (_skillDragging && e.type == EventType.Repaint)
            {
                var item = SkillTraining.ItemOf((byte)_skillDrag);
                var texture = item != null ? ItemIcons.Get(item) : SkillIcons.Get(Player.Skills[_skillDrag]);
                if (texture != null) GUI.DrawTexture(new Rect(e.mousePosition.x - 26, e.mousePosition.y - 26, 52, 52), texture);
            }
        }

        /// <summary>Soltou um item do inventário: se é poção e caiu numa casa da barra, entra nela.</summary>
        private bool DropItemOnHotbar(Vector2 point, ItemInstance item)
        {
            if (Player == null) return false;
            var slot = HotbarSlotAt(point, CombatHud.HotbarCount(Player.Skills.Count));
            var value = SkillTraining.ItemValue(item.ItemId);
            if (slot < 0) return false;
            if (value == SkillTraining.Empty)
            {
                Player.ShowNotice(Loc.T("hotbar.only_potions"));
                return true;
            }
            _skillDrag = value;
            _skillDragSlot = -1;
            DropSkill(slot);
            _skillDrag = -1;
            return true;
        }

        private void BeginSkillDrag(int skill, int slot, Vector2 at)
        {
            _skillDrag = skill;
            _skillDragSlot = slot;
            _skillDragStart = at;
            _skillDragging = false;
        }

        private static int HotbarSlotAt(Vector2 point, int count)
        {
            for (var slot = 0; slot < count; slot++)
                if (CombatHud.HotbarRect(slot, count).Contains(point)) return slot;
            return -1;
        }

        /// <summary>Soltou a skill: numa casa (troca de lugar ou entra) ou fora da barra (sai, se veio da barra).</summary>
        private void DropSkill(int target)
        {
            var bar = new byte[SkillTraining.HotbarSize];
            for (var slot = 0; slot < bar.Length; slot++) bar[slot] = CharacterState.HotbarValue(slot);
            var dragged = (byte)_skillDrag;
            if (target >= 0 && _skillDragSlot >= 0)
            {
                (bar[target], bar[_skillDragSlot]) = (bar[_skillDragSlot], bar[target]);
            }
            else if (target >= 0)
            {
                for (var slot = 0; slot < bar.Length; slot++)
                    if (bar[slot] == dragged) bar[slot] = SkillTraining.Empty;
                bar[target] = dragged;
            }
            else if (_skillDragSlot >= 0) bar[_skillDragSlot] = SkillTraining.Empty;
            else return;

            Fx.UiSounds.Click();
            CharacterState.SetHotbar(bar);
            Send(new Msg.SetHotbar { Slots = new List<byte>(bar) });
        }
    }
}
