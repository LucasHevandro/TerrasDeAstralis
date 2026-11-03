using System;
using System.Collections;
using System.IO;
using Game.Client.Fx;
using Game.Client.Net;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Conferência dos modelos animados no jogo de verdade (a deformação da malha só acontece no loop do jogo):
    /// <c>-posetest pasta</c> põe as classes e os NPCs lado a lado na vila, de frente para uma câmera própria, e tira
    /// uma captura de cada estado do Animator (parado, andar, correr, os golpes, dano, esquiva e morte). Depois fecha.
    /// Com <c>-posemonsters</c> faz o mesmo com os 13 monstros (os bichos em partes são animados por código).
    /// </summary>
    public sealed class PoseSheetTest : MonoBehaviour
    {
        private static readonly string[] Models =
        {
            "class/blade", "class/arcanist", "class/ranger", "class/guardian", "npc/anciao_doran", "npc/ferreira_ilsa", "npc/guarda_volumes_bento",
            "npc/mercadora_lia", "npc/guardia_selene",
        };

        private static readonly string[] Monsters =
        {
            "monster/coelho_saltador", "monster/lobo_cinzento", "monster/javali_furioso", "monster/aranha_da_mata",
            "monster/golem_de_pedra", "monster/espectro", "monster/cavaleiro_caido", "monster/guardiao_das_ruinas",
            "monster/esqueleto_guerreiro", "monster/esqueleto_arqueiro", "monster/cavaleiro_ossudo", "monster/servo_da_cripta",
            "monster/senhor_da_cripta",
        };

        private string _folder = "";
        private string[] _models = Models;
        private float _angle = 200f;
        private Vector3? _weaponEuler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-posetest");
            if (index < 0) return;
            GameMode.SetOffline();
            var test = new GameObject("Teste de poses").AddComponent<PoseSheetTest>();
            if (Array.IndexOf(args, "-posemonsters") >= 0) test._models = Monsters;
            // -posemodel class/guardian: um modelo só, de perto (para conferir arma e mão).
            var single = Array.IndexOf(args, "-posemodel");
            if (single >= 0 && single + 1 < args.Length) test._models = new[] { args[single + 1] };
            // -poseangle 120: para que lado os modelos olham (200 = de frente para a câmera, um pouco de lado).
            var angle = Array.IndexOf(args, "-poseangle");
            if (angle >= 0 && angle + 1 < args.Length && float.TryParse(args[angle + 1], out var degrees)) test._angle = degrees;
            // -poseweapon x,y,z: gira a arma da mão direita (para achar o giro certo de uma arma nova sem refazer o build).
            var weapon = Array.IndexOf(args, "-poseweapon");
            if (weapon >= 0 && weapon + 1 < args.Length)
            {
                var parts = args[weapon + 1].Split(',');
                if (parts.Length == 3 && float.TryParse(parts[0], out var x) && float.TryParse(parts[1], out var y) && float.TryParse(parts[2], out var z))
                    test._weaponEuler = new Vector3(x, y, z);
            }
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "posetest");
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            yield return null;
            // Sem HUD e sem a câmera do jogo: só os modelos.
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var ns = behaviour.GetType().Namespace ?? "";
                if (behaviour != this && (ns.StartsWith("Game.Client.UI") || ns.StartsWith("Game.Client.Combat") || ns.StartsWith("Game.Client.Player")))
                    behaviour.enabled = false;
            }
            if (Camera.main != null) Camera.main.gameObject.SetActive(false);

            var models = _models;
            var monsters = models == Monsters;
            var spacing = monsters ? 3.3f : 2.2f;
            var animators = new Animator[models.Length];
            var rigs = new ProceduralRig[models.Length];
            var origin = World.Ground.Point(0, 12);
            for (var i = 0; i < models.Length; i++)
            {
                var slash = models[i].IndexOf('/');
                var holder = new GameObject(models[i]);
                holder.transform.position = World.Ground.Point(origin.x + (i - (models.Length - 1) / 2f) * spacing, origin.z);
                holder.transform.rotation = Quaternion.Euler(0, _angle, 0);
                var model = VisualCatalog.Spawn(models[i].Substring(0, slash), models[i].Substring(slash + 1), holder.transform);
                if (model == null) continue;
                var visual = model.GetComponent<VisualAnimator>();
                if (visual != null) visual.enabled = false; // o teste controla o Animator
                animators[i] = model.GetComponentInChildren<Animator>();
                if (_weaponEuler is { } euler)
                    foreach (var t in model.GetComponentsInChildren<Transform>(true))
                        if (t.name == "handslot.r")
                            for (var c = 0; c < t.childCount; c++) t.GetChild(c).localRotation = Quaternion.Euler(euler);
                rigs[i] = model.GetComponentInChildren<ProceduralRig>();
            }

            var camera = new GameObject("Câmera das poses").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 32;
            var close = models.Length == 1 && !monsters;
            camera.transform.position = origin + (monsters ? new Vector3(0, 5f, -30f) : close ? new Vector3(0, 1.6f, -5f) : new Vector3(0, 2.2f, -13f));
            camera.transform.LookAt(origin + Vector3.up * (monsters ? 1.6f : 0.9f));

            var states = new[]
            {
                ("Locomoção", 0f), ("Locomoção", 2.2f), ("Locomoção", 6f),
                ("Golpe 0", -1f), ("Golpe 1", -1f), ("Golpe 2", -1f), ("Golpe 3", -1f), ("Golpe 4", -1f), ("Golpe 5", -1f),
                ("Golpe 6", -1f), ("Golpe 7", -1f), ("Golpe 8", -1f), ("Golpe 9", -1f), ("Dano", -1f), ("Esquiva", -1f), ("Sentado", -1f), ("Morte", -1f),
            };
            for (var s = 0; s < states.Length; s++)
            {
                var (name, speed) = states[s];
                // Bichos em partes: animação por código, em tempo real (não congela).
                foreach (var rig in rigs)
                {
                    if (rig == null) continue;
                    rig.Speed = speed >= 0 ? speed : 0f;
                    rig.Dead = name == "Morte";
                    if (name.StartsWith("Golpe")) rig.Trigger("Attack");
                    if (name == "Dano") rig.Trigger("Hit");
                }
                foreach (var animator in animators)
                {
                    if (animator == null) continue;
                    var full = FindState(animator, name);
                    animator.speed = 1f;
                    if (speed >= 0) animator.SetFloat("Speed", speed);
                    animator.Play(full, 0, name == "Morte" ? 0.98f : 0.42f);
                    animator.Update(0f);
                    animator.speed = 0f; // congela a pose
                }
                yield return new WaitForSeconds(name == "Morte" ? 1f : 0.25f);
                var file = Path.Combine(_folder, $"{s:00}-{name.Replace(' ', '_')}{(speed > 0 ? "_" + speed : "")}.png");
                ScreenCapture.CaptureScreenshot(file);
                yield return new WaitForSeconds(0.35f);
            }
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"), "SUCESSO: " + states.Length + " poses\n");
            Application.Quit();
        }

        /// <summary>Hash do estado cujo nome começa com <paramref name="prefix"/> (os golpes têm o nome do clipe junto).</summary>
        private static int FindState(Animator animator, string prefix)
        {
            var controller = animator.runtimeAnimatorController;
            // Os nomes completos dos golpes são "Golpe N: Clipe"; procura pelo prefixo tentando os clipes conhecidos.
            foreach (var clip in controller.animationClips)
            {
                var hash = Animator.StringToHash($"Base Layer.{prefix}: {clip.name}");
                if (animator.HasState(0, hash)) return hash;
            }
            return Animator.StringToHash($"Base Layer.{prefix}");
        }
    }
}
