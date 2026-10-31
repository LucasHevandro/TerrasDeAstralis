using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Modelos dos itens: as armas por nível na mão (Visuals/weapon/&lt;id&gt;, com os modelos do KayKit tingidos por nível)
    /// e todos os itens no chão (Visuals/item/&lt;id&gt;): as armas e, em low-poly gerado aqui, armaduras, luvas, botas,
    /// anéis, núcleos, pedras, poções, materiais dos monstros e a chave da cripta.
    /// Menu: Jogo &gt; Visuais &gt; Montar itens.
    /// </summary>
    internal static class ItemModelBuilder
    {
        private const string Weapons = "Assets/Art/KayKit/Armas";
        private const string Generated = "Assets/Art/KayKit/Gerado";
        private const string MeshFolder = "Assets/Art/Itens/Malhas";
        private const string WeaponPrefabs = "Assets/Resources/Visuals/weapon";
        private const string ItemPrefabs = "Assets/Resources/Visuals/item";

        // Cor de cada nível: ferro (aço claro), aço negro (escuro azulado) e astralita (azul-claro que brilha).
        private static readonly (string Key, Color Tint, Color Glow)[] Grades =
        {
            ("ferro", Color.white, Color.black),
            ("aco_negro", new Color(0.42f, 0.44f, 0.58f), Color.black),
            ("astralita", new Color(0.72f, 0.92f, 1f), new Color(0.12f, 0.35f, 0.5f)),
        };

        [MenuItem("Jogo/Visuais/Montar itens")]
        public static void Build()
        {
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(WeaponPrefabs);
            Directory.CreateDirectory(ItemPrefabs);
            WeaponPrefab("espada_ferro", "sword_1handed", "knight", 0, 1f);
            WeaponPrefab("espada_aco_negro", "sword_2handed", "knight", 1, 0.9f);
            WeaponPrefab("espada_astralita", "sword_2handed_color", "knight", 2, 0.95f);
            WeaponPrefab("cajado_ferro", "staff", "mage", 0, 1f);
            WeaponPrefab("cajado_aco_negro", "staff", "mage", 1, 1.1f);
            WeaponPrefab("cajado_astralita", "staff", "mage", 2, 1.2f);
            WeaponPrefab("arco_ferro", "bow_withString", "ranger", 0, 1f);
            WeaponPrefab("arco_aco_negro", "bow_withString", "ranger", 1, 1.05f);
            WeaponPrefab("arco_astralita", "bow_withString", "ranger", 2, 1.1f);
            WeaponPrefab("machado_ferro", "axe_1handed_Large", "barbarian", 0, 1f);
            WeaponPrefab("machado_aco_negro", "axe_1handed_Large", "barbarian", 1, 1.05f);
            WeaponPrefab("machado_astralita", "axe_1handed_Large", "barbarian", 2, 1.1f);
            ArrowPrefab();
            Gear();
            Small();
            AssetDatabase.SaveAssets();
            Debug.Log("[Itens] armas e itens montados.");
        }

        // ===== Armas =====

        private static Material Tinted(string atlas, int grade)
        {
            var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Generated}/{atlas}.mat");
            if (grade == 0) return baseMaterial;
            var (key, tint, glow) = Grades[grade];
            var path = $"{Generated}/{atlas}_{key}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(baseMaterial);
                AssetWrite.Create(material, path);
            }
            material.CopyPropertiesFromMaterial(baseMaterial);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", grade == 2 ? 0.5f : 0.3f);
            if (glow.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", glow);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void WeaponPrefab(string itemId, string model, string atlas, int grade, float scale)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Weapons}/{model}.fbx");
            var material = Tinted(atlas, grade);
            var root = new GameObject(itemId);
            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            weapon.transform.localScale = Vector3.one * scale;
            foreach (var renderer in weapon.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
            Save(root, $"{WeaponPrefabs}/{itemId}.prefab", keep: true);

            // No chão: deitada e um pouco menor, no centro do brilho.
            weapon.transform.localScale = Vector3.one * scale * 0.75f;
            weapon.transform.localRotation = Quaternion.Euler(0, 0, 60);
            weapon.transform.localPosition = new Vector3(0.3f, -0.25f, 0);
            Save(root, $"{ItemPrefabs}/{itemId}.prefab");
        }

        /// <summary>
        /// Flecha que voa (Visuals/fx/flecha): o modelo do pacote deitado com a ponta para +Z (o Projectile aponta o +Z
        /// para o alvo), pelo eixo mais comprido da malha.
        /// </summary>
        private static void ArrowPrefab()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Weapons}/arrow_bow.fbx");
            var material = Tinted("ranger", 0);
            var root = new GameObject("flecha");
            var arrow = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            foreach (var renderer in arrow.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var filter = arrow.GetComponentInChildren<MeshFilter>();
            if (filter != null)
            {
                // O pivô fica na ponta e a malha vai dele até as penas: a ponta é o lado oposto ao centro da malha.
                var bounds = filter.sharedMesh.bounds;
                var size = bounds.size;
                var axis = size.x > size.y && size.x > size.z ? Vector3.right : size.y > size.z ? Vector3.up : Vector3.forward;
                var tip = Vector3.Dot(bounds.center, axis) >= 0 ? -axis : axis;
                arrow.transform.localRotation = Quaternion.FromToRotation(filter.transform.localRotation * tip, Vector3.forward);
                Debug.Log($"[Itens] flecha: eixo {axis}, ponta {tip}, tamanho {size}");
            }
            Directory.CreateDirectory("Assets/Resources/Visuals/fx");
            Save(root, "Assets/Resources/Visuals/fx/flecha.prefab");
        }

        // ===== Peças low-poly =====

        private static Material Mat(string name, Color color) => TrainingSceneBuilder.MaterialFor($"Item_{name}", color);

        private static Material Glow(string name, Color color) =>
            TrainingSceneBuilder.MaterialFor($"Item_{name}", color * 1.6f, "Universal Render Pipeline/Unlit");

        private static int _parts;

        private static GameObject Part(Transform parent, string name, Material material, System.Action<MeshKit> draw)
        {
            var kit = new MeshKit(1);
            draw(kit);
            var mesh = kit.Build($"{parent.root.name}_{++_parts}");
            var path = $"{MeshFolder}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetWrite.Create(mesh, path);
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            return obj;
        }

        private static void Save(GameObject root, string path, bool keep = false)
        {
            AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            if (!keep) Object.DestroyImmediate(root);
        }

        private static Transform Root(string id)
        {
            _parts = 0;
            return new GameObject(id).transform;
        }

        private static void Gear()
        {
            var colors = new Dictionary<string, (Color Metal, Color Trim)>
            {
                ["ferro"] = (new Color(0.62f, 0.64f, 0.68f), new Color(0.48f, 0.33f, 0.2f)),
                ["aco_negro"] = (new Color(0.24f, 0.25f, 0.33f), new Color(0.6f, 0.2f, 0.2f)),
                ["astralita"] = (new Color(0.7f, 0.88f, 1f), new Color(0.95f, 0.8f, 0.35f)),
            };
            foreach (var (key, (metalColor, trimColor)) in colors)
            {
                var metal = Mat($"metal_{key}", metalColor);
                var trim = Mat($"detalhe_{key}", trimColor);
                var r = new System.Random(key.GetHashCode());

                // Peitoral: tronco com ombreiras e cinto.
                var armor = Root($"armadura_{key}");
                Part(armor, "Peitoral", metal, k =>
                {
                    k.Box(0, new Vector3(0, -0.25f, 0), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.28f));
                    k.Blob(0, new Vector3(-0.3f, 0.2f, 0), new Vector3(0.16f, 0.12f, 0.16f), r, 0.08f);
                    k.Blob(0, new Vector3(0.3f, 0.2f, 0), new Vector3(0.16f, 0.12f, 0.16f), r, 0.08f);
                });
                Part(armor, "Cinto", trim, k => k.Box(0, new Vector3(0, -0.28f, 0), Quaternion.identity, new Vector3(0.52f, 0.08f, 0.3f)));
                Save(armor.gameObject, $"{ItemPrefabs}/armadura_{key}.prefab");

                var gloves = Root($"luvas_{key}");
                foreach (var side in new[] { -1f, 1f })
                {
                    Part(gloves, "Luva", metal, k =>
                    {
                        k.Box(0, new Vector3(side * 0.13f, -0.15f, 0), Quaternion.Euler(0, side * 15, 0), new Vector3(0.14f, 0.2f, 0.12f));
                        k.Frustum(0, new Vector3(side * 0.13f, 0.05f, 0), Quaternion.identity, 0.08f, 0.09f, 0.12f, 6, null);
                    });
                    Part(gloves, "Punho", trim, k => k.Frustum(0, new Vector3(side * 0.13f, 0.02f, 0), Quaternion.identity, 0.095f, 0.095f, 0.04f, 6, null));
                }
                Save(gloves.gameObject, $"{ItemPrefabs}/luvas_{key}.prefab");

                var boots = Root($"botas_{key}");
                foreach (var side in new[] { -1f, 1f })
                {
                    Part(boots, "Bota", metal, k =>
                    {
                        k.Box(0, new Vector3(side * 0.12f, -0.3f, 0.04f), Quaternion.identity, new Vector3(0.13f, 0.1f, 0.24f));
                        k.Frustum(0, new Vector3(side * 0.12f, -0.2f, 0), Quaternion.identity, 0.07f, 0.075f, 0.24f, 6, null);
                    });
                    Part(boots, "Borda", trim, k => k.Frustum(0, new Vector3(side * 0.12f, 0.02f, 0), Quaternion.identity, 0.08f, 0.08f, 0.04f, 6, null));
                }
                Save(boots.gameObject, $"{ItemPrefabs}/botas_{key}.prefab");

                // Anel: aro de 12 pedaços e uma pedra.
                var ring = Root($"anel_{key}");
                Part(ring, "Aro", trim, k =>
                {
                    for (var i = 0; i < 12; i++)
                    {
                        var a = i / 12f * Mathf.PI * 2f;
                        k.Box(0, new Vector3(Mathf.Cos(a) * 0.14f, Mathf.Sin(a) * 0.14f - 0.1f, 0), Quaternion.Euler(0, 0, a * Mathf.Rad2Deg),
                            new Vector3(0.04f, 0.08f, 0.04f));
                    }
                });
                Part(ring, "Pedra", Glow($"pedra_anel_{key}", key == "astralita" ? new Color(0.4f, 0.8f, 1f) : key == "aco_negro" ? new Color(0.9f, 0.2f, 0.25f) : new Color(0.4f, 0.9f, 0.5f)),
                    k => k.Blob(0, new Vector3(0, 0.08f, 0), Vector3.one * 0.06f, r, 0.1f));
                Save(ring.gameObject, $"{ItemPrefabs}/anel_{key}.prefab");
            }
        }

        private static void Crystal(string id, Color color, float size)
        {
            var root = Root(id);
            var r = new System.Random(id.GetHashCode());
            Part(root, "Cristal", Glow(id, color), k =>
            {
                k.Frustum(0, Vector3.zero, Quaternion.identity, size * 0.45f, 0f, size, 6, r, 0f, bottomCap: true);
                k.Frustum(0, Vector3.zero, Quaternion.Euler(180, 0, 0), size * 0.45f, 0f, size * 0.6f, 6, r, 0f, bottomCap: true);
            });
            Save(root.gameObject, $"{ItemPrefabs}/{id}.prefab");
        }

        private static void Gem(string id, Color color)
        {
            var root = Root(id);
            Part(root, "Gema", Glow(id, color), k => k.Blob(0, Vector3.zero, new Vector3(0.16f, 0.12f, 0.16f), new System.Random(id.GetHashCode()), 0.2f));
            Save(root.gameObject, $"{ItemPrefabs}/{id}.prefab");
        }

        private static void Potion(string id, Color color, float size)
        {
            var root = Root(id);
            var glass = Mat("vidro", new Color(0.85f, 0.9f, 0.95f));
            Part(root, "Líquido", Glow(id, color), k => k.Blob(0, new Vector3(0, -0.1f, 0), Vector3.one * 0.15f * size, new System.Random(1), 0.04f));
            Part(root, "Gargalo", glass, k => k.Frustum(0, new Vector3(0, 0.03f * size, 0), Quaternion.identity, 0.06f * size, 0.05f * size, 0.12f * size, 6, null));
            Part(root, "Rolha", Mat("rolha", new Color(0.55f, 0.38f, 0.22f)),
                k => k.Frustum(0, new Vector3(0, 0.15f * size, 0), Quaternion.identity, 0.055f * size, 0.06f * size, 0.06f * size, 6, null));
            Save(root.gameObject, $"{ItemPrefabs}/{id}.prefab");
        }

        private static void Lump(string id, Color color, Vector3 scale, float rough, bool glow = false)
        {
            var root = Root(id);
            Part(root, "Peça", glow ? Glow(id, color) : Mat(id, color),
                k => k.Blob(0, Vector3.zero, scale, new System.Random(id.GetHashCode()), rough));
            Save(root.gameObject, $"{ItemPrefabs}/{id}.prefab");
        }

        private static void Small()
        {
            Crystal("nucleo_baixo", new Color(0.35f, 0.65f, 1f), 0.3f);
            Crystal("nucleo_medio", new Color(0.7f, 0.4f, 1f), 0.34f);
            Crystal("nucleo_alto", new Color(1f, 0.75f, 0.25f), 0.38f);
            Gem("pedra_vigor", new Color(0.35f, 1f, 0.45f));
            Gem("pedra_furia", new Color(1f, 0.3f, 0.2f));
            Gem("pedra_precisao", new Color(1f, 0.9f, 0.3f));
            Potion("pocao_hp_p", new Color(1f, 0.2f, 0.25f), 1f);
            Potion("pocao_hp_m", new Color(1f, 0.15f, 0.3f), 1.3f);
            Potion("pocao_mp_p", new Color(0.25f, 0.45f, 1f), 1f);
            Potion("pocao_mp_m", new Color(0.2f, 0.35f, 1f), 1.3f);
            Lump("pelo_macio", new Color(0.95f, 0.93f, 0.88f), new Vector3(0.2f, 0.12f, 0.2f), 0.25f);
            Lump("pele_lobo", new Color(0.5f, 0.5f, 0.55f), new Vector3(0.28f, 0.05f, 0.22f), 0.2f);
            Lump("couro_javali", new Color(0.42f, 0.28f, 0.18f), new Vector3(0.3f, 0.05f, 0.24f), 0.2f);
            Lump("seda", new Color(0.97f, 0.97f, 1f), new Vector3(0.12f, 0.16f, 0.12f), 0.08f);
            Lump("fragmento_pedra", new Color(0.55f, 0.55f, 0.58f), new Vector3(0.16f, 0.12f, 0.14f), 0.3f);
            Lump("po_de_osso", new Color(0.88f, 0.84f, 0.72f), new Vector3(0.16f, 0.1f, 0.16f), 0.15f);
            Lump("essencia", new Color(0.75f, 0.45f, 1f), Vector3.one * 0.13f, 0.1f, glow: true);
            Lump("veneno", new Color(0.4f, 1f, 0.3f), Vector3.one * 0.11f, 0.05f, glow: true);
            Crystal("fragmento_guardiao", new Color(1f, 0.25f, 0.2f), 0.32f);

            var tusk = Root("presa_lobo");
            Part(tusk, "Presa", Mat("presa", new Color(0.95f, 0.92f, 0.82f)),
                k => k.Frustum(0, new Vector3(0, -0.15f, 0), Quaternion.Euler(0, 0, 20), 0.06f, 0f, 0.32f, 5, null, 0f, bottomCap: true));
            Save(tusk.gameObject, $"{ItemPrefabs}/presa_lobo.prefab");

            var key = Root("chave_cripta");
            var iron = Mat("ferro_chave", new Color(0.45f, 0.42f, 0.38f));
            Part(key, "Chave", iron, k =>
            {
                k.Box(0, new Vector3(0, -0.2f, 0), Quaternion.identity, new Vector3(0.04f, 0.32f, 0.04f));
                k.Box(0, new Vector3(0.05f, -0.18f, 0), Quaternion.identity, new Vector3(0.08f, 0.04f, 0.04f));
                k.Box(0, new Vector3(0.04f, -0.1f, 0), Quaternion.identity, new Vector3(0.06f, 0.035f, 0.04f));
                for (var i = 0; i < 10; i++)
                {
                    var a = i / 10f * Mathf.PI * 2f;
                    k.Box(0, new Vector3(Mathf.Cos(a) * 0.08f, 0.17f + Mathf.Sin(a) * 0.08f, 0), Quaternion.Euler(0, 0, a * Mathf.Rad2Deg),
                        new Vector3(0.03f, 0.05f, 0.04f));
                }
            });
            Part(key, "Olho", Glow("olho_chave", new Color(0.5f, 0.9f, 1f)), k => k.Blob(0, new Vector3(0, 0.21f, 0), Vector3.one * 0.035f, new System.Random(3), 0.05f));
            Save(key.gameObject, $"{ItemPrefabs}/chave_cripta.prefab");
        }
    }
}
