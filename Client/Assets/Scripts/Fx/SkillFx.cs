using System;
using System.Collections.Generic;
using Game.Shared.Combat;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>Como o projétil de uma skill mágica sai.</summary>
    public enum ProjectileStyle
    {
        /// <summary>Esfera saindo da mão.</summary>
        Orb,

        /// <summary>Lança: menor, mais rápida, com rastro mais longo.</summary>
        Lance,

        /// <summary>Dois projéteis seguidos.</summary>
        Twin,

        /// <summary>Cai do céu em cima do alvo.</summary>
        Meteor,

        /// <summary>Flecha (Ranger): o modelo da flecha, reto e rápido.</summary>
        Arrow,

        /// <summary>Duas flechas seguidas.</summary>
        ArrowTwin,

        /// <summary>Flechas caindo do céu em volta do alvo (chuva e saraivada).</summary>
        ArrowRain,
    }

    /// <summary>
    /// Sensação de cada skill (game feel): cor, como sai, como acerta e quanto pesa. Antes todas as físicas soltavam as
    /// mesmas faíscas e todas as mágicas a mesma esfera; agora cada uma tem identidade. Só visual e som: dano, alcance e
    /// área continuam vindo do Shared (e do servidor no online).
    /// </summary>
    public static class SkillFx
    {
        private sealed class Feel
        {
            public Color Color = Color.white;
            public ProjectileStyle Projectile = ProjectileStyle.Orb;
            public float ProjectileSize = 1f;

            /// <summary>Golpe que acerta duas vezes (a segunda faísca e o segundo som vêm logo depois).</summary>
            public bool Double;

            /// <summary>Área em volta do personagem (giro), e não do alvo.</summary>
            public bool AroundCaster;

            /// <summary>Peso do impacto: soma no hit stop e multiplica a tremida da câmera.</summary>
            public float Weight;

            public int Sparks = 14;
        }

        private static readonly Color Steel = new Color(0.85f, 0.9f, 1f);
        private static readonly Color Rune = new Color(0.45f, 0.75f, 1f);
        private static readonly Color Fire = new Color(1f, 0.5f, 0.15f);
        private static readonly Color Earth = new Color(0.85f, 0.65f, 0.35f);
        private static readonly Color Arcane = new Color(0.7f, 0.45f, 1f);
        private static readonly Color Ice = new Color(0.55f, 0.95f, 1f);
        private static readonly Color Spark = new Color(1f, 0.95f, 0.5f);
        private static readonly Color Blood = new Color(1f, 0.25f, 0.2f);
        private static readonly Color Wood = new Color(0.95f, 0.85f, 0.6f);
        private static readonly Color Wind = new Color(0.7f, 1f, 0.75f);

        private static readonly Dictionary<string, Feel> Feels = new Dictionary<string, Feel>
        {
            // ----- Lâmina (corpo a corpo) -----
            ["corte"] = new Feel { Color = Steel, Sparks = 12 },
            ["giro_de_lamina"] = new Feel { Color = Steel, AroundCaster = true, Weight = 0.3f, Sparks = 10 },
            ["golpe_runico"] = new Feel { Color = Rune, Weight = 0.3f, Sparks = 18 },
            ["estocada_dupla"] = new Feel { Color = Steel, Double = true, Sparks = 12 },
            ["fenda_sismica"] = new Feel { Color = Earth, Weight = 1f, Sparks = 20 },
            ["lamina_flamejante"] = new Feel { Color = Fire, Weight = 0.5f, Sparks = 24 },
            ["tempestade_de_aco"] = new Feel { Color = Steel, AroundCaster = false, Weight = 0.8f, Double = true, Sparks = 18 },
            ["execucao"] = new Feel { Color = Blood, Weight = 1.6f, Sparks = 36 },
            // ----- Arcanista (à distância) -----
            ["faisca"] = new Feel { Color = Spark, Projectile = ProjectileStyle.Lance, ProjectileSize = 0.6f, Sparks = 10 },
            ["lanca_de_forca"] = new Feel { Color = Rune, Projectile = ProjectileStyle.Lance, ProjectileSize = 0.9f, Weight = 0.2f, Sparks = 16 },
            ["golpe_de_cajado"] = new Feel { Color = Arcane, Sparks = 10 },
            ["explosao_arcana"] = new Feel { Color = Arcane, ProjectileSize = 1.3f, Weight = 0.6f, Sparks = 22 },
            ["corrente_gelida"] = new Feel { Color = Ice, Projectile = ProjectileStyle.Lance, ProjectileSize = 1f, Weight = 0.4f, Sparks = 20 },
            ["meteoro"] = new Feel { Color = Fire, Projectile = ProjectileStyle.Meteor, ProjectileSize = 2f, Weight = 1.2f, Sparks = 30 },
            ["raio_duplo"] = new Feel { Color = Spark, Projectile = ProjectileStyle.Twin, ProjectileSize = 0.8f, Double = true, Weight = 0.4f, Sparks = 16 },
            ["cataclismo"] = new Feel { Color = Arcane, Projectile = ProjectileStyle.Meteor, ProjectileSize = 2.6f, Weight = 1.8f, Sparks = 40 },
            // ----- Ranger (flechas) -----
            ["tiro_certeiro"] = new Feel { Color = Wood, Projectile = ProjectileStyle.Arrow, Sparks = 10 },
            ["flecha_perfurante"] = new Feel { Color = Wind, Projectile = ProjectileStyle.Arrow, ProjectileSize = 1.2f, Weight = 0.3f, Sparks = 16 },
            ["chute_de_recuo"] = new Feel { Color = Earth, Weight = 0.3f, Sparks = 10 },
            ["chuva_de_flechas"] = new Feel { Color = Wood, Projectile = ProjectileStyle.ArrowRain, Weight = 0.5f, Sparks = 18 },
            ["tiro_duplo"] = new Feel { Color = Wind, Projectile = ProjectileStyle.ArrowTwin, Double = true, Weight = 0.3f, Sparks = 14 },
            ["flecha_flamejante"] = new Feel { Color = Fire, Projectile = ProjectileStyle.Arrow, ProjectileSize = 1.3f, Weight = 0.7f, Sparks = 24 },
            ["saraivada"] = new Feel { Color = Steel, Projectile = ProjectileStyle.ArrowRain, ProjectileSize = 1.2f, Weight = 0.9f, Sparks = 26 },
            ["tiro_do_falcao"] = new Feel { Color = new Color(1f, 0.85f, 0.4f), Projectile = ProjectileStyle.Arrow, ProjectileSize = 1.8f, Weight = 1.6f, Sparks = 36 },
            // ----- Guardião (machado e escudo, golpes pesados) -----
            ["golpe_de_machado"] = new Feel { Color = Steel, Weight = 0.2f, Sparks = 12 },
            ["grito_de_guerra"] = new Feel { Color = Blood, Weight = 0.4f, Sparks = 16 },
            ["investida_de_escudo"] = new Feel { Color = Wood, Weight = 0.5f, Sparks = 14 },
            ["golpe_esmagador"] = new Feel { Color = Steel, Weight = 0.8f, Sparks = 20 },
            ["desafio"] = new Feel { Color = Blood, Weight = 0.2f, Sparks = 12 },
            ["terremoto"] = new Feel { Color = Earth, Weight = 1.1f, Sparks = 24 },
            ["furia_do_urso"] = new Feel { Color = Fire, Double = true, Weight = 1f, Sparks = 26 },
            ["colosso"] = new Feel { Color = Earth, Double = true, Weight = 1.7f, Sparks = 36 },
        };

        private static bool IsArrow(Feel feel) =>
            feel.Projectile == ProjectileStyle.Arrow || feel.Projectile == ProjectileStyle.ArrowTwin || feel.Projectile == ProjectileStyle.ArrowRain;

        /// <summary>A skill viaja até o alvo (magias e flechas) em vez de acertar na hora do golpe?</summary>
        public static bool IsProjectile(SkillData skill) => skill != null && (skill.Kind == SkillKind.Magical || IsArrow(For(skill)));

        /// <summary>Som da saída: a corda do arco nas flechas, o chiado da magia no resto.</summary>
        public static void PlayLaunchSound(SkillData skill)
        {
            if (skill != null && IsArrow(For(skill))) SoundPlayer.Play(SoundId.BowShot, 0.6f, 0.08f);
            else SoundPlayer.Play(SoundId.Cast, 0.6f);
        }

        private static readonly Feel Default = new Feel();
        private static readonly Dictionary<string, float> LastAreaAt = new Dictionary<string, float>();

        private static Feel For(SkillData skill) => skill != null && Feels.TryGetValue(skill.Id, out var feel) ? feel : Default;

        /// <summary>Cor da skill misturada com a nota do combo (Excellent puxa para o dourado).</summary>
        public static Color ColorFor(SkillData skill, ComboGrade? grade, bool critical)
        {
            var color = For(skill).Color;
            if (critical) return Color.Lerp(color, new Color(1f, 0.55f, 0.15f), 0.6f);
            if (grade == ComboGrade.Excellent) return Color.Lerp(color, new Color(1f, 0.88f, 0.25f), 0.55f);
            return color;
        }

        /// <summary>Ao apertar: corte no ar na frente do personagem (físicas) ou anel em volta dele (giro).</summary>
        public static void OnCast(SkillData skill, Transform caster)
        {
            var fx = CombatFx.Instance;
            if (fx == null || skill == null || caster == null) return;
            var feel = For(skill);
            if (feel.AroundCaster)
            {
                Ring(caster.position + Vector3.up * 0.9f, Mathf.Max(1f, skill.AreaRadius), feel.Color, 36);
                MagicFx.Mark(GroundMarkStyle.Rune, new Vector3(caster.position.x, GroundAt(caster.position) - 0.1f, caster.position.z),
                    Mathf.Max(1f, skill.AreaRadius), feel.Color * 1.15f, 0.9f);
                return;
            }
            if (skill.Kind != SkillKind.Physical || IsArrow(feel)) return;
            // Arco de faíscas da direita para a esquerda, na altura do peito.
            var center = caster.position + Vector3.up * 1.2f;
            for (var i = 0; i < 14; i++)
            {
                var angle = Mathf.Lerp(-70f, 70f, i / 13f) * Mathf.Deg2Rad;
                var direction = caster.rotation * new Vector3(Mathf.Sin(angle), 0.1f, Mathf.Cos(angle));
                fx.Emit(center + direction * 1.1f, direction * 1.5f + Vector3.up * 0.4f, feel.Color, 0.14f, 0.22f);
            }
        }

        /// <summary>Quanto o projétil da skill leva de <paramref name="from"/> até <paramref name="to"/> (segundos).</summary>
        public static float TravelSeconds(SkillData skill, Vector3 from, Vector3 to)
        {
            var fx = CombatFx.Instance;
            if (fx == null || !IsProjectile(skill)) return 0f;
            var feel = For(skill);
            var speed = fx.ProjectileSpeed * (feel.Projectile switch
            {
                ProjectileStyle.Meteor => 1.4f,
                ProjectileStyle.Twin => 1.2f,
                ProjectileStyle.Lance => 1.6f,
                ProjectileStyle.Arrow or ProjectileStyle.ArrowTwin => ArrowSpeed,
                ProjectileStyle.ArrowRain => RainSpeed,
                _ => 1f,
            });
            var distance = feel.Projectile == ProjectileStyle.Meteor ? 11.5f : feel.Projectile == ProjectileStyle.ArrowRain ? RainHeight : Vector3.Distance(from, to);
            return distance / Mathf.Max(1f, speed);
        }

        /// <summary>Lança o projétil da skill mágica do jeito dela; <paramref name="onArrive"/> roda na chegada do primeiro.</summary>
        /// <param name="origin">De onde sai (ponta do cajado); sem ela, da frente do peito.</param>
        public static void Launch(SkillData skill, Transform caster, Transform target, Vector3 targetOffset, ComboGrade? grade, Action onArrive,
            Vector3? origin = null)
        {
            var fx = CombatFx.Instance;
            if (fx == null || caster == null || target == null)
            {
                onArrive?.Invoke();
                return;
            }
            var feel = For(skill);
            var color = ColorFor(skill, grade, false);
            if (IsArrow(feel))
            {
                LaunchArrows(skill, feel, caster, target, targetOffset, color, onArrive);
                return;
            }
            var hand = origin ?? caster.position + Vector3.up * 1.4f + caster.forward * 0.6f;
            // A magia sai com um clarão na ponta do cajado.
            fx.Flash(hand, color, 2.5f, 4f, 0.12f);
            for (var i = 0; i < 8; i++) fx.Glow(hand, UnityEngine.Random.onUnitSphere * 1.5f, color, 0.18f, 0.25f);
            switch (feel.Projectile)
            {
                case ProjectileStyle.Meteor:
                    var sky = target.position + Vector3.up * 11f - caster.forward * 3f;
                    fx.LaunchProjectile(sky, target, targetOffset, color, onArrive, feel.ProjectileSize, 1.4f);
                    // Cataclismo: chovem mais pedras em volta do alvo (só visual; o dano é o do primeiro).
                    if (skill.Id == "cataclismo") fx.StartCoroutine(MeteorShower(target.position, color, Mathf.Max(2f, skill.AreaRadius)));
                    break;
                case ProjectileStyle.Twin:
                    fx.LaunchProjectile(hand + caster.right * 0.3f, target, targetOffset, color, onArrive, feel.ProjectileSize, 1.2f);
                    fx.LaunchProjectile(hand - caster.right * 0.3f, target, targetOffset, color, null, feel.ProjectileSize, 1.05f);
                    break;
                case ProjectileStyle.Lance:
                    fx.LaunchProjectile(hand, target, targetOffset, color, onArrive, feel.ProjectileSize, 1.6f);
                    break;
                default:
                    fx.LaunchProjectile(hand, target, targetOffset, color, onArrive, feel.ProjectileSize, 1f);
                    break;
            }
        }

        /// <summary>Velocidade das flechas (vezes a do projétil mágico) e de onde a chuva cai.</summary>
        private const float ArrowSpeed = 2f;

        private const float RainSpeed = 1.6f;
        private const float RainHeight = 10f;

        /// <summary>
        /// Flechas: saem da frente do peito (o arco está na mão esquerda, esticado à frente). A chuva sobe e cai em cima
        /// do alvo, com mais flechas espalhadas na área (só visual; o dano é o da primeira).
        /// </summary>
        private static void LaunchArrows(SkillData skill, Feel feel, Transform caster, Transform target, Vector3 targetOffset, Color color, Action onArrive)
        {
            var fx = CombatFx.Instance;
            var bow = caster.position + Vector3.up * 1.35f + caster.forward * 0.5f;
            fx.Flash(bow, color, 1.2f, 2.5f, 0.08f);
            switch (feel.Projectile)
            {
                case ProjectileStyle.ArrowTwin:
                    fx.LaunchArrow(bow + caster.right * 0.12f, target, targetOffset, color, onArrive, feel.ProjectileSize, ArrowSpeed);
                    fx.StartCoroutine(Delayed(SecondHitDelay(skill), () =>
                    {
                        if (target != null && caster != null)
                            fx.LaunchArrow(caster.position + Vector3.up * 1.35f + caster.forward * 0.5f, target, targetOffset, color, null, feel.ProjectileSize, ArrowSpeed);
                    }));
                    break;
                case ProjectileStyle.ArrowRain:
                    // Uma flecha sobe do arco (só para mostrar o disparo) e a chuva desce em cima do alvo.
                    var up = new GameObject("Mira da chuva").transform;
                    up.position = bow + Vector3.up * 6f + caster.forward * 1.5f;
                    fx.LaunchArrow(bow, up, Vector3.zero, color, () => { if (up != null) UnityEngine.Object.Destroy(up.gameObject); }, feel.ProjectileSize, ArrowSpeed);
                    var radius = Mathf.Max(1.5f, skill.AreaRadius);
                    fx.LaunchArrow(target.position + Vector3.up * RainHeight, target, targetOffset, color, onArrive, feel.ProjectileSize, RainSpeed);
                    fx.StartCoroutine(ArrowShower(target.position, color, radius, skill.Id == "saraivada" ? 14 : 8, feel.ProjectileSize));
                    break;
                default:
                    fx.LaunchArrow(bow, target, targetOffset, color, onArrive, feel.ProjectileSize, ArrowSpeed);
                    break;
            }
        }

        private static System.Collections.IEnumerator Delayed(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }

        /// <summary>Flechas extras caindo na área da chuva (cada uma some quando bate no chão).</summary>
        private static System.Collections.IEnumerator ArrowShower(Vector3 around, Color color, float radius, int count, float size)
        {
            for (var i = 0; i < count; i++)
            {
                yield return new WaitForSeconds(0.03f + UnityEngine.Random.value * 0.05f);
                var fx = CombatFx.Instance;
                if (fx == null) yield break;
                var offset = UnityEngine.Random.insideUnitCircle * radius;
                var spot = around + new Vector3(offset.x, 0, offset.y);
                var holder = new GameObject("Flecha da chuva").transform;
                holder.position = new Vector3(spot.x, GroundAt(spot), spot.z);
                fx.LaunchArrow(holder.position + Vector3.up * RainHeight + new Vector3(-0.8f, 0, -0.8f), holder, Vector3.zero, color, () =>
                {
                    if (CombatFx.Instance != null) Dust(holder.position, 0.4f, 3);
                    if (holder != null) UnityEngine.Object.Destroy(holder.gameObject);
                }, size, RainSpeed);
            }
        }

        /// <summary>
        /// Impacto num alvo: faíscas da cor da skill; anel do tamanho da área (uma vez por uso, mesmo acertando vários);
        /// segunda batida nos golpes duplos. Retorna o peso para quem chama aplicar no hit stop, na câmera e no som.
        /// </summary>
        public static float OnImpact(SkillData skill, Vector3 center, ComboGrade? grade, bool critical)
        {
            var fx = CombatFx.Instance;
            var feel = For(skill);
            if (fx == null || skill == null) return feel.Weight;
            var color = ColorFor(skill, grade, critical);
            var heavy = grade == ComboGrade.Excellent || critical;
            fx.Sparks(center, color, heavy ? feel.Sparks * 2 : feel.Sparks);

            var now = Time.unscaledTime;
            var firstOfCast = !LastAreaAt.TryGetValue(skill.Id, out var last) || now - last > 0.25f;
            LastAreaAt[skill.Id] = now;
            if (firstOfCast && skill.AreaRadius > 0 && !feel.AroundCaster)
                Ring(new Vector3(center.x, center.y - 0.6f, center.z), skill.AreaRadius, color, 28 + (int)(skill.AreaRadius * 8));
            if (firstOfCast && feel.Double) fx.StartCoroutine(SecondHit(center, color, feel.Sparks, SecondHitDelay(skill)));
            if (firstOfCast && feel.Weight >= 1f) fx.Sparks(center + Vector3.up * 0.3f, Color.white, 12);
            if (firstOfCast) Signature(skill, center, color, heavy);
            return feel.Weight;
        }

        /// <summary>Tempo entre as duas batidas, pelo golpe da skill (estocada dupla, tempestade, raio duplo).</summary>
        private static float SecondHitDelay(SkillData skill)
        {
            var move = SkillMoves.For(skill.Id);
            return move != null && move.Impacts.Length > 1 ? move.Impacts[1] - move.Impacts[0] : 0.11f;
        }

        // ===== Assinatura de cada skill no impacto =====

        /// <summary>As assinaturas são vistas de longe (câmera atrás do jogador): partículas maiores que as faíscas comuns.</summary>
        private const float Boost = 2.3f;

        private static void EmitBig(Vector3 position, Vector3 velocity, Color color, float size, float lifetime) =>
            CombatFx.Instance?.Emit(position, velocity, color, size * Boost, lifetime * 1.2f);

        private static void GlowBig(Vector3 position, Vector3 velocity, Color color, float size, float lifetime) =>
            CombatFx.Instance?.Glow(position, velocity, color, size * Boost, lifetime * 1.2f);

        private static float GroundAt(Vector3 point) => World.Ground.HeightAt(point.x, point.z) + 0.1f;

        private static void Signature(SkillData skill, Vector3 center, Color color, bool heavy)
        {
            var fx = CombatFx.Instance;
            if (fx == null) return;
            var ground = new Vector3(center.x, GroundAt(center), center.z);
            var floor = ground - Vector3.up * 0.1f;
            // Marcas e cúpulas na cor própria da skill (o dourado do Excellent fica nas faíscas e no clarão).
            var tone = For(skill).Color;
            var area = Mathf.Max(1.2f, skill.AreaRadius);
            switch (skill.Id)
            {
                case "golpe_runico":
                    // Círculo de runas no chão que sobe devagar.
                    for (var i = 0; i < 24; i++)
                    {
                        var a = i / 24f * Mathf.PI * 2f;
                        GlowBig(ground + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.2f, Vector3.up * 0.6f, color, 0.16f, 0.7f);
                    }
                    for (var i = 0; i < 6; i++) GlowBig(center + UnityEngine.Random.insideUnitSphere * 0.4f, Vector3.up * 1.5f, color, 0.25f, 0.6f);
                    fx.Flash(center, color, 2f, 5f, 0.15f);
                    MagicFx.Mark(GroundMarkStyle.Rune, floor, 1.5f, tone * 1.3f, 1.3f);
                    break;
                case "fenda_sismica":
                    // A terra racha: três linhas de pedra saindo do ponto e poeira.
                    for (var line = 0; line < 3; line++)
                    {
                        var a = (line / 3f + UnityEngine.Random.value * 0.1f) * Mathf.PI * 2f;
                        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        for (var i = 0; i < 9; i++)
                            EmitBig(ground + dir * (0.4f * i), Vector3.up * (3.5f - i * 0.25f) + dir * 0.5f, new Color(0.55f, 0.45f, 0.32f), 0.22f, 0.5f);
                    }
                    Dust(ground, 2.5f, 18);
                    fx.Flash(center, new Color(1f, 0.8f, 0.5f), 2f, 6f, 0.15f);
                    MagicFx.Mark(GroundMarkStyle.Crack, floor, area * 0.9f, new Color(2.2f, 1.1f, 0.35f), 2.6f);
                    break;
                case "lamina_flamejante":
                    for (var i = 0; i < 26; i++)
                        EmitBig(center, UnityEngine.Random.insideUnitSphere * 2.5f + Vector3.up * 3f, new Color(1f, 0.35f + UnityEngine.Random.value * 0.4f, 0.08f), 0.2f, 0.55f);
                    for (var i = 0; i < 10; i++) GlowBig(center, UnityEngine.Random.onUnitSphere * 1.2f + Vector3.up, new Color(1f, 0.55f, 0.15f), 0.45f, 0.35f);
                    fx.Flash(center, new Color(1f, 0.55f, 0.2f), 3f, 6f, 0.2f);
                    MagicFx.Mark(GroundMarkStyle.Scorch, floor, 1.3f, new Color(2.5f, 0.9f, 0.2f), 2.2f);
                    MagicFx.Burst(center, 1.1f, new Color(1f, 0.45f, 0.1f), 0.4f);
                    break;
                case "execucao":
                    // Um X vermelho no ar e um clarão.
                    foreach (var tilt in new[] { 1f, -1f })
                        for (var i = -6; i <= 6; i++)
                            GlowBig(center + new Vector3(i * 0.12f, i * 0.12f * tilt, 0), Vector3.zero, new Color(1f, 0.2f, 0.15f), 0.3f, 0.3f);
                    fx.Flash(center, new Color(1f, 0.25f, 0.2f), 4f, 7f, 0.25f);
                    Dust(ground, 1.8f, 12);
                    MagicFx.Mark(GroundMarkStyle.Crack, floor, 1.7f, new Color(2.4f, 0.3f, 0.25f), 2f);
                    break;
                case "tempestade_de_aco":
                    for (var i = 0; i < 30; i++)
                    {
                        var a = i / 30f * Mathf.PI * 2f;
                        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        GlowBig(center + dir * 0.8f, new Vector3(-dir.z, 0.3f, dir.x) * 5f, color, 0.14f, 0.3f);
                    }
                    MagicFx.Mark(GroundMarkStyle.Rune, floor, area, tone * 1.15f, 1.1f);
                    break;
                case "corrente_gelida":
                    // Cristais de gelo brotando e uma geada que fica um pouco no chão.
                    for (var i = 0; i < 22; i++)
                    {
                        var dir = UnityEngine.Random.insideUnitCircle * 1.2f;
                        EmitBig(ground + new Vector3(dir.x, 0, dir.y), Vector3.up * UnityEngine.Random.Range(3f, 6f) + new Vector3(dir.x, 0, dir.y),
                            Color.Lerp(color, Color.white, UnityEngine.Random.value * 0.6f), 0.18f, 0.6f);
                    }
                    for (var i = 0; i < 30; i++)
                    {
                        var a = i / 30f * Mathf.PI * 2f;
                        GlowBig(ground + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * UnityEngine.Random.Range(0.4f, 1.6f), Vector3.up * 0.15f,
                            new Color(0.8f, 0.97f, 1f), 0.2f, 1.1f);
                    }
                    fx.Flash(center, color, 3f, 6f, 0.25f);
                    MagicFx.Mark(GroundMarkStyle.Frost, floor, 2f, new Color(0.7f, 0.95f, 1.2f), 3.2f);
                    break;
                case "explosao_arcana":
                    fx.StartCoroutine(Implode(center, color, Mathf.Max(1.5f, skill.AreaRadius)));
                    MagicFx.Mark(GroundMarkStyle.Rune, floor, area, tone * 1.25f, 1.4f);
                    MagicFx.Burst(center, area * 0.8f, tone, 0.55f);
                    break;
                case "meteoro":
                    Explosion(center, ground, color, Mathf.Max(2f, skill.AreaRadius), 1f);
                    MagicFx.Mark(GroundMarkStyle.Scorch, floor, area, new Color(2.6f, 0.9f, 0.2f), 3f);
                    MagicFx.Burst(center, area * 0.9f, new Color(1f, 0.5f, 0.15f), 0.6f);
                    break;
                case "cataclismo":
                    fx.StartCoroutine(Shockwaves(center, ground, color, Mathf.Max(2f, skill.AreaRadius)));
                    MagicFx.Mark(GroundMarkStyle.Scorch, floor, area * 1.15f, new Color(2.4f, 0.8f, 0.25f), 3.5f);
                    MagicFx.Mark(GroundMarkStyle.Rune, floor, area * 1.3f, tone * 1.2f, 1.6f);
                    MagicFx.Burst(center, area, tone, 0.8f);
                    break;
                case "chuva_de_flechas":
                case "saraivada":
                    Dust(ground, area * 0.7f, skill.Id == "saraivada" ? 18 : 10);
                    MagicFx.Mark(GroundMarkStyle.Rune, floor, area, tone * 1.1f, 1.2f);
                    fx.Flash(center, color, 2f, area * 2f, 0.12f);
                    break;
                case "flecha_flamejante":
                    for (var i = 0; i < 20; i++)
                        EmitBig(center, UnityEngine.Random.insideUnitSphere * 2.2f + Vector3.up * 2.5f, new Color(1f, 0.35f + UnityEngine.Random.value * 0.4f, 0.08f), 0.18f, 0.5f);
                    fx.Flash(center, new Color(1f, 0.55f, 0.2f), 3f, 6f, 0.2f);
                    MagicFx.Mark(GroundMarkStyle.Scorch, floor, area, new Color(2.5f, 0.9f, 0.2f), 2.2f);
                    MagicFx.Burst(center, 1.2f, new Color(1f, 0.45f, 0.1f), 0.4f);
                    break;
                case "tiro_do_falcao":
                    // Penas douradas abrindo como asas e um clarão forte.
                    for (var i = 0; i < 24; i++)
                    {
                        var side = i % 2 == 0 ? 1f : -1f;
                        GlowBig(center, new Vector3(side * UnityEngine.Random.Range(2f, 4f), UnityEngine.Random.Range(0.5f, 2.5f), UnityEngine.Random.Range(-1f, 1f)),
                            Color.Lerp(color, Color.white, UnityEngine.Random.value * 0.5f), 0.22f, 0.5f);
                    }
                    fx.Flash(center, color, 5f, 7f, 0.25f);
                    MagicFx.Burst(center, 1.4f, tone, 0.5f);
                    break;
                case "chute_de_recuo":
                    Dust(ground, 1f, 8);
                    break;
                case "grito_de_guerra":
                case "desafio":
                    // Provocação: um clarão vermelho no alvo (os monstros atingidos passam a atacar o Guardião).
                    fx.Flash(center, color, 2.4f, area > 0 ? area * 2f : 4f, 0.18f);
                    MagicFx.Burst(center, area > 0 ? area * 0.8f : 1.2f, color, 0.4f);
                    break;
                case "terremoto":
                case "colosso":
                    Dust(ground, Mathf.Max(1.5f, area * 0.8f), skill.Id == "colosso" ? 22 : 16);
                    MagicFx.Mark(GroundMarkStyle.Scorch, floor, Mathf.Max(1.5f, area), new Color(1.2f, 0.8f, 0.45f), 2.2f);
                    break;
                case "tiro_certeiro":
                case "flecha_perfurante":
                case "tiro_duplo":
                    fx.Flash(center, color, heavy ? 2.2f : 1.4f, 3f, 0.08f);
                    for (var i = 0; i < 6; i++) GlowBig(center, UnityEngine.Random.onUnitSphere * 1.5f, color, 0.1f, 0.18f);
                    break;
                case "raio_duplo":
                case "faisca":
                case "lanca_de_forca":
                    fx.Flash(center, color, heavy ? 3f : 2f, 4f, 0.1f);
                    for (var i = 0; i < 8; i++) GlowBig(center, UnityEngine.Random.onUnitSphere * 2f, Color.white, 0.12f, 0.2f);
                    MagicFx.Burst(center, heavy ? 0.9f : 0.6f, tone, 0.3f);
                    break;
            }
        }

        private static void Dust(Vector3 ground, float radius, int count)
        {
            var fx = CombatFx.Instance;
            for (var i = 0; i < count; i++)
            {
                var dir = UnityEngine.Random.insideUnitCircle.normalized;
                GlowBig(ground + new Vector3(dir.x, 0, dir.y) * radius * 0.3f, new Vector3(dir.x, 0.25f, dir.y) * radius * 0.8f,
                    new Color(0.6f, 0.55f, 0.48f, 0.6f), 0.6f, 0.9f);
            }
        }

        /// <summary>Explosão: clarão, onda no chão, pedaços voando e fumaça subindo.</summary>
        private static void Explosion(Vector3 center, Vector3 ground, Color color, float radius, float scale)
        {
            var fx = CombatFx.Instance;
            fx.Flash(center, color, 6f * scale, radius * 4f, 0.35f);
            Ring(ground, radius, color, 40);
            for (var i = 0; i < (int)(40 * scale); i++)
                EmitBig(center, UnityEngine.Random.insideUnitSphere * 5f + Vector3.up * 6f, Color.Lerp(color, new Color(0.3f, 0.25f, 0.2f), UnityEngine.Random.value), 0.25f, 0.8f);
            for (var i = 0; i < (int)(16 * scale); i++)
                GlowBig(ground + UnityEngine.Random.insideUnitSphere * radius * 0.4f, Vector3.up * UnityEngine.Random.Range(0.8f, 1.8f),
                    new Color(0.25f, 0.22f, 0.22f, 0.7f), 0.9f, 1.4f);
            for (var i = 0; i < 12; i++) GlowBig(center, UnityEngine.Random.onUnitSphere * 3f, Color.Lerp(color, Color.white, 0.5f), 0.5f, 0.25f);
        }

        private static System.Collections.IEnumerator Implode(Vector3 center, Color color, float radius)
        {
            var fx = CombatFx.Instance;
            // Energia sendo sugada para o ponto, depois duas ondas para fora.
            for (var i = 0; i < 36; i++)
            {
                var from = center + UnityEngine.Random.onUnitSphere * radius;
                GlowBig(from, (center - from) * 5f, color, 0.18f, 0.2f);
            }
            yield return new WaitForSeconds(0.18f);
            if (CombatFx.Instance == null) yield break;
            var ground = new Vector3(center.x, GroundAt(center), center.z);
            fx.Flash(center, color, 4f, radius * 3f, 0.25f);
            Ring(ground, radius, color, 36);
            for (var i = 0; i < 20; i++) GlowBig(center, UnityEngine.Random.onUnitSphere * radius * 2f, color, 0.3f, 0.3f);
            yield return new WaitForSeconds(0.12f);
            if (CombatFx.Instance == null) yield break;
            Ring(ground + Vector3.up * 0.4f, radius * 0.7f, Color.Lerp(color, Color.white, 0.5f), 28);
        }

        private static System.Collections.IEnumerator Shockwaves(Vector3 center, Vector3 ground, Color color, float radius)
        {
            Explosion(center, ground, color, radius, 1.4f);
            for (var wave = 1; wave <= 3; wave++)
            {
                yield return new WaitForSeconds(0.14f);
                if (CombatFx.Instance == null) yield break;
                Ring(ground, radius * (1f + wave * 0.35f), Color.Lerp(color, Color.white, wave * 0.2f), 36 + wave * 8);
                CombatFx.Instance.Flash(center, color, 3f, radius * 3f, 0.12f);
            }
        }

        /// <summary>Pedras extras caindo em volta (Cataclismo).</summary>
        private static System.Collections.IEnumerator MeteorShower(Vector3 around, Color color, float radius)
        {
            for (var i = 0; i < 5; i++)
            {
                yield return new WaitForSeconds(0.12f + i * 0.08f);
                var fx = CombatFx.Instance;
                if (fx == null) yield break;
                var spot = around + new Vector3(UnityEngine.Random.Range(-radius, radius), 0, UnityEngine.Random.Range(-radius, radius));
                var ground = new Vector3(spot.x, GroundAt(spot), spot.z);
                var holder = new GameObject("Pedra do cataclismo").transform;
                holder.position = ground;
                fx.LaunchProjectile(ground + Vector3.up * 12f + new Vector3(-2f, 0, -2f), holder, Vector3.zero, color, () =>
                {
                    if (CombatFx.Instance != null) Explosion(ground + Vector3.up * 0.5f, ground, color, radius * 0.4f, 0.4f);
                    if (holder != null) UnityEngine.Object.Destroy(holder.gameObject);
                }, 1.3f, 1.5f);
            }
        }

        private static System.Collections.IEnumerator SecondHit(Vector3 center, Color color, int sparks, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (CombatFx.Instance == null) yield break;
            CombatFx.Instance.Sparks(center + Vector3.up * 0.15f, color, sparks);
            SoundPlayer.Play(SoundId.Hit, 0.6f, 0.08f);
        }

        /// <summary>Anel de partículas saindo para fora no chão (mostra o tamanho da área).</summary>
        private static void Ring(Vector3 center, float radius, Color color, int count)
        {
            var fx = CombatFx.Instance;
            if (fx == null) return;
            for (var i = 0; i < count; i++)
            {
                var angle = i / (float)count * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                fx.Emit(center + direction * radius * 0.35f, direction * radius * 2.2f + Vector3.up * 1.2f, color, 0.16f, 0.32f);
            }
        }
    }
}
