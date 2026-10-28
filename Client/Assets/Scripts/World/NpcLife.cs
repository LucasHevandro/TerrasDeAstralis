using System.Collections.Generic;
using Game.Client.Fx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client.World
{
    /// <summary>
    /// Vida dos NPCs: viram para o jogador enquanto conversam (e voltam para a pose de antes), fazem um gesto ao abrir a
    /// conversa e, parados, alternam poses e acenam. Os gatilhos <c>Talk</c>, <c>Idle2</c> e <c>Waving</c> estão no Animator
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
                VisualAnimator.Trigger(this, Random.value < 0.3f ? "Waving" : "Idle2");
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
            public CharacterController Collision;
            public Vector3 Goal;
            public float WaitUntil;
            public float Speed;
            public float Radius;
            public float BlockedFor;
            public float GestureAt;
            public bool GesturePlayed;
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
                var height = child ? 1.4f : 1.85f;
                var radius = child ? 0.28f : 0.36f;
                var collision = root.gameObject.AddComponent<CharacterController>();
                collision.height = height;
                collision.radius = radius;
                collision.center = new Vector3(0, height * 0.5f, 0);
                collision.stepOffset = 0.25f;
                collision.slopeLimit = 45f;
                _walkers.Add(new Walker
                {
                    Body = root,
                    Collision = collision,
                    Goal = RandomSpot(),
                    WaitUntil = Time.time + i * 1.5f,
                    GesturePlayed = true,
                    Speed = child ? 3.2f : WalkSpeed,
                    Radius = radius,
                });
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
                if (!clear || IsBlockedSpot(spot)) continue;
                return spot;
            }
            return new Vector3(8f, 0, -8f);
        }

        private static bool IsBlockedSpot(Vector3 spot)
        {
            var ground = Ground.Point(spot.x, spot.z);
            var bottom = ground + Vector3.up * 0.45f;
            var top = ground + Vector3.up * 1.55f;
            return Physics.CheckCapsule(bottom, top, 0.35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private Vector3 AvoidNearby(Walker walker, Vector3 desired)
        {
            var position = walker.Body.position;
            var push = Vector3.zero;
            AddRepulsion(ref push, position, desired, Combat.PlayerCombat.Local != null ? Combat.PlayerCombat.Local.transform : null, walker.Radius + 0.55f);

            var session = Game.Client.Net.NetworkSession.Instance;
            if (session != null)
            {
                foreach (var view in session.Views.Values)
                    if (view != null && view.Kind == Game.Shared.Net.EntityKind.Player)
                        AddRepulsion(ref push, position, desired, view.transform, walker.Radius + 0.55f);
                foreach (var npc in session.NpcViews.Values)
                    AddRepulsion(ref push, position, desired, npc, walker.Radius + 0.5f);
            }

            foreach (var other in _walkers)
                if (other != walker && other.Body != null)
                    AddRepulsion(ref push, position, desired, other.Body, walker.Radius + other.Radius + 0.2f);

            var steered = desired + push;
            return steered.sqrMagnitude > 0.001f ? steered.normalized : desired;
        }

        private static void AddRepulsion(ref Vector3 push, Vector3 position, Vector3 desired, Transform obstacle, float clearance)
        {
            if (obstacle == null) return;
            var away = position - obstacle.position;
            away.y = 0;
            var distance = away.magnitude;
            if (distance >= clearance) return;
            if (distance < 0.001f)
            {
                away = Vector3.Cross(Vector3.up, desired);
                if (away.sqrMagnitude < 0.001f) away = Vector3.right;
                distance = 0;
            }
            else away /= distance;

            push += away * (1f - distance / clearance) * 2f;
        }

        private void Update()
        {
            foreach (var walker in _walkers)
            {
                if (walker.Body == null) continue;
                if (Time.time < walker.WaitUntil)
                {
                    if (!walker.GesturePlayed && Time.time >= walker.GestureAt)
                    {
                        var gestures = new[] { "Waving", "Cheering", "PushUps", "SitUps" };
                        VisualAnimator.Trigger(walker.Body, gestures[_random.Next(gestures.Length)]);
                        walker.GesturePlayed = true;
                        walker.WaitUntil = Mathf.Max(walker.WaitUntil, Time.time + 3.5f);
                    }
                    continue;
                }
                var position = walker.Body.position;
                var toGoal = new Vector3(walker.Goal.x - position.x, 0, walker.Goal.z - position.z);
                if (toGoal.magnitude < 0.2f)
                {
                    // Chegou: fica um pouco e escolhe outro lugar.
                    walker.WaitUntil = Time.time + 2f + (float)_random.NextDouble() * 5f;
                    walker.GestureAt = Time.time + 0.6f;
                    walker.GesturePlayed = false;
                    walker.Goal = RandomSpot();
                    continue;
                }
                var direction = AvoidNearby(walker, toGoal.normalized);
                var step = Vector3.ClampMagnitude(direction * walker.Speed, walker.Speed) * Time.deltaTime;
                var flags = walker.Collision != null
                    ? walker.Collision.Move(step)
                    : CollisionFlags.None;
                var moved = walker.Body.position;
                var grounded = Ground.Point(moved.x, moved.z);
                walker.Body.position = grounded;
                walker.Body.rotation = Quaternion.RotateTowards(walker.Body.rotation, Quaternion.LookRotation(direction), 360f * Time.deltaTime);

                if ((flags & CollisionFlags.Sides) != 0)
                {
                    walker.BlockedFor += Time.deltaTime;
                    if (walker.BlockedFor >= 0.8f)
                    {
                        walker.Goal = RandomSpot();
                        walker.BlockedFor = 0;
                    }
                }
                else walker.BlockedFor = 0;
            }
        }
    }
}
