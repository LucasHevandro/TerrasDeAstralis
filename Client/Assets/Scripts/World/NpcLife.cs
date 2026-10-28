using System.Collections.Generic;
using Game.Client.Fx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client.World
{
    /// <summary>
    /// Vida dos NPCs: viram para o jogador enquanto conversam (e voltam para a pose de antes), fazem um gesto ao abrir a
    /// conversa e, parados, trocam de pose de vez em quando. Os gatilhos <c>Talk</c> e <c>Idle2</c> estão no Animator
    /// dos personagens do KayKit; modelo sem eles só vira.
    /// </summary>
    public sealed class NpcLife : MonoBehaviour
    {
        public string NpcId = "";

        private Quaternion _rest;
        private bool _talking;
        private float _nextIdle;

        private void Start()
        {
            _rest = transform.rotation;
            _nextIdle = Time.time + Random.Range(4f, 12f);
        }

        private void Update()
        {
            var dialog = Net.CharacterState.Dialog;
            var player = Combat.PlayerCombat.Local;
            var talking = dialog != null && dialog.NpcId == NpcId && player != null;
            if (talking && !_talking) VisualAnimator.Trigger(this, "Talk");
            _talking = talking;

            var goal = _rest;
            if (talking)
            {
                var toPlayer = player.transform.position - transform.position;
                toPlayer.y = 0;
                if (toPlayer.sqrMagnitude > 0.01f) goal = Quaternion.LookRotation(toPlayer);
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, 300f * Time.deltaTime);

            if (!talking && Time.time >= _nextIdle)
            {
                VisualAnimator.Trigger(this, "Idle2");
                _nextIdle = Time.time + Random.Range(8f, 16f);
            }
        }
    }

    /// <summary>
    /// Moradores da vila (só enfeite, no cliente): andam devagar entre pontos da praça, param um pouco e seguem.
    /// Não são alvo, não falam e não lutam. Modelos em <c>Resources/Visuals/villager</c> (gerados pelo KayKitBuilder).
    /// </summary>
    public sealed class VillageLife : MonoBehaviour
    {
        private const float WalkSpeed = 1.3f;
        private static readonly string[] Models = { "morador_1", "morador_2", "morador_3", "morador_4" };

        private sealed class Walker
        {
            public Transform Body;
            public Vector3 Goal;
            public float WaitUntil;
            public float Speed;
        }

        private readonly List<Walker> _walkers = new List<Walker>();
        private System.Random _random;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoad()
        {
            // ConnectMenu recarrega a cena ao entrar no jogo. RuntimeInitializeOnLoadMethod roda só na inicialização
            // do aplicativo; assinar o evento recria os moradores depois de cada recarga.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<VillageLife>() != null) return;
            new GameObject("Moradores").AddComponent<VillageLife>();
        }

        private void Start()
        {
            _random = new System.Random(42);
            for (var i = 0; i < Models.Length; i++)
            {
                var root = new GameObject($"Morador {i + 1}").transform;
                root.SetParent(transform, false);
                var start = RandomSpot();
                root.position = Ground.Point(start.x, start.z);
                if (VisualCatalog.Spawn("villager", Models[i], root) == null)
                {
                    Destroy(root.gameObject);
                    continue;
                }
                // As crianças (2 e 3) correm de um lado para o outro; os adultos andam.
                var child = i == 1 || i == 2;
                _walkers.Add(new Walker { Body = root, Goal = RandomSpot(), WaitUntil = Time.time + i * 1.5f, Speed = child ? 3.2f : WalkSpeed });
            }
        }

        /// <summary>Ponto da praça longe do centro (onde o jogador nasce) e dos cantos dos NPCs.</summary>
        private Vector3 RandomSpot()
        {
            for (var tries = 0; tries < 20; tries++)
            {
                var angle = (float)(_random.NextDouble() * Mathf.PI * 2);
                var radius = 6f + (float)_random.NextDouble() * 8f;
                var spot = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                var clear = true;
                foreach (var npc in Game.Shared.Progression.NpcCatalog.All)
                    if ((new Vector2(npc.X, npc.Z) - new Vector2(spot.x, spot.z)).sqrMagnitude < 4.5f * 4.5f) clear = false;
                if (clear) return spot;
            }
            return new Vector3(8f, 0, -8f);
        }

        private void Update()
        {
            foreach (var walker in _walkers)
            {
                if (walker.Body == null || Time.time < walker.WaitUntil) continue;
                var position = walker.Body.position;
                var toGoal = new Vector3(walker.Goal.x - position.x, 0, walker.Goal.z - position.z);
                if (toGoal.magnitude < 0.2f)
                {
                    // Chegou: fica um pouco e escolhe outro lugar.
                    walker.WaitUntil = Time.time + 2f + (float)_random.NextDouble() * 5f;
                    walker.Goal = RandomSpot();
                    continue;
                }
                var step = Vector3.ClampMagnitude(toGoal, walker.Speed * Time.deltaTime);
                walker.Body.position = Ground.Point(position.x + step.x, position.z + step.z);
                walker.Body.rotation = Quaternion.RotateTowards(walker.Body.rotation, Quaternion.LookRotation(toGoal), 360f * Time.deltaTime);
            }
        }
    }
}
