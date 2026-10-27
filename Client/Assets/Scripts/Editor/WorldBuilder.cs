using Game.Client.Monsters;
using Game.Client.World;
using Game.Shared.Monsters;
using Game.Shared.World;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Monta o mapa: terreno com relevo (TerrainBuilder), cenário (PropBuilder), as zonas com nome e faixa de nível
    /// e os grupos de monstros. Vila inicial (onde ficam os bonecos de treino), três zonas de caça com dificuldade
    /// crescente para o norte e a arena do chefe no fim da estrada.
    /// </summary>
    internal static class WorldBuilder
    {
        public static void Build(Material ring, Material telegraph)
        {
            var world = new GameObject("Mundo").transform;

            // Terreno com relevo e chão pintado por zona, e o cenário em volta (Ground decide onde é plano).
            TerrainBuilder.Build(world);
            PropBuilder.Build(world);
            VillageBuilder.Build(world);
            foreach (var zone in WorldContent.Zones())
                Zone(world, "zone." + zone.Id, zone.MinLevel, zone.MaxLevel, new Vector3(zone.CenterX, 0, zone.CenterZ), zone.Radius);

            foreach (var spawn in WorldContent.Spawns())
            {
                // Variantes (lobo-alfa, aranha-rainha...) usam a forma simples do monstro base.
                if (!MonsterVisuals.Looks.TryGetValue(spawn.Data.Id, out var look) &&
                    !MonsterVisuals.Looks.TryGetValue(Fx.VisualCatalog.BaseOf(spawn.Data.Id), out look))
                    throw new System.InvalidOperationException($"Sem aparência definida para o monstro {spawn.Data.Id}.");
                Spawner(world, Ground.Point(spawn.CenterX, spawn.CenterZ), spawn.Count, spawn.Radius, look.Shape,
                    look.Scale, look.Color, ring, telegraph, spawn.Data, spawn.IsBoss);
            }
        }

        private static void Zone(Transform parent, string nameKey, int minLevel, int maxLevel, Vector3 center, float radius)
        {
            var zone = new GameObject($"Zona: {nameKey}");
            zone.transform.SetParent(parent, false);
            zone.transform.position = Ground.Snap(center);
            zone.AddComponent<ZoneArea>().Configure(nameKey, minLevel, maxLevel, radius);
        }

        private static void Spawner(Transform parent, Vector3 position, int count, float radius, MonsterShape shape,
            Vector3 scale, Color color, Material ring, Material telegraph, MonsterData data, bool boss = false)
        {
            data.Validate();
            var spawner = new GameObject($"Spawner: {data.Name}");
            spawner.transform.SetParent(parent, false);
            spawner.transform.position = position;
            spawner.AddComponent<MonsterSpawner>().Configure(data, count, radius, shape, scale,
                TrainingSceneBuilder.MaterialFor($"Monstro_{data.Id}", color), telegraph, ring, boss);
        }
    }
}
