using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Client.Fx;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Personagens do KayKit (Kay Lousberg, CC0) no jogo: configura a importação, cria os materiais, o Animator
    /// Controller (andar/correr, 10 estilos de golpe, dano, esquiva e morte) e os prefabs que o
    /// <see cref="VisualCatalog"/> procura: <c>Visuals/class/blade|arcanist|ranger</c> e <c>Visuals/npc/&lt;id&gt;</c>.
    /// Menu: Jogo &gt; Visuais &gt; Montar personagens KayKit (ou -executeMethod ...KayKitBuilder.Build).
    /// Os arquivos originais ficam em Assets/Art/KayKit (licenças junto).
    /// </summary>
    internal static class KayKitBuilder
    {
        private const string Root = "Assets/Art/KayKit";
        private const string Characters = Root + "/Personagens";
        private const string Animations = Root + "/Animacoes";
        private const string Weapons = Root + "/Armas";
        private const string Generated = Root + "/Gerado";
        private const string Visuals = "Assets/Resources/Visuals";

        /// <summary>Altura de um personagem no jogo (a cápsula de colisão tem 2 m).</summary>
        private const float CharacterHeight = 1.9f;

        private sealed class Look
        {
            public string Model = "";
            public string RightHand = "";
            public string LeftHand = "";
            public string[] Hide = System.Array.Empty<string>();
            public float Height = CharacterHeight;

            /// <summary>Material no lugar do atlas (ex.: "fantasma" para o espectro) e cor dos olhos.</summary>
            public string Override = "";
            public string Eyes = "glow";

            /// <summary>Classes: leva as cabeças dos outros aventureiros escondidas (os penteados da criação).</summary>
            public bool Heads;
        }

        /// <summary>Aventureiros do pacote com o mesmo esqueleto: a cabeça de um serve no corpo do outro.</summary>
        private static readonly string[] HeadModels = { "Knight", "Mage", "Ranger", "Rogue", "Barbarian" };

        private static readonly Dictionary<string, Look> ClassLooks = new Dictionary<string, Look>
        {
            ["blade"] = new Look { Model = "Knight", RightHand = "sword_1handed", LeftHand = "shield_badge_color", Heads = true },
            ["arcanist"] = new Look { Model = "Mage", RightHand = "staff", Heads = true },
            // Ranger: o arco fica na mão esquerda, como nas animações de arco do pacote.
            ["ranger"] = new Look { Model = "Ranger", LeftHand = "bow_withString", Heads = true },
        };

        private static readonly Dictionary<string, Look> NpcLooks = new Dictionary<string, Look>
        {
            ["anciao_doran"] = new Look { Model = "Rogue_Hooded", LeftHand = "spellbook_open" },
            ["ferreira_ilsa"] = new Look { Model = "Barbarian", RightHand = "axe_1handed" },
            ["guarda_volumes_bento"] = new Look { Model = "Ranger", RightHand = "mug_full" },
            ["mercadora_lia"] = new Look { Model = "Rogue", RightHand = "dagger" },
            ["cacador_rurik"] = new Look { Model = "Ranger", RightHand = "dagger" },
            ["batedora_nara"] = new Look { Model = "Rogue_Hooded", RightHand = "dagger" },
            ["erudita_veska"] = new Look { Model = "Mage", LeftHand = "spellbook_open", RightHand = "staff" },
            ["mestre_aldric"] = new Look { Model = "Mage", RightHand = "sword_1handed", LeftHand = "spellbook_open" },
            ["guardia_selene"] = new Look { Model = "Knight", RightHand = "sword_2handed", Hide = new[] { "Knight_HelmetVisor" } },
            ["escriva_marta"] = new Look { Model = "Rogue", LeftHand = "spellbook_open" },
        };

        /// <summary>Moradores que andam pela praça (só enfeite: não falam nem lutam).</summary>
        private static readonly Dictionary<string, Look> VillagerLooks = new Dictionary<string, Look>
        {
            ["morador_1"] = new Look { Model = "Barbarian", LeftHand = "mug_full" },
            // Crianças (menores): não se confundem com os NPCs, que usam os mesmos modelos em tamanho de adulto.
            ["morador_2"] = new Look { Model = "Rogue", Height = 1.35f },
            ["morador_3"] = new Look { Model = "Ranger", Height = 1.3f },
            ["morador_4"] = new Look { Model = "Mage", LeftHand = "spellbook_open", Hide = new[] { "Mage_Hat" } },
        };

        /// <summary>Monstros mortos-vivos (o pacote Skeletons); os bichos ficam no CreatureBuilder.</summary>
        private static readonly Dictionary<string, Look> MonsterLooks = new Dictionary<string, Look>
        {
            ["esqueleto_guerreiro"] = new Look { Model = "Skeleton_Warrior", RightHand = "Skeleton_Blade", LeftHand = "Skeleton_Shield_Small_A", Height = 2f },
            ["esqueleto_arqueiro"] = new Look { Model = "Skeleton_Rogue", RightHand = "Skeleton_Crossbow", Height = 1.9f },
            ["cavaleiro_ossudo"] = new Look { Model = "Skeleton_Warrior", RightHand = "Skeleton_Axe", LeftHand = "Skeleton_Shield_Large_A", Height = 2.7f },
            ["servo_da_cripta"] = new Look { Model = "Skeleton_Minion", Height = 1.4f },
            ["senhor_da_cripta"] = new Look { Model = "Skeleton_Mage", RightHand = "Skeleton_Staff", Height = 4.4f, Eyes = "glow_roxo" },
            ["cavaleiro_caido"] = new Look { Model = "Skeleton_Warrior", RightHand = "Skeleton_Blade", LeftHand = "Skeleton_Shield_Large_A", Height = 2.3f },
            ["espectro"] = new Look { Model = "Skeleton_Mage", RightHand = "Skeleton_Staff", Height = 2.2f, Override = "fantasma", Eyes = "glow_roxo" },
            ["guardiao_das_ruinas"] = new Look { Model = "Skeleton_Warrior", RightHand = "Skeleton_Axe", LeftHand = "Skeleton_Shield_Large_A", Height = 4.6f, Eyes = "glow_vermelho" },
        };

        [MenuItem("Jogo/Visuais/Montar personagens KayKit")]
        public static void Build()
        {
            Directory.CreateDirectory(Generated);
            ConfigureImports();
            var materials = Materials();
            var controller = BuildController("Personagem", Clip("General", "Idle_A"), Clip("MovementBasic", "Walking_A"),
                Clip("MovementBasic", "Running_A"), Clip("General", "Death_A"));
            var skeleton = BuildController("Esqueleto", Clip("Special", "Skeletons_Idle"), Clip("Special", "Skeletons_Walking"),
                Clip("MovementBasic", "Running_A"), Clip("Special", "Skeletons_Death"));
            foreach (var (id, look) in ClassLooks) MakePrefab($"{Visuals}/class/{id}.prefab", look, materials, controller, weaponFx: true);
            foreach (var (id, look) in NpcLooks) MakePrefab($"{Visuals}/npc/{id}.prefab", look, materials, controller);
            Directory.CreateDirectory($"{Visuals}/villager");
            foreach (var (id, look) in VillagerLooks) MakePrefab($"{Visuals}/villager/{id}.prefab", look, materials, controller);
            foreach (var (id, look) in MonsterLooks) MakePrefab($"{Visuals}/monster/{id}.prefab", look, materials, skeleton);
            AssetDatabase.SaveAssets();
            Debug.Log($"[KayKit] {ClassLooks.Count} classes, {NpcLooks.Count} NPCs e {MonsterLooks.Count} monstros montados.");
        }

        // ===== Importação =====

        private static readonly string[] Looping = { "Idle", "Walking", "Running", "Blocking", "Skeletons_Walking" };

        private static void ConfigureImports()
        {
            foreach (var path in Directory.GetFiles(Root, "*.fbx", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var changed = false;
                if (importer.animationType != ModelImporterAnimationType.Generic)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    changed = true;
                }
                var isAnimation = path.StartsWith(Animations);
                importer.importAnimation = isAnimation;
                if (isAnimation)
                {
                    var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                    foreach (var clip in clips)
                    {
                        var loop = Looping.Any(prefix => clip.name.StartsWith(prefix) || clip.name.Contains("_Idle"));
                        if (clip.loopTime != loop) changed = true;
                        clip.loopTime = loop;
                        clip.lockRootPositionXZ = true;
                        clip.keepOriginalPositionXZ = true;
                    }
                    importer.clipAnimations = clips;
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> Materials()
        {
            var result = new Dictionary<string, Material>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var texturePath in Directory.GetFiles(Characters, "*_texture.png"))
            {
                var name = Path.GetFileNameWithoutExtension(texturePath).Replace("_texture", "");
                var path = $"{Generated}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetWrite.Create(material, path);
                }
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath.Replace('\\', '/')));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.15f);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                result[name] = material;
            }
            // Olhos dos esqueletos.
            var glowPath = $"{Generated}/glow.mat";
            var glow = AssetDatabase.LoadAssetAtPath<Material>(glowPath);
            if (glow == null)
            {
                glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetWrite.Create(glow, glowPath);
            }
            glow.SetColor("_BaseColor", new Color(0.45f, 0.95f, 1f) * 2f);
            result["glow"] = glow;
            result["glow_roxo"] = Solid("glow_roxo", "Universal Render Pipeline/Unlit", new Color(0.8f, 0.45f, 1f) * 2f);
            result["glow_vermelho"] = Solid("glow_vermelho", "Universal Render Pipeline/Unlit", new Color(1f, 0.25f, 0.15f) * 2f);

            // Espectro: o atlas do esqueleto, roxo e translúcido.
            // Unlit: brilho próprio e sem sombra (o Lit translúcido com emissão estoura o compilador de shader).
            var ghost = Solid("fantasma", "Universal Render Pipeline/Unlit", new Color(0.8f, 0.65f, 1f, 0.55f));
            ghost.SetTexture("_BaseMap", result["skeleton"].GetTexture("_BaseMap"));
            ghost.SetFloat("_Surface", 1f);
            ghost.SetFloat("_Blend", 0f);
            ghost.SetFloat("_ZWrite", 0f);
            ghost.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ghost.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ghost.SetOverrideTag("RenderType", "Transparent");
            ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghost.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            result["fantasma"] = ghost;
            return result;
        }

        /// <summary>Material do rastro da arma: partícula sem textura, aditiva (a cor vem do TrailRenderer).</summary>
        private static Material TrailMaterial()
        {
            var material = Solid("rastro", "Universal Render Pipeline/Particles/Unlit", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f); // aditivo
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetTexture("_BaseMap", null); // cor pura: o rastro some pela transparência do fim
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material Solid(string name, string shader, Color color)
        {
            var path = $"{Generated}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetWrite.Create(material, path);
            }
            if (material.shader != Shader.Find(shader)) material.shader = Shader.Find(shader);
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ===== Animator =====

        private static AnimationClip Clip(string file, string name)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{Animations}/Rig_Medium_{file}.fbx").OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (clip == null) throw new System.InvalidOperationException($"Animação {name} não encontrada em Rig_Medium_{file}.");
            return clip;
        }

        private static AnimatorController BuildController(string name, AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip dying)
        {
            var path = $"{Generated}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackKind", AnimatorControllerParameterType.Int);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "AttackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f,
            });
            var machine = controller.layers[0].stateMachine;

            // Parado → andar → correr pela velocidade (m/s) que o jogo passa.
            var locomotion = controller.CreateBlendTreeInController("Locomoção", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 2.2f);
            tree.AddChild(run, 6f);
            machine.defaultState = locomotion;

            // Golpes: um estado por estilo (AttackStyle), todos bem rápidos (o combo pede resposta imediata).
            var attacks = new (int Style, string File, string Clip, float Seconds)[]
            {
                (AttackStyle.Slice, "CombatMelee", "Melee_1H_Attack_Slice_Horizontal", 0.55f),
                (AttackStyle.Chop, "CombatMelee", "Melee_1H_Attack_Chop", 0.55f),
                (AttackStyle.Stab, "CombatMelee", "Melee_1H_Attack_Stab", 0.6f),
                (AttackStyle.Spin, "CombatMelee", "Melee_2H_Attack_Spinning", 0.6f),
                (AttackStyle.JumpChop, "CombatMelee", "Melee_1H_Attack_Jump_Chop", 0.8f),
                (AttackStyle.Diagonal, "CombatMelee", "Melee_1H_Attack_Slice_Diagonal", 0.55f),
                (AttackStyle.MagicShoot, "CombatRanged", "Ranged_Magic_Shoot", 0.55f),
                (AttackStyle.MagicCast, "CombatRanged", "Ranged_Magic_Spellcasting", 0.6f),
                (AttackStyle.MagicRaise, "CombatRanged", "Ranged_Magic_Raise", 0.9f),
                (AttackStyle.Finisher, "CombatMelee", "Melee_2H_Attack_Chop", 0.75f),
                (AttackStyle.RangedShoot, "CombatRanged", "Ranged_2H_Shoot", 0.6f),
            };
            foreach (var attack in attacks)
            {
                var clip = Clip(attack.File, attack.Clip);
                var state = machine.AddState($"Golpe {attack.Style}: {attack.Clip}");
                state.motion = clip;
                state.speed = clip.length / attack.Seconds;
                var enter = machine.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                enter.AddCondition(AnimatorConditionMode.Equals, attack.Style, "AttackKind");
                enter.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                Quick(enter, 0.06f);
                enter.canTransitionToSelf = true;
                Leave(state, locomotion, 0.85f);
            }

            // Golpe próprio de cada skill (SkillMoves): passos em sequência, no ritmo do AttackSpeed (Excellent acelera).
            for (var i = 0; i < SkillMoves.All.Count; i++)
            {
                var move = SkillMoves.All[i];
                var kind = SkillMoves.FirstKind + i;
                AnimatorState previous = null;
                for (var step = 0; step < move.Steps.Length; step++)
                {
                    var (file, clipName, seconds) = move.Steps[step];
                    var clip = Clip(file, clipName);
                    var state = machine.AddState($"Skill {kind}-{step + 1}: {move.Id}");
                    state.motion = clip;
                    state.speed = clip.length / seconds;
                    state.speedParameterActive = true;
                    state.speedParameter = "AttackSpeed";
                    if (previous == null)
                    {
                        var enter = machine.AddAnyStateTransition(state);
                        enter.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                        enter.AddCondition(AnimatorConditionMode.Equals, kind, "AttackKind");
                        enter.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                        Quick(enter, 0.05f);
                        enter.canTransitionToSelf = true;
                    }
                    else
                    {
                        var next = previous.AddTransition(state);
                        next.hasExitTime = true;
                        next.exitTime = 0.94f;
                        next.duration = 0.05f;
                        next.hasFixedDuration = true;
                    }
                    previous = state;
                }
                if (previous != null) Leave(previous, locomotion, 0.9f);
            }

            var hit = machine.AddState("Dano");
            hit.motion = Clip("General", "Hit_A");
            hit.speed = 1.3f;
            var toHit = machine.AddAnyStateTransition(hit);
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            toHit.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Quick(toHit, 0.05f);
            Leave(hit, locomotion, 0.8f);

            // Gestos dos NPCs: conversar (Talk) e trocar a pose parada de vez em quando (Idle2).
            AddGesture(controller, machine, locomotion, "Talk", Clip("General", "Interact"));
            AddGesture(controller, machine, locomotion, "Idle2", Clip("General", "Idle_B"));

            var dodge = machine.AddState("Esquiva");
            dodge.motion = Clip("MovementAdvanced", "Dodge_Forward");
            var toDodge = machine.AddAnyStateTransition(dodge);
            toDodge.AddCondition(AnimatorConditionMode.If, 0, "Dodge");
            toDodge.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Quick(toDodge, 0.04f);
            Leave(dodge, locomotion, 0.9f);

            // Sentado na fogueira: o pacote não tem pose de sentar; o PickUp parado no meio dá a pose agachada.
            controller.AddParameter("Resting", AnimatorControllerParameterType.Bool);
            var rest = machine.AddState("Sentado");
            rest.motion = Clip("General", "PickUp");
            rest.speed = 0f;
            rest.cycleOffset = 0.45f;
            var toRest = locomotion.AddTransition(rest);
            toRest.AddCondition(AnimatorConditionMode.If, 0, "Resting");
            toRest.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Quick(toRest, 0.35f);
            var standUp = rest.AddTransition(locomotion);
            standUp.AddCondition(AnimatorConditionMode.IfNot, 0, "Resting");
            Quick(standUp, 0.25f);

            var death = machine.AddState("Morte");
            death.motion = dying;
            var toDeath = machine.AddAnyStateTransition(death);
            toDeath.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            toDeath.canTransitionToSelf = false;
            Quick(toDeath, 0.1f);
            var revive = death.AddTransition(locomotion);
            revive.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Quick(revive, 0.2f);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddGesture(AnimatorController controller, AnimatorStateMachine machine, AnimatorState locomotion, string trigger,
            AnimationClip clip)
        {
            if (clip == null) return;
            if (controller.parameters.All(p => p.name != trigger))
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
            var state = machine.AddState("Gesto " + trigger);
            state.motion = clip;
            // Só sai do andar/parado (um gesto não corta golpe, dano nem morte).
            var enter = locomotion.AddTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
            enter.AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");
            enter.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Quick(enter, 0.15f);
            Leave(state, locomotion, 0.9f);
        }

        private static void Quick(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.hasFixedDuration = true;
        }

        private static void Leave(AnimatorState state, AnimatorState locomotion, float exitTime)
        {
            var back = state.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = exitTime;
            back.duration = 0.15f;
            back.hasFixedDuration = true;
        }

        // ===== Prefabs =====

        private static void MakePrefab(string path, Look look, Dictionary<string, Material> materials, AnimatorController controller,
            bool weaponFx = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Characters}/{look.Model}.fbx");
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            model.name = look.Model;
            if (!model.TryGetComponent<Animator>(out var animator)) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var name in look.Hide)
            {
                var part = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                if (part != null) part.gameObject.SetActive(false);
            }

            Attach(model, "handslot.r", look.RightHand);
            Attach(model, "handslot.l", look.LeftHand);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                var isEyes = renderer.sharedMaterial != null && renderer.sharedMaterial.name.ToLowerInvariant().Contains("glow");
                for (var i = 0; i < shared.Length; i++)
                    shared[i] = isEyes ? materials[look.Eyes] : look.Override.Length > 0 ? materials[look.Override] : MaterialFor(renderer, look.Model, materials);
                renderer.sharedMaterials = shared;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = false;
            }

            // Escala pela altura (pivô nos pés), igual para todos.
            var bounds = Bounds(model);
            var scale = bounds.size.y > 0.1f ? look.Height / bounds.size.y : 1f;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            if (look.Heads) AddHeads(model, look.Model, materials);

            // Classes: rastro na arma, energia no cajado e avanço nos golpes (WeaponFx).
            if (weaponFx)
            {
                var fx = root.AddComponent<WeaponFx>();
                fx.SetTrailMaterial(TrailMaterial());
                if (look.RightHand.Length == 0 && look.LeftHand.Length > 0) fx.SetHandSlot("handslot.l");
            }

            AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Põe as cabeças dos outros aventureiros no modelo, escondidas e presas aos mesmos ossos (pelo nome). Cada uma
        /// mantém a textura do próprio modelo; o <see cref="CharacterPaint"/> liga a do penteado escolhido.
        /// </summary>
        private static void AddHeads(GameObject model, string own, Dictionary<string, Material> materials)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) bones.TryAdd(t.name, t);
            var ownHead = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == own + "_Head");
            if (ownHead == null) throw new System.InvalidOperationException($"{own} sem {own}_Head.");
            foreach (var other in HeadModels)
            {
                if (other == own) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Characters}/{other}.fbx");
                if (asset == null) continue;
                var temp = Object.Instantiate(asset);
                var source = temp.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == other + "_Head");
                if (source != null && materials.TryGetValue(other.ToLowerInvariant(), out var material))
                {
                    var head = new GameObject(other + "_Head");
                    head.transform.SetParent(ownHead.transform.parent, false);
                    head.transform.localPosition = source.transform.localPosition;
                    head.transform.localRotation = source.transform.localRotation;
                    head.transform.localScale = source.transform.localScale;
                    var skinned = head.AddComponent<SkinnedMeshRenderer>();
                    skinned.sharedMesh = source.sharedMesh;
                    skinned.bones = source.bones.Select(b => b != null && bones.TryGetValue(b.name, out var mine) ? mine : null).ToArray();
                    skinned.rootBone = source.rootBone != null && bones.TryGetValue(source.rootBone.name, out var root) ? root : ownHead.rootBone;
                    skinned.localBounds = source.localBounds;
                    skinned.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, source.sharedMaterials.Length)).ToArray();
                    skinned.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    skinned.updateWhenOffscreen = false;
                    head.SetActive(false);
                }
                Object.DestroyImmediate(temp);
            }
        }

        private static void Attach(GameObject model, string slot, string weapon)
        {
            if (string.IsNullOrEmpty(weapon)) return;
            var hand = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == slot);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Weapons}/{weapon}.fbx");
            if (hand == null || asset == null) throw new System.InvalidOperationException($"Sem {slot} ou {weapon}.");
            var item = (GameObject)PrefabUtility.InstantiatePrefab(asset, hand);
            item.name = weapon;
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one;
        }

        /// <summary>O material do atlas certo: a arma usa a textura que o FBX dela pede; o corpo, a do personagem.</summary>
        private static Material MaterialFor(Renderer renderer, string model, Dictionary<string, Material> materials)
        {
            var original = renderer.sharedMaterial != null ? renderer.sharedMaterial.name.ToLowerInvariant() : "";
            if (original.Contains("glow")) return materials["glow"];
            foreach (var (name, material) in materials)
                if (original.StartsWith(name)) return material;
            var key = model.ToLowerInvariant().Split('_')[0];
            return materials.TryGetValue(key, out var own) ? own : materials.Values.First();
        }

        private static Bounds Bounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
