using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game.Client.UI
{
    /// <summary>Comandos do jogo que o jogador pode trocar de tecla.</summary>
    public enum GameAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Run, Dodge,
        Skill1, Skill2, Skill3, Skill4, Skill5, Skill6, Skill7, Skill8, Skill9, Skill10,
        StartCombo, Finisher, Awakening, Pickup, NextTarget, Talk,
        Inventory, Attributes, QuestLog, Friends, Map, Options,
        Skills, Progress, Character, Rest,
    }

    /// <summary>
    /// Atalhos de teclado configuráveis, salvos no PlayerPrefs. Esc (fechar e cancelar) e Enter (chat) ficam fixos.
    /// Trocar para uma tecla já usada troca as duas ações de lugar, para nenhuma ficar sem tecla.
    /// </summary>
    public static class KeyBindings
    {
        private const string Prefix = "atalho.";

        private static readonly Dictionary<GameAction, Key> Defaults = new Dictionary<GameAction, Key>
        {
            [GameAction.MoveForward] = Key.W, [GameAction.MoveBack] = Key.S, [GameAction.MoveLeft] = Key.A,
            [GameAction.MoveRight] = Key.D, [GameAction.Run] = Key.LeftShift, [GameAction.Dodge] = Key.Space,
            [GameAction.Skill1] = Key.Digit1, [GameAction.Skill2] = Key.Digit2, [GameAction.Skill3] = Key.Digit3,
            [GameAction.Skill4] = Key.Digit4, [GameAction.Skill5] = Key.Digit5, [GameAction.Skill6] = Key.Digit6,
            [GameAction.Skill7] = Key.Digit7, [GameAction.Skill8] = Key.Digit8,
            [GameAction.Skill9] = Key.Digit9, [GameAction.Skill10] = Key.Digit0,
            [GameAction.StartCombo] = Key.Q, [GameAction.Finisher] = Key.F, [GameAction.Awakening] = Key.R,
            [GameAction.Pickup] = Key.E, [GameAction.NextTarget] = Key.Tab, [GameAction.Talk] = Key.T,
            [GameAction.Inventory] = Key.I, [GameAction.Attributes] = Key.C, [GameAction.QuestLog] = Key.J,
            [GameAction.Friends] = Key.O, [GameAction.Map] = Key.M, [GameAction.Options] = Key.F10,
            [GameAction.Skills] = Key.K, [GameAction.Progress] = Key.L,
            [GameAction.Character] = Key.P, [GameAction.Rest] = Key.X,
        };

        private static readonly Dictionary<GameAction, Key> Current = new Dictionary<GameAction, Key>(Defaults);

        public static IEnumerable<GameAction> All => Defaults.Keys;

        /// <summary>Muda a cada troca de atalho: quem guarda textos com as teclas sabe quando montar de novo.</summary>
        public static int Version { get; private set; }

        /// <summary>Teclas que não podem virar atalho.</summary>
        public static bool IsReserved(Key key) =>
            key == Key.None || key == Key.Escape || key == Key.Enter || key == Key.NumpadEnter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            Current.Clear();
            foreach (var pair in Defaults) Current[pair.Key] = pair.Value;
            try
            {
                foreach (var action in Defaults.Keys)
                {
                    var saved = (Key)PlayerPrefs.GetInt(Prefix + action, (int)Defaults[action]);
                    if (Enum.IsDefined(typeof(Key), saved) && !IsReserved(saved)) Current[action] = saved;
                }
            }
            catch (Exception)
            {
                // Sem PlayerPrefs: ficam os atalhos padrão.
            }
            // Atalhos salvos repetidos (arquivo mexido à mão): quem repete volta ao padrão.
            foreach (var group in Current.GroupBy(p => p.Value).Where(g => g.Count() > 1).ToList())
                foreach (var pair in group.Skip(1)) Current[pair.Key] = Defaults[pair.Key];
            Version++;
        }

        public static void Save()
        {
            try
            {
                foreach (var pair in Current) PlayerPrefs.SetInt(Prefix + pair.Key, (int)pair.Value);
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // Não salvou: os atalhos valem só nesta sessão.
            }
        }

        public static Key Get(GameAction action) => Current[action];

        /// <summary>Muda a tecla da ação; se outra ação usava essa tecla, ela fica com a antiga. Falso se a tecla é reservada.</summary>
        public static bool Set(GameAction action, Key key)
        {
            if (IsReserved(key)) return false;
            var old = Current[action];
            foreach (var pair in Current.ToList())
                if (pair.Key != action && pair.Value == key) Current[pair.Key] = old;
            Current[action] = key;
            Version++;
            Save();
            return true;
        }

        public static void ResetDefaults()
        {
            foreach (var pair in Defaults) Current[pair.Key] = pair.Value;
            Version++;
            Save();
        }

        /// <summary>Apertou neste quadro (nulo enquanto digita no chat ou com as opções abertas, ver <see cref="GameInput"/>).</summary>
        public static bool Pressed(GameAction action) => Control(GameInput.Keyboard, action)?.wasPressedThisFrame ?? false;

        /// <summary>Está segurando.</summary>
        public static bool Held(GameAction action) => Control(GameInput.Keyboard, action)?.isPressed ?? false;

        /// <summary>Apertou neste quadro lendo o teclado direto (para a tecla que abre as opções, que funciona com tudo aberto).</summary>
        public static bool PressedRaw(GameAction action) => Control(Keyboard.current, action)?.wasPressedThisFrame ?? false;

        private static KeyControl Control(Keyboard keyboard, GameAction action) => keyboard == null ? null : keyboard[Current[action]];

        /// <summary>Nome curto da tecla da ação, para os textos da tela.</summary>
        public static string Label(GameAction action) => KeyLabel(Current[action]);

        public static string KeyLabel(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return key.ToString();
            if (key >= Key.Digit1 && key <= Key.Digit9) return ((int)(key - Key.Digit1) + 1).ToString();
            if (key == Key.Digit0) return "0";
            var text = "key." + key.ToString().ToLowerInvariant();
            return Loc.Has(text) ? Loc.T(text) : key.ToString();
        }

        /// <summary>Teclas das skills 1 a <paramref name="count"/>: "1–8" quando são os números em ordem, senão a lista.</summary>
        public static string SkillsLabel(int count)
        {
            var labels = Enumerable.Range(0, count).Select(i => Label(GameAction.Skill1 + i)).ToList();
            var sequential = labels.Select((l, i) => l == (i + 1).ToString()).All(ok => ok);
            return sequential ? $"1–{count}" : string.Join(" ", labels);
        }

        /// <summary>Teclas de andar: "WASD" quando são letras, senão separadas por barra.</summary>
        public static string MoveLabel()
        {
            var labels = new[] { GameAction.MoveForward, GameAction.MoveLeft, GameAction.MoveBack, GameAction.MoveRight }.Select(Label).ToList();
            return labels.All(l => l.Length == 1) ? string.Concat(labels) : string.Join("/", labels);
        }

        /// <summary>Nome da ação na tela de atalhos.</summary>
        public static string ActionName(GameAction action) =>
            action >= GameAction.Skill1 && action <= GameAction.Skill10
                ? Loc.T("action.skill", (int)(action - GameAction.Skill1) + 1)
                : Loc.T("action." + action.ToString().ToLowerInvariant());
    }
}
