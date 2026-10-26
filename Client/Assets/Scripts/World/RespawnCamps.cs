using Game.Client.Fx;
using Game.Shared.World;
using UnityEngine;

namespace Game.Client.World
{
    /// <summary>
    /// Fogueiras de retorno do mundo aberto (<see cref="WorldContent.RespawnPoints"/>): um braseiro aceso com luz em cada
    /// uma, fora a da vila. Quem morre renasce na mais perto; o braseiro também serve de marco para se orientar.
    /// </summary>
    public sealed class RespawnCamps : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            // A tela de entrada recarrega a cena: um grupo de fogueiras por cena.
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
