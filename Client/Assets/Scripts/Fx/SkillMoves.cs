using System.Collections.Generic;

namespace Game.Client.Fx
{
    /// <summary>
    /// O "golpe" de cada skill: que animações tocam (em sequência), quando a arma acerta ou a magia sai, quanto o corpo
    /// avança e quando a arma deixa rastro. Só visual: dano, alcance e recarga continuam no Shared (e no servidor).
    /// O Animator ganha um estado por passo (KayKitBuilder); <see cref="AttackStyle.For"/> escolhe o golpe da skill.
    /// </summary>
    public sealed class SkillMove
    {
        public string Id = "";

        /// <summary>Animações em sequência: arquivo Rig_Medium_*, clipe e quanto dura no jogo (segundos).</summary>
        public (string File, string Clip, float Seconds)[] Steps = System.Array.Empty<(string, string, float)>();

        /// <summary>
        /// O mesmo golpe no esqueleto grande (Rig_Large_*, o Guardião). Nulo nas skills das outras classes: o Animator do
        /// Rig_Large só tem estado para as skills que preenchem este campo.
        /// </summary>
        public (string File, string Clip, float Seconds)[] LargeSteps;

        /// <summary>Segundos depois do aperto em que a arma acerta (físicas) ou a magia sai da ponta do cajado (mágicas).</summary>
        public float[] Impacts = { 0.2f };

        /// <summary>Quanto o corpo avança (metros, só o modelo) e em que momento chega lá.</summary>
        public float Lunge;

        public float LungeAt = 0.2f;

        /// <summary>Janela do rastro na arma (segundos depois do aperto).</summary>
        public float TrailFrom = 0.05f;

        public float TrailTo = 0.3f;

        /// <summary>Magia que junta energia na ponta do cajado antes de soltar.</summary>
        public bool Charge;

        /// <summary>Sem rastro na arma (o arco não corta o ar).</summary>
        public bool NoTrail;

        public float FirstImpact => Impacts.Length > 0 ? Impacts[0] : 0f;
    }

    public static class SkillMoves
    {
        /// <summary>Os estados das skills no Animator começam neste AttackKind (os estilos genéricos vêm antes).</summary>
        public const int FirstKind = 20;

        public static readonly IReadOnlyList<SkillMove> All = new List<SkillMove>
        {
            // ----- Lâmina -----
            new SkillMove { Id = "corte", Steps = new[] { ("CombatMelee", "Melee_1H_Attack_Slice_Horizontal", 0.45f) },
                Impacts = new[] { 0.17f }, Lunge = 0.15f, LungeAt = 0.15f, TrailFrom = 0.06f, TrailTo = 0.26f },
            new SkillMove { Id = "giro_de_lamina", Steps = new[] { ("CombatMelee", "Melee_2H_Attack_Spinning", 0.55f) },
                Impacts = new[] { 0.24f }, TrailFrom = 0.04f, TrailTo = 0.45f },
            new SkillMove { Id = "golpe_runico", Steps = new[] { ("CombatMelee", "Melee_1H_Attack_Chop", 0.6f) },
                Impacts = new[] { 0.3f }, Lunge = 0.25f, LungeAt = 0.28f, TrailFrom = 0.18f, TrailTo = 0.36f },
            new SkillMove
            {
                Id = "estocada_dupla",
                Steps = new[] { ("CombatMelee", "Melee_1H_Attack_Stab", 0.34f), ("CombatMelee", "Melee_Dualwield_Attack_Stab", 0.4f) },
                Impacts = new[] { 0.16f, 0.5f }, Lunge = 0.5f, LungeAt = 0.16f, TrailFrom = 0.08f, TrailTo = 0.58f,
            },
            new SkillMove { Id = "fenda_sismica", Steps = new[] { ("CombatMelee", "Melee_1H_Attack_Jump_Chop", 0.85f) },
                Impacts = new[] { 0.55f }, Lunge = 0.9f, LungeAt = 0.5f, TrailFrom = 0.38f, TrailTo = 0.6f },
            new SkillMove { Id = "lamina_flamejante", Steps = new[] { ("CombatMelee", "Melee_1H_Attack_Slice_Diagonal", 0.55f) },
                Impacts = new[] { 0.22f }, Lunge = 0.2f, LungeAt = 0.2f, TrailFrom = 0.02f, TrailTo = 0.34f },
            new SkillMove
            {
                Id = "tempestade_de_aco",
                Steps = new[] { ("CombatMelee", "Melee_2H_Attack_Spinning", 0.5f), ("CombatMelee", "Melee_2H_Attack_Spin", 0.75f) },
                Impacts = new[] { 0.25f, 0.85f }, Lunge = 0.3f, LungeAt = 0.8f, TrailFrom = 0.04f, TrailTo = 1.1f,
            },
            new SkillMove { Id = "execucao", Steps = new[] { ("CombatMelee", "Melee_2H_Attack_Chop", 0.95f) },
                Impacts = new[] { 0.62f }, Lunge = 0.7f, LungeAt = 0.6f, TrailFrom = 0.45f, TrailTo = 0.68f },

            // ----- Arcanista -----
            new SkillMove { Id = "faisca", Steps = new[] { ("CombatRanged", "Ranged_Magic_Shoot", 0.45f) },
                Impacts = new[] { 0.16f } },
            new SkillMove { Id = "lanca_de_forca", Steps = new[] { ("CombatRanged", "Ranged_Magic_Shoot", 0.6f) },
                Impacts = new[] { 0.28f }, Charge = true },
            new SkillMove { Id = "golpe_de_cajado", Steps = new[] { ("CombatMelee", "Melee_1H_Attack_Chop", 0.55f) },
                Impacts = new[] { 0.27f }, Lunge = 0.25f, LungeAt = 0.25f, TrailFrom = 0.15f, TrailTo = 0.32f },
            new SkillMove { Id = "explosao_arcana", Steps = new[] { ("CombatRanged", "Ranged_Magic_Spellcasting", 0.75f) },
                Impacts = new[] { 0.42f }, Charge = true },
            new SkillMove
            {
                Id = "corrente_gelida",
                Steps = new[] { ("CombatRanged", "Ranged_Magic_Spellcasting", 0.45f), ("CombatRanged", "Ranged_Magic_Shoot", 0.4f) },
                Impacts = new[] { 0.58f }, Charge = true,
            },
            new SkillMove { Id = "meteoro", Steps = new[] { ("CombatRanged", "Ranged_Magic_Raise", 1f) },
                Impacts = new[] { 0.55f }, Charge = true },
            new SkillMove
            {
                Id = "raio_duplo",
                Steps = new[] { ("CombatRanged", "Ranged_Magic_Shoot", 0.34f), ("CombatRanged", "Ranged_Magic_Shoot", 0.36f) },
                Impacts = new[] { 0.15f, 0.48f },
            },
            new SkillMove { Id = "cataclismo", Steps = new[] { ("CombatRanged", "Ranged_Magic_Summon", 1.5f) },
                Impacts = new[] { 0.95f }, Charge = true },

            // ----- Ranger (arco na mão esquerda: puxa a corda e solta) -----
            new SkillMove
            {
                Id = "tiro_certeiro",
                Steps = new[] { ("CombatRanged", "Ranged_Bow_Draw", 0.25f), ("CombatRanged", "Ranged_Bow_Release", 0.25f) },
                Impacts = new[] { 0.27f }, NoTrail = true,
            },
            new SkillMove
            {
                Id = "flecha_perfurante",
                Steps = new[] { ("CombatRanged", "Ranged_Bow_Draw", 0.35f), ("CombatRanged", "Ranged_Bow_Release", 0.3f) },
                Impacts = new[] { 0.37f }, Charge = true, NoTrail = true,
            },
            new SkillMove { Id = "chute_de_recuo", Steps = new[] { ("CombatMelee", "Melee_Unarmed_Attack_Kick", 0.5f) },
                Impacts = new[] { 0.22f }, Lunge = 0.2f, LungeAt = 0.2f, NoTrail = true },
            new SkillMove
            {
                Id = "chuva_de_flechas",
                Steps = new[] { ("CombatRanged", "Ranged_Bow_Draw_Up", 0.4f), ("CombatRanged", "Ranged_Bow_Release_Up", 0.4f) },
                Impacts = new[] { 0.42f }, NoTrail = true,
            },
            new SkillMove
            {
                Id = "tiro_duplo",
                Steps = new[]
                {
                    ("CombatRanged", "Ranged_Bow_Draw", 0.2f), ("CombatRanged", "Ranged_Bow_Release", 0.18f),
                    ("CombatRanged", "Ranged_Bow_Draw", 0.15f), ("CombatRanged", "Ranged_Bow_Release", 0.22f),
                },
                Impacts = new[] { 0.22f, 0.55f }, NoTrail = true,
            },
            new SkillMove
            {
                Id = "flecha_flamejante",
                Steps = new[] { ("CombatRanged", "Ranged_Bow_Draw", 0.45f), ("CombatRanged", "Ranged_Bow_Release", 0.4f) },
                Impacts = new[] { 0.47f }, Charge = true, NoTrail = true,
            },
            new SkillMove
            {
                Id = "saraivada",
                Steps = new[]
                {
                    ("CombatRanged", "Ranged_Bow_Draw_Up", 0.45f), ("CombatRanged", "Ranged_Bow_Release_Up", 0.25f),
                    ("CombatRanged", "Ranged_Bow_Draw_Up", 0.15f), ("CombatRanged", "Ranged_Bow_Release_Up", 0.25f),
                },
                Impacts = new[] { 0.47f }, NoTrail = true,
            },
            new SkillMove
            {
                Id = "tiro_do_falcao",
                Steps = new[] { ("CombatRanged", "Ranged_Bow_Draw", 0.75f), ("CombatRanged", "Ranged_Bow_Release", 0.55f) },
                Impacts = new[] { 0.78f }, Charge = true, NoTrail = true,
            },

            // ----- Guardião (Rig_Large; os Steps só dão o estado também ao Animator do Rig_Medium) -----
            new SkillMove
            {
                Id = "golpe_de_machado", Steps = new[] { ("CombatMelee", "Melee_1H_Attack_Chop", 0.5f) },
                LargeSteps = new[] { ("CombatMelee", "Melee_1H_Slash", 0.5f) },
                Impacts = new[] { 0.22f }, Lunge = 0.15f, LungeAt = 0.2f, TrailFrom = 0.08f, TrailTo = 0.3f,
            },
            new SkillMove
            {
                Id = "grito_de_guerra", Steps = new[] { ("CombatMelee", "Melee_Unarmed_Attack_Punch_A", 0.6f) },
                LargeSteps = new[] { ("CombatMelee", "Melee_Unarmed_Smash", 0.6f) },
                Impacts = new[] { 0.32f }, NoTrail = true,
            },
            new SkillMove
            {
                Id = "investida_de_escudo", Steps = new[] { ("CombatMelee", "Melee_Block_Attack", 0.55f) },
                LargeSteps = new[] { ("CombatMelee", "Melee_Block_Attack", 0.55f) },
                Impacts = new[] { 0.25f }, Lunge = 0.6f, LungeAt = 0.22f, NoTrail = true,
            },
            new SkillMove
            {
                Id = "golpe_esmagador", Steps = new[] { ("CombatMelee", "Melee_2H_Attack_Chop", 0.75f) },
                LargeSteps = new[] { ("CombatMelee", "Melee_2H_Attack", 0.75f) },
                Impacts = new[] { 0.4f }, Lunge = 0.3f, LungeAt = 0.38f, TrailFrom = 0.25f, TrailTo = 0.45f,
            },
            new SkillMove
            {
                Id = "desafio", Steps = new[] { ("CombatMelee", "Melee_Unarmed_Attack_Punch_A", 0.5f) },
                LargeSteps = new[] { ("CombatMelee", "Melee_Unarmed_Punch", 0.5f) },
                Impacts = new[] { 0.22f }, NoTrail = true,
            },
            new SkillMove
            {
                Id = "terremoto", Steps = new[] { ("CombatMelee", "Melee_1H_Attack_Jump_Chop", 0.85f) },
                LargeSteps = new[] { ("CombatMelee", "Melee_2H_Slam", 0.85f) },
                Impacts = new[] { 0.55f }, TrailFrom = 0.35f, TrailTo = 0.6f,
            },
            new SkillMove
            {
                Id = "furia_do_urso", Steps = new[] { ("CombatMelee", "Melee_2H_Attack_Spin", 1.2f) },
                LargeSteps = new[] { ("CombatMelee", "Melee_Dualwield_SlashCombo", 1.2f) },
                Impacts = new[] { 0.35f, 0.75f }, Lunge = 0.3f, LungeAt = 0.3f, TrailFrom = 0.2f, TrailTo = 0.9f,
            },
            new SkillMove
            {
                Id = "colosso",
                Steps = new[] { ("CombatMelee", "Melee_2H_Attack_Chop", 0.45f), ("CombatMelee", "Melee_1H_Attack_Jump_Chop", 0.55f) },
                LargeSteps = new[] { ("CombatMelee", "Melee_2H_Attack", 0.45f), ("CombatMelee", "Melee_2H_Slam", 0.55f) },
                Impacts = new[] { 0.3f, 0.8f }, Lunge = 0.8f, LungeAt = 0.7f, TrailFrom = 0.15f, TrailTo = 0.9f,
            },
        };

        private static Dictionary<string, int> _index;

        public static SkillMove For(string skillId)
        {
            var index = IndexOf(skillId);
            return index >= 0 ? All[index] : null;
        }

        public static int IndexOf(string skillId)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, int>();
                for (var i = 0; i < All.Count; i++) _index[All[i].Id] = i;
            }
            return skillId != null && _index.TryGetValue(skillId, out var found) ? found : -1;
        }
    }
}
