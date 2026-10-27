using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Modelos do jogo por convenção de pasta: <c>Assets/Resources/Visuals/&lt;categoria&gt;/&lt;id&gt;.prefab</c>
    /// (ex.: <c>Visuals/monster/lobo_cinzento</c>, <c>Visuals/npc/ferreira_ilsa</c>, <c>Visuals/class/blade</c>,
    /// <c>Visuals/item/espada_ferro</c>). Sem prefab para o id, o jogo continua com as formas simples de hoje — assim os
    /// modelos entram um de cada vez, sem mexer no código. O guia está em <c>Docs/visuais.md</c>.
    /// </summary>
    public static class VisualCatalog
    {
        public const string Monster = "monster";
        public const string Npc = "npc";
        public const string Class = "class";
        public const string Item = "item";

        private const string Root = "Visuals/";

        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        /// <summary>Tom dos itens de missão (vem antes de <see cref="Variants"/>, que o usa ao ser montado).</summary>
        private static readonly Color QuestTint = new Color(1.5f, 1.25f, 0.55f);

        /// <summary>
        /// Variantes: um id sem prefab próprio usa o modelo de outro, maior ou menor e com a cor puxada para um tom
        /// (multiplica a cor dos materiais). Os monstros do Santuário reaproveitam os da Cripta e das Ruínas, e a chave do
        /// Santuário é a da Cripta em dourado.
        /// </summary>
        public static readonly Dictionary<string, (string BaseId, float Scale, Color Tint)> Variants = new Dictionary<string, (string, float, Color)>
        {
            ["sentinela_de_pedra"] = ("golem_de_pedra", 1.25f, new Color(1f, 0.86f, 0.62f)),
            // Monstros das lacunas de nível no mundo aberto (2026-10-25).
            ["lobo_alfa"] = ("lobo_cinzento", 1.25f, new Color(0.62f, 0.58f, 0.66f)),
            ["aranha_rainha"] = ("aranha_da_mata", 1.3f, new Color(0.95f, 0.6f, 1.15f)),
            ["cavaleiro_errante"] = ("cavaleiro_caido", 1.05f, new Color(0.7f, 0.85f, 1.15f)),
            ["espectro_ancestral"] = ("espectro", 1.15f, new Color(0.55f, 0.95f, 1.15f)),
            ["cavaleiro_juramentado"] = ("cavaleiro_caido", 1.15f, new Color(1.1f, 0.9f, 0.55f)),
            ["eco_do_oraculo"] = ("servo_da_cripta", 1f, new Color(0.55f, 1f, 1.15f)),
            ["oraculo_corrompido"] = ("senhor_da_cripta", 1.05f, new Color(0.5f, 1f, 1.4f)),
            ["chave_santuario"] = ("chave_cripta", 1f, new Color(1.7f, 1.3f, 0.6f)),
            // Itens de missão: o modelo do material, puxado para o dourado (só quem tem a missão vê).
            ["missao_pelo_macio"] = ("pelo_macio", 1f, QuestTint),
            ["missao_pele_lobo"] = ("pele_lobo", 1f, QuestTint),
            ["missao_presa_lobo"] = ("presa_lobo", 1f, QuestTint),
            ["missao_couro_javali"] = ("couro_javali", 1f, QuestTint),
            ["missao_seda"] = ("seda", 1f, QuestTint),
            ["missao_veneno"] = ("veneno", 1f, QuestTint),
            ["missao_fragmento_pedra"] = ("fragmento_pedra", 1f, QuestTint),
            ["missao_essencia"] = ("essencia", 1f, QuestTint),
        };

        /// <summary>O id cujo modelo é usado (o próprio, ou a base da variante).</summary>
        public static string BaseOf(string id) => id != null && Variants.TryGetValue(id, out var variant) ? variant.BaseId : id;

        private static readonly Dictionary<(Material, Color), Material> TintCache = new Dictionary<(Material, Color), Material>();

        /// <summary>O prefab do id, ou nulo se ainda não existe (usa a forma simples).</summary>
        public static GameObject Find(string category, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var path = Root + category + "/" + id;
            if (!Cache.TryGetValue(path, out var prefab))
            {
                prefab = Resources.Load<GameObject>(path);
                if (prefab == null && Variants.TryGetValue(id, out var variant)) prefab = Find(category, variant.BaseId);
                Cache[path] = prefab;
            }
            return prefab;
        }

        public static bool Has(string category, string id) => Find(category, id) != null;

        /// <summary>
        /// Cria o modelo como filho de <paramref name="parent"/> (sem colisores nem física: quem clica e colide é o objeto
        /// do jogo, não o modelo). Nulo se não houver prefab.
        /// </summary>
        public static GameObject Spawn(string category, string id, Transform parent)
        {
            var prefab = Find(category, id);
            if (prefab == null) return null;
            var instance = Object.Instantiate(prefab, parent, false);
            instance.name = "Modelo: " + id;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.Destroy(collider);
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(body);
            ApplyVariant(instance, category, id);
            var animated = instance.GetComponentInChildren<Animator>() != null || instance.GetComponentInChildren<ProceduralRig>() != null;
            if (animated && instance.GetComponent<VisualAnimator>() == null)
                instance.AddComponent<VisualAnimator>();
            return instance;
        }

        /// <summary>
        /// Se o id é uma variante (sem prefab próprio), aumenta o modelo e troca os materiais por cópias tingidas (uma
        /// cópia por material e tom, reaproveitada).
        /// </summary>
        public static void ApplyVariant(GameObject instance, string category, string id)
        {
            if (id == null || !Variants.TryGetValue(id, out var variant) || Find(category, variant.BaseId) != Find(category, id)) return;
            ApplyTint(instance, variant.Scale, variant.Tint);
        }

        /// <summary>Tamanho e tom do monstro elite (maior e dourado).</summary>
        public const float EliteScale = 1.2f;

        public static readonly Color EliteTint = new Color(1.45f, 1.15f, 0.55f);

        /// <summary>Aumenta o modelo e troca os materiais por cópias tingidas (uma cópia por material e tom, reaproveitada).</summary>
        public static void ApplyTint(GameObject instance, float scale, Color tint)
        {
            instance.transform.localScale *= scale;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null) continue;
                    if (!TintCache.TryGetValue((source, tint), out var tinted))
                    {
                        tinted = new Material(source) { name = source.name + " (tom)" };
                        if (tinted.HasProperty("_BaseColor")) tinted.SetColor("_BaseColor", source.GetColor("_BaseColor") * tint);
                        if (tinted.HasProperty("_Color")) tinted.SetColor("_Color", source.GetColor("_Color") * tint);
                        if (tinted.HasProperty("_EmissionColor")) tinted.SetColor("_EmissionColor", source.GetColor("_EmissionColor") * tint);
                        TintCache[(source, tint)] = tinted;
                    }
                    materials[i] = tinted;
                }
                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>Altura e raio do modelo (pelas malhas), relativos a <paramref name="root"/>.</summary>
        public static (float Height, float Radius) Measure(GameObject model, Transform root)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return (2f, 0.5f);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var height = Mathf.Max(0.3f, bounds.max.y - root.position.y);
            var radius = Mathf.Max(0.2f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            return (height, radius);
        }
    }
}
