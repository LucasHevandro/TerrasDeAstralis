using Game.Client.Fx;
using Game.Shared.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client.World
{
    /// <summary>
    /// Fogueiras de retorno do mundo aberto (<see cref="WorldContent.RespawnPoints"/>): um braseiro aceso com luz em cada
    /// uma, fora a da vila. Quem morre renasce na mais perto; o braseiro também serve de marco para se orientar.
    /// </summary>
    public sealed class RespawnCamps : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoad()
        {
            // ConnectMenu recarrega a cena ao entrar no jogo. RuntimeInitializeOnLoadMethod roda só na inicialização
            // do aplicativo; assinar o evento também recria as fogueiras depois de cada recarga.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<RespawnCamps>() != null) return;
            new GameObject("Fogueiras de retorno").AddComponent<RespawnCamps>();
        }

        private void Start()
        {
            var particle = Resources.Load<Material>("Visuals/fx/particula_suave");
            for (var i = 1; i < WorldContent.RespawnPoints.Count; i++)
            {
                var point = WorldContent.RespawnPoints[i];
                var camp = new GameObject($"Fogueira: {point.Id}").transform;
                camp.SetParent(transform, false);
                camp.position = new Vector3(point.X, Ground.HeightAt(point.X, point.Z), point.Z);
                VisualCatalog.Spawn("dungeon", "braseiro_arena", camp);
                Fire(camp, new Vector3(0, 1.1f, 0), particle);
            }
            RestFire(particle);
        }

        /// <summary>
        /// Fogueira de descanso no centro da praça (<see cref="RestRules"/>): anel de pedras, lenha cruzada e troncos em volta
        /// para sentar. O fogo e os troncos têm colisão; os outros detalhes são decorativos.
        /// </summary>
        private void RestFire(Material particle)
        {
            var camp = new GameObject("Fogueira de descanso").transform;
            camp.SetParent(transform, false);
            camp.position = new Vector3(RestRules.FireX, Ground.HeightAt(RestRules.FireX, RestRules.FireZ), RestRules.FireZ);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material Mat(Color color)
            {
                var material = new Material(shader) { color = color };
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
                return material;
            }
            var stone = Mat(new Color(0.45f, 0.43f, 0.4f));
            var wood = Mat(new Color(0.42f, 0.27f, 0.15f));
            var bark = Mat(new Color(0.3f, 0.2f, 0.12f));

            GameObject Piece(PrimitiveType type, Material material, Vector3 position, Quaternion rotation, Vector3 scale, bool hitbox = false)
            {
                var piece = GameObject.CreatePrimitive(type);
                if (!hitbox) Destroy(piece.GetComponent<Collider>());
                piece.transform.SetParent(camp, false);
                piece.transform.localPosition = position;
                piece.transform.localRotation = rotation;
                piece.transform.localScale = scale;
                piece.GetComponent<Renderer>().sharedMaterial = material;
                return piece;
            }

            // Anel de pedras.
            for (var i = 0; i < 12; i++)
            {
                var angle = i * Mathf.PI * 2f / 12f;
                var size = 0.32f + (i % 3) * 0.05f;
                Piece(PrimitiveType.Sphere, stone, new Vector3(Mathf.Cos(angle) * 0.95f, 0.08f, Mathf.Sin(angle) * 0.95f),
                    Quaternion.Euler(0, i * 37f, 0), new Vector3(size, size * 0.6f, size));
            }
            // Lenha cruzada no meio.
            for (var i = 0; i < 4; i++)
                Piece(PrimitiveType.Cylinder, wood, new Vector3(0, 0.15f, 0), Quaternion.Euler(70, i * 45f, 0), new Vector3(0.14f, 0.45f, 0.14f));
            // Troncos deitados em volta, para sentar.
            for (var i = 0; i < 6; i++)
            {
                var angle = (i * 60f + 30f) * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Cos(angle) * 3.3f, 0.2f, Mathf.Sin(angle) * 3.3f);
                Piece(PrimitiveType.Cylinder, bark, position, Quaternion.Euler(0, -i * 60f - 30f, 90), new Vector3(0.4f, 0.75f, 0.4f), hitbox: true);
            }

            var block = camp.gameObject.AddComponent<CapsuleCollider>();
            block.radius = RestRules.FireRadius;
            block.height = 2f;
            block.center = new Vector3(0, 1f, 0);
            Fire(camp, new Vector3(0, 0.25f, 0), particle);
        }

        /// <summary>Fogo do braseiro (o mesmo do construtor da arena) e uma luz quente.</summary>
        private static void Fire(Transform parent, Vector3 offset, Material material)
        {
            var fire = new GameObject("Fogo");
            fire.transform.SetParent(parent, false);
            fire.transform.localPosition = offset;
            fire.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var particles = fire.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.35f));
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 28;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12;
            shape.radius = 0.25f;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0), new GradientColorKey(new Color(0.9f, 0.25f, 0.05f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            if (material != null) fire.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            var light = new GameObject("Luz do fogo").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = offset + Vector3.up * 0.6f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.range = 9f;
            light.intensity = 2.2f;
            light.gameObject.AddComponent<FlickerLight>();
        }
    }
}
