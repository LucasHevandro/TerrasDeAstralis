using System.IO;
using Game.Client.Fx;
using Game.Client.World;
using Game.Shared.Progression;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Vida na vila e no mundo: o rio (água que corre) com a ponte de madeira na estrada, o canto de cada NPC (forja e
    /// bigorna da ferreira, barraca da mercadora, caixotes e barris do guarda-volumes, estante do ancião, arco de pedra
    /// do portal da Cripta), postes com lanterna, e os bonecos de palha do treino. Chamado pelo WorldBuilder.
    /// </summary>
    internal static class VillageBuilder
    {
        private const string MeshFolder = TerrainBuilder.Folder + "/Malhas";
        private static int _parts;

        private static Material Mat(string name, Color color) => TrainingSceneBuilder.MaterialFor($"Vila_{name}", color);

        private static Material Glow(string name, Color color) =>
            TrainingSceneBuilder.MaterialFor($"Vila_{name}", color * 1.6f, "Universal Render Pipeline/Unlit");

        public static void Build(Transform world)
        {
            Directory.CreateDirectory(MeshFolder);
            var root = new GameObject("Vila e rio").transform;
            root.SetParent(world, false);
            River(root);
            Bridge(root);
            NpcCorners(root);
            Lamps(root);
        }

        private static GameObject Part(Transform parent, string name, Material material, System.Action<MeshKit> draw, bool collider = false)
        {
            var kit = new MeshKit(1);
            draw(kit);
            var mesh = kit.Build($"vila_{name}_{++_parts}");
            var path = $"{MeshFolder}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetWrite.Create(mesh, path);
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider)
            {
                var box = obj.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }
            GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.BatchingStatic);
            return obj;
        }

        /// <summary>
        /// Objeto do KayKit Medieval (barril, caixote, barraca, portão...) no canto, com todas as peças do modelo e o atlas
        /// do pacote; falso se o pacote não está no projeto.
        /// </summary>
        private static bool PackProp(Transform parent, string model, Vector3 position, float yaw, float scale, bool collider = false)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{PackMeshes.Village}/{model}.fbx");
            if (source == null) return false;
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            PrefabUtility.UnpackPrefabInstance(obj, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            obj.name = model;
            obj.transform.localPosition = position;
            obj.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            obj.transform.localScale = Vector3.one * scale;
            var atlas = PackMeshes.Atlas(PackMeshes.Village, "hexagons_medieval.png", "vila");
            var renderers = obj.GetComponentsInChildren<MeshRenderer>();
            foreach (var renderer in renderers)
            {
                var shared = new Material[renderer.sharedMaterials.Length];
                for (var m = 0; m < shared.Length; m++) shared[m] = atlas;
                renderer.sharedMaterials = shared;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
            }
            if (collider && renderers.Length > 0)
            {
                // Uma caixa só, do tamanho do modelo (em coordenadas do objeto), sem a base hexagonal do pacote: só os
                // vértices acima de 35 cm contam. Com a base, a caixa da ferraria cobria até a ferreira.
                var bounds = new Bounds();
                var any = false;
                foreach (var filter in obj.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null) continue;
                    foreach (var vertex in filter.sharedMesh.vertices)
                    {
                        var world = filter.transform.TransformPoint(vertex);
                        if (world.y - obj.transform.position.y < 0.35f) continue;
                        var local = obj.transform.InverseTransformPoint(world);
                        if (!any) bounds = new Bounds(local, Vector3.zero);
                        else bounds.Encapsulate(local);
                        any = true;
                    }
                }
                if (!any) return true;
                bounds.Encapsulate(new Vector3(bounds.center.x, 0f, bounds.center.z)); // até o chão
                var box = obj.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
            return true;
        }

        private static Transform Group(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.position = Ground.Point(position.x, position.z);
            group.rotation = rotation;
            return group;
        }

        // ===== Rio =====

        /// <summary>Faixa de água seguindo a curva do rio, de uma ponta à outra da área jogável, com textura que corre.</summary>
        private static void River(Transform parent)
        {
            var mesh = new Mesh { name = "rio_agua" };
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();
            const float halfWidth = 4.6f;
            var segments = 0;
            for (var x = -128f; x <= 128f; x += 2f)
            {
                var z = Ground.RiverZ(x);
                vertices.Add(new Vector3(x, Ground.WaterLevel, z - halfWidth));
                vertices.Add(new Vector3(x, Ground.WaterLevel, z + halfWidth));
                uvs.Add(new Vector2(x / 8f, 0));
                uvs.Add(new Vector2(x / 8f, 1));
                if (segments > 0)
                {
                    var i = vertices.Count - 4;
                    triangles.AddRange(new[] { i, i + 1, i + 2, i + 1, i + 3, i + 2 });
                }
                segments++;
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var path = $"{MeshFolder}/rio_agua.asset";
            AssetDatabase.DeleteAsset(path);
            AssetWrite.Create(mesh, path);

            // Shader próprio: espuma nas margens, reflexo do céu e ondinhas (Shaders/Agua.shader).
            var shaded = Shader.Find("Jogo/Agua") != null;
            if (shaded)
            {
                var custom = TrainingSceneBuilder.MaterialFor("Agua", Color.white, "Jogo/Agua");
                custom.shaderKeywords = new string[0];
                custom.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(custom);
                RiverObject(parent, mesh, custom, false);
                return;
            }

            var water = TrainingSceneBuilder.MaterialFor("Agua", new Color(0.25f, 0.5f, 0.62f, 0.72f), "Universal Render Pipeline/Unlit");
            water.SetFloat("_Surface", 1f);
            water.SetFloat("_Blend", 0f);
            water.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            water.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            water.SetFloat("_ZWrite", 0f);
            water.SetOverrideTag("RenderType", "Transparent");
            water.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            water.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            water.SetTexture("_BaseMap", TerrainBuilder.GroundTexture("agua", new Color(0.55f, 0.75f, 0.85f), new Color(0.95f, 1f, 1f), 0.04f, 31));
            EditorUtility.SetDirty(water);
            RiverObject(parent, mesh, water, true);
        }

        private static void RiverObject(Transform parent, Mesh mesh, Material material, bool flowingTexture)
        {
            var river = new GameObject("Rio");
            river.transform.SetParent(parent, false);
            river.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = river.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (flowingTexture) river.AddComponent<FlowingWater>();
        }

        /// <summary>Ponte de madeira em arco onde a estrada cruza o rio.</summary>
        private static void Bridge(Transform parent)
        {
            var z = Ground.RiverZ(0);
            var bridge = Group(parent, "Ponte", new Vector3(0, 0, z), Quaternion.identity);
            // As alturas do arco já são do mundo (Ground.BridgeDeck): a ponte fica no zero, não "no chão", que ali já
            // inclui as próprias tábuas e a ergueria (o personagem ficaria dentro delas).
            bridge.position = new Vector3(0, 0, z);
            var wood = Mat("madeira_ponte", new Color(0.48f, 0.33f, 0.2f));
            var dark = Mat("madeira_escura", new Color(0.3f, 0.2f, 0.13f));
            Part(bridge, "Tábuas", wood, k =>
            {
                for (var i = -Ground.BridgePlanks; i <= Ground.BridgePlanks; i++)
                {
                    var t = i / (float)Ground.BridgePlanks;
                    k.Box(0, new Vector3(0, Ground.BridgeArch(t), i * Ground.BridgePlankStep), Quaternion.identity,
                        new Vector3(Ground.BridgeHalfWidth * 2f, Ground.BridgePlankThickness, 0.5f));
                }
            });
            // Corrimão: um de cada lado, cada um com a própria caixa de colisão (só se entra e sai pelas pontas).
            foreach (var side in new[] { -1f, 1f })
            {
                Part(bridge, side < 0 ? "Corrimão esquerdo" : "Corrimão direito", dark, k =>
                {
                    for (var i = -2; i <= 2; i++)
                        k.Box(0, new Vector3(side * 2.35f, -0.1f, i * 2.4f), Quaternion.identity, new Vector3(0.16f, 1.25f, 0.16f));
                    k.Box(0, new Vector3(side * 2.35f, 0.95f, 0), Quaternion.identity, new Vector3(0.12f, 0.1f, 10f));
                }, collider: true);
            }
            // Estrutura: vigas sob as tábuas e pilares até o leito (o rio passa por baixo).
            Part(bridge, "Vigas e pilares", dark, k =>
            {
                foreach (var x in new[] { -1.6f, 0f, 1.6f })
                    k.Box(0, new Vector3(x, -0.3f, 0), Quaternion.identity, new Vector3(0.22f, 0.22f, 10.4f));
                foreach (var x in new[] { -1.9f, 1.9f })
                foreach (var z in new[] { -2.6f, 2.6f })
                    k.Box(0, new Vector3(x, -1.9f, z), Quaternion.identity, new Vector3(0.3f, 1.75f, 0.3f));
            });
        }

        // ===== Cantos dos NPCs =====

        private static void NpcCorners(Transform parent)
        {
            var wood = Mat("madeira", new Color(0.5f, 0.35f, 0.22f));
            var darkWood = Mat("madeira_escura", new Color(0.3f, 0.2f, 0.13f));
            var iron = Mat("ferro", new Color(0.25f, 0.25f, 0.27f));
            var stone = Mat("pedra", new Color(0.55f, 0.53f, 0.5f));
            var clothRed = Mat("pano_vermelho", new Color(0.7f, 0.22f, 0.2f));
            var clothBlue = Mat("pano_azul", new Color(0.25f, 0.4f, 0.65f));
            var ember = Glow("brasa", new Color(1f, 0.45f, 0.1f));
            var portalGlow = Glow("portal", new Color(0.55f, 0.35f, 1f));
            var books = Mat("livros", new Color(0.55f, 0.25f, 0.3f));
            var r = new System.Random(7);

            foreach (var npc in NpcCatalog.All)
            {
                // O canto fica atrás do NPC (mais longe do centro da praça), virado para a praça.
                var at = new Vector3(npc.X, 0, npc.Z);
                var outward = at.sqrMagnitude > 0.01f ? at.normalized : Vector3.forward;
                var spot = at + outward * 2.2f;
                // NPC na saída da estrada (o ancião): o canto vai para o lado, para não fechar o caminho.
                if (Ground.DistanceToRoad(spot.x, spot.z) < 3.5f)
                {
                    outward = new Vector3(outward.z, 0, -outward.x);
                    spot = at + outward * 3f;
                }
                var facing = Quaternion.LookRotation(-outward);
                var corner = Group(parent, $"Canto: {npc.Id}", spot, facing);
                switch (npc.Role)
                {
                    case NpcRole.Smith:
                        // Ferraria do pacote atrás da bancada (a forja e a bigorna ficam na frente, onde o jogador vê).
                        PackProp(corner, "building_blacksmith_red", new Vector3(0, 0, -4.2f), 0f, 4.5f, true);
                        PackProp(corner, "barrel", new Vector3(1.9f, 0, 0.6f), 30f, 4.2f, true);
                        PackProp(corner, "resource_stone", new Vector3(-1.9f, 0, 0.6f), -20f, 3f);
                        Part(corner, "Forja", stone, k => k.Box(0, new Vector3(0.9f, 0, 0.3f), Quaternion.identity, new Vector3(1.4f, 1f, 1.1f)), collider: true);
                        Part(corner, "Brasas", ember, k => k.Box(0, new Vector3(0.9f, 1f, 0.3f), Quaternion.identity, new Vector3(1f, 0.08f, 0.7f)));
                        Part(corner, "Bigorna", iron, k =>
                        {
                            k.Box(0, new Vector3(-0.8f, 0, -0.2f), Quaternion.identity, new Vector3(0.35f, 0.5f, 0.35f));
                            k.Box(0, new Vector3(-0.8f, 0.5f, -0.2f), Quaternion.identity, new Vector3(0.8f, 0.25f, 0.4f));
                        }, collider: true);
                        PropBuilder.Fire(corner, new Vector3(0.9f, 1.05f, 0.3f), withLight: true);
                        break;
                    case NpcRole.Merchant:
                        Part(corner, "Balcão", wood, k => k.Box(0, new Vector3(0, 0, 0.2f), Quaternion.identity, new Vector3(2.4f, 0.9f, 0.8f)), collider: true);
                        Part(corner, "Postes", darkWood, k =>
                        {
                            foreach (var x in new[] { -1.15f, 1.15f })
                            foreach (var z in new[] { -0.15f, 0.9f })
                                k.Box(0, new Vector3(x, 0, z), Quaternion.identity, new Vector3(0.1f, 2.3f, 0.1f));
                        });
                        Part(corner, "Toldo", clothRed, k => k.Roof(0, new Vector3(0, 2.3f, 0.38f), Quaternion.Euler(0, 90, 0), 1.3f, 2.6f, 0.45f, 0.15f));
                        if (!PackProp(corner, "crate_long_A", new Vector3(-0.55f, 0.9f, 0.2f), 0f, 2.4f))
                            Part(corner, "Mercadorias", books, k =>
                        {
                            for (var i = 0; i < 5; i++)
                                k.Blob(0, new Vector3(-0.9f + i * 0.45f, 1f, 0.15f), Vector3.one * 0.13f, r, 0.15f);
                        });
                        PackProp(corner, "crate_long_B", new Vector3(0.55f, 0.9f, 0.2f), 0f, 2.4f);
                        PackProp(corner, "crate_open", new Vector3(1.6f, 0, -0.3f), 25f, 3.5f, true);
                        break;
                    case NpcRole.Warehouse when PackMeshes.Has(PackMeshes.Village):
                        PackProp(corner, "crate_A_big", new Vector3(-0.7f, 0, 0), 10f, 4f, true);
                        PackProp(corner, "crate_B_big", new Vector3(0.2f, 0, 0.1f), -15f, 3.8f, true);
                        PackProp(corner, "crate_A_small", new Vector3(-0.6f, 0.82f, 0.05f), 30f, 4f);
                        PackProp(corner, "barrel", new Vector3(1.1f, 0, -0.1f), 0f, 4.5f, true);
                        PackProp(corner, "barrel", new Vector3(1.0f, 0, 0.8f), 40f, 4.3f, true);
                        PackProp(corner, "sack", new Vector3(0.4f, 0, -0.8f), 70f, 5f);
                        break;
                    case NpcRole.Warehouse:
                        Part(corner, "Caixotes", wood, k =>
                        {
                            k.Box(0, new Vector3(-0.7f, 0, 0), Quaternion.Euler(0, 10, 0), new Vector3(0.8f, 0.8f, 0.8f));
                            k.Box(0, new Vector3(0.2f, 0, 0.1f), Quaternion.Euler(0, -15, 0), new Vector3(0.75f, 0.75f, 0.75f));
                            k.Box(0, new Vector3(-0.6f, 0.8f, 0.05f), Quaternion.Euler(0, 30, 0), new Vector3(0.6f, 0.6f, 0.6f));
                        }, collider: true);
                        Part(corner, "Barris", darkWood, k =>
                        {
                            k.Frustum(0, new Vector3(1.1f, 0, -0.1f), Quaternion.identity, 0.38f, 0.38f, 0.95f, 8, null);
                            k.Frustum(0, new Vector3(1.0f, 0, 0.75f), Quaternion.identity, 0.36f, 0.36f, 0.9f, 8, null);
                        }, collider: true);
                        break;
                    case NpcRole.QuestGiver when at.magnitude > 25f:
                        // NPC de região: acampamento com barraca e fogueira.
                        if (!PackProp(corner, "tent", new Vector3(0.6f, 0, 0.6f), 180f, 5f, true))
                            Part(corner, "Barraca", clothBlue, k => k.Roof(0, new Vector3(0.6f, 0, 0.6f), Quaternion.identity, 1.6f, 2.2f, 1.5f, 0.05f), collider: true);
                        PackProp(corner, "resource_lumber", new Vector3(1.6f, 0, -0.6f), 20f, 3f);
                        Part(corner, "Lenha", darkWood, k =>
                        {
                            k.Box(0, new Vector3(-1.4f, 0, -0.9f), Quaternion.Euler(0, 30, 0), new Vector3(0.8f, 0.12f, 0.12f));
                            k.Box(0, new Vector3(-1.4f, 0, -0.9f), Quaternion.Euler(0, -30, 0), new Vector3(0.8f, 0.12f, 0.12f));
                        });
                        PropBuilder.Fire(corner, new Vector3(-1.4f, 0.1f, -0.9f), withLight: true);
                        break;
                    case NpcRole.QuestGiver:
                        Part(corner, "Estante", darkWood, k =>
                        {
                            k.Box(0, new Vector3(0, 0, 0.3f), Quaternion.identity, new Vector3(1.8f, 2.2f, 0.45f));
                        }, collider: true);
                        Part(corner, "Livros", books, k =>
                        {
                            for (var shelf = 0; shelf < 3; shelf++)
                                for (var i = 0; i < 6; i++)
                                    k.Box(0, new Vector3(-0.7f + i * 0.27f, 0.25f + shelf * 0.65f, 0.05f), Quaternion.identity, new Vector3(0.18f, 0.42f, 0.12f));
                        });
                        Part(corner, "Tapete", clothBlue, k => k.Box(0, new Vector3(0, 0.01f, -1.6f), Quaternion.identity, new Vector3(2.2f, 0.02f, 1.6f)));
                        break;
                    case NpcRole.DungeonPortal:
                        // Portão de pedra com o portal brilhando (a entrada da Cripta). Sem o pacote, o arco feito em código.
                        // Recuado (gate) para a guardiã ficar na frente do portão, e não dentro dele.
                        const float gate = -2.3f;
                        if (PackProp(corner, "wall_straight_gate", new Vector3(0, 0, 0.3f + gate), 90f, 2.6f, true))
                        {
                            PackProp(corner, "flag_red", new Vector3(-2.9f, 0, 0.1f + gate), 0f, 3.2f);
                            PackProp(corner, "flag_red", new Vector3(2.9f, 0, 0.1f + gate), 0f, 3.2f);
                            Part(corner, "Portal", portalGlow, k =>
                            {
                                // A porta aparece dos dois lados do portão: o brilho também.
                                k.Box(0, new Vector3(0, 0.15f, 1.55f + gate), Quaternion.identity, new Vector3(1.7f, 2.5f, 0.06f));
                                k.Box(0, new Vector3(0, 0.15f, -0.95f + gate), Quaternion.identity, new Vector3(1.7f, 2.5f, 0.06f));
                            });
                        }
                        else
                        {
                            Part(corner, "Arco", stone, k =>
                            {
                                foreach (var x in new[] { -1.3f, 1.3f })
                                    k.Box(0, new Vector3(x, 0, 0.3f + gate), Quaternion.identity, new Vector3(0.6f, 3.2f, 0.7f));
                                k.Box(0, new Vector3(0, 3.2f, 0.3f + gate), Quaternion.identity, new Vector3(3.4f, 0.6f, 0.8f));
                                k.Box(0, new Vector3(0, 0, 0.3f + gate), Quaternion.identity, new Vector3(3.6f, 0.2f, 1.2f));
                            }, collider: true);
                            Part(corner, "Portal", portalGlow, k => k.Box(0, new Vector3(0, 0.2f, 0.3f + gate), Quaternion.identity, new Vector3(2f, 3f, 0.08f)));
                        }
                        var light = new GameObject("Luz do portal").AddComponent<Light>();
                        light.transform.SetParent(corner, false);
                        light.transform.localPosition = new Vector3(0, 1.6f, -0.4f + gate);
                        light.type = LightType.Point;
                        light.color = new Color(0.6f, 0.4f, 1f);
                        light.range = 6f;
                        light.intensity = 1.6f;
                        light.shadows = LightShadows.None;
                        break;
                    case NpcRole.Market:
                        // Mercado: o quadro de ofertas (tábuas com papéis pregados), bandeira amarela e caixotes de quem vende.
                        Part(corner, "Quadro", darkWood, k =>
                        {
                            foreach (var x in new[] { -1.1f, 1.1f })
                                k.Box(0, new Vector3(x, 0, 0.3f), Quaternion.identity, new Vector3(0.14f, 2.3f, 0.14f));
                            k.Box(0, new Vector3(0, 0.9f, 0.3f), Quaternion.identity, new Vector3(2.4f, 1.3f, 0.08f));
                            k.Box(0, new Vector3(0, 2.25f, 0.3f), Quaternion.identity, new Vector3(2.7f, 0.12f, 0.3f));
                        }, collider: true);
                        Part(corner, "Ofertas", Mat("papel", new Color(0.93f, 0.87f, 0.7f)), k =>
                        {
                            for (var i = 0; i < 9; i++)
                            {
                                var col = i % 3;
                                var line = i / 3;
                                k.Box(0, new Vector3(-0.7f + col * 0.7f + (float)(r.NextDouble() - 0.5) * 0.12f, 1.0f + line * 0.38f, 0.25f),
                                    Quaternion.Euler(0, 0, (float)(r.NextDouble() - 0.5) * 12f), new Vector3(0.42f, 0.3f, 0.02f));
                            }
                        });
                        PackProp(corner, "flag_yellow", new Vector3(1.6f, 0, 0.2f), 0f, 3.2f);
                        PackProp(corner, "crate_A_small", new Vector3(-1.7f, 0, -0.2f), 20f, 4f, true);
                        PackProp(corner, "sack", new Vector3(-1.4f, 0, -0.9f), 60f, 5f);
                        break;
                    case NpcRole.Trainer:
                        // Mestre de skills: armeiro com espadas e cajados, e um boneco de palha para as aulas.
                        PackProp(corner, "weaponrack", new Vector3(-0.2f, 0, 0.7f), 0f, 5f, true);
                        PackProp(corner, "target", new Vector3(-1.9f, 0, 0.4f), 20f, 5f, true);
                        if (!PackMeshes.Has(PackMeshes.Village)) Part(corner, "Armeiro", darkWood, k =>
                        {
                            foreach (var x in new[] { -1f, 1f })
                                k.Box(0, new Vector3(x, 0, 0.3f), Quaternion.identity, new Vector3(0.12f, 1.6f, 0.12f));
                            k.Box(0, new Vector3(0, 1.35f, 0.3f), Quaternion.identity, new Vector3(2.2f, 0.1f, 0.14f));
                            k.Box(0, new Vector3(0, 0.25f, 0.3f), Quaternion.identity, new Vector3(2.2f, 0.1f, 0.3f));
                        }, collider: true);
                        if (!PackMeshes.Has(PackMeshes.Village)) Part(corner, "Armas", iron, k =>
                        {
                            for (var i = 0; i < 4; i++)
                                k.Box(0, new Vector3(-0.66f + i * 0.44f, 0.3f, 0.36f), Quaternion.Euler(-8f, 0, 0), new Vector3(0.07f, 1.25f, 0.03f));
                        });
                        var dummy = Group(corner, "Boneco do mestre", new Vector3(1.9f, 0, -0.4f), Quaternion.Euler(0, 200f, 0));
                        StrawDummy(dummy);
                        break;
                }
            }
        }

        /// <summary>Postes com lanterna em volta da praça (acendem a vila à noite).</summary>
        private static void Lamps(Transform parent)
        {
            var post = Mat("poste", new Color(0.22f, 0.2f, 0.2f));
            var lantern = Glow("lanterna", new Color(1f, 0.8f, 0.45f));
            for (var i = 0; i < 6; i++)
            {
                var angle = (i * 60f + 30f) * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Cos(angle) * 12.5f, 0, Mathf.Sin(angle) * 12.5f);
                if (Ground.DistanceToRoad(position.x, position.z) < 2.5f) continue;
                var lamp = Group(parent, "Poste", position, Quaternion.identity);
                Part(lamp, "Poste", post, k =>
                {
                    k.Frustum(0, Vector3.zero, Quaternion.identity, 0.09f, 0.07f, 3f, 6, null);
                    k.Box(0, new Vector3(0.3f, 2.9f, 0), Quaternion.identity, new Vector3(0.65f, 0.06f, 0.06f));
                }, collider: true);
                Part(lamp, "Lanterna", lantern, k => k.Box(0, new Vector3(0.55f, 2.5f, 0), Quaternion.identity, new Vector3(0.22f, 0.32f, 0.22f)));
                var light = new GameObject("Luz da lanterna").AddComponent<Light>();
                light.transform.SetParent(lamp, false);
                light.transform.localPosition = new Vector3(0.55f, 2.4f, 0);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.75f, 0.45f);
                light.range = 8f;
                light.intensity = 1.2f;
                light.shadows = LightShadows.None;
                light.gameObject.AddComponent<NightLight>();
            }
        }

        // ===== Boneco de treino =====

        /// <summary>Boneco de palha: poste, corpo de palha amarrado, cabeça de saco e os braços de madeira.</summary>
        public static void StrawDummy(Transform parent)
        {
            var straw = Mat("palha", new Color(0.85f, 0.72f, 0.38f));
            var sack = Mat("saco", new Color(0.78f, 0.68f, 0.52f));
            var wood = Mat("madeira", new Color(0.5f, 0.35f, 0.22f));
            var rope = Mat("corda", new Color(0.45f, 0.32f, 0.2f));
            var r = new System.Random(parent.name.GetHashCode());
            Part(parent, "Poste", wood, k =>
            {
                k.Frustum(0, Vector3.zero, Quaternion.identity, 0.09f, 0.08f, 1.9f, 6, null);
                k.Box(0, new Vector3(0, 1.35f, 0), Quaternion.identity, new Vector3(1.5f, 0.1f, 0.1f));
            });
            Part(parent, "Corpo", straw, k => k.Blob(0, new Vector3(0, 1.15f, 0), new Vector3(0.36f, 0.48f, 0.3f), r, 0.12f));
            Part(parent, "Cabeça", sack, k => k.Blob(0, new Vector3(0, 1.82f, 0), new Vector3(0.24f, 0.24f, 0.22f), r, 0.08f));
            Part(parent, "Cordas", rope, k =>
            {
                k.Frustum(0, new Vector3(0, 0.95f, 0), Quaternion.identity, 0.34f, 0.34f, 0.06f, 8, null);
                k.Frustum(0, new Vector3(0, 1.4f, 0), Quaternion.identity, 0.33f, 0.33f, 0.06f, 8, null);
            });
        }
    }
}
