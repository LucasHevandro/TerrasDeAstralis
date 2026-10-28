using System.Collections.Generic;
using Game.Client.Combat;
using Game.Shared.Monsters;
using Game.Shared.Net;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Uma entidade do servidor vista pelo cliente (monstro ou outro jogador).
    /// A posição é interpolada entre snapshots, desenhada um pouco no passado (<see cref="InterpolationDelay"/>)
    /// para sempre haver dois pontos entre os quais andar suavemente.
    /// </summary>
    public sealed class NetEntityView : MonoBehaviour
    {
        /// <summary>Atraso da interpolação: dois ticks do servidor (20/s) mais folga para jitter.</summary>
        public const double InterpolationDelay = 0.12;

        private const int MaxSamples = 20;

        private struct Sample
        {
            public double ServerTime;
            public Vector3 Position;
            public float Yaw;
        }

        private readonly List<Sample> _samples = new List<Sample>();
        private GameObject _body;

        // Modelo animado: ao morrer faz a queda e o corpo some depois (monstros); jogadores ficam caídos até renascer.
        private float _hideBodyAt = float.PositiveInfinity;
        private GameObject _ring;
        private Transform _telegraph;
        private float _telegraphStartedAt;
        private bool _windUp;

        public int Id { get; private set; }
        public EntityKind Kind { get; private set; }
        public string TypeId { get; private set; } = "";
        public Targetable Target { get; private set; }

        /// <summary>Dados do monstro conhecidos pelo cliente (do WorldContent), ou nulo.</summary>
        public MonsterData MonsterData { get; private set; }

        public bool IsDead { get; private set; }
        public bool IsEnraged { get; private set; }

        public void Initialize(EntitySpawned spawned, GameObject body, GameObject ring, Transform telegraph,
            Targetable target, MonsterData monsterData)
        {
            Id = spawned.Id;
            Kind = spawned.Kind;
            TypeId = spawned.TypeId;
            _body = body;
            _ring = ring;
            _telegraph = telegraph;
            Target = target;
            MonsterData = monsterData;
            transform.position = Game.Client.World.Ground.Point(spawned.X, spawned.Z);
            _style = new Fx.CharacterPaint.Style(spawned.Hair, spawned.Cloth, spawned.Head, spawned.Skin, spawned.HideHelmet);
            Fx.VisualAnimator.Trigger(this, Kind == EntityKind.Player ? "Spawn" : "Awaken");
        }

        private Fx.CharacterPaint.Style _style;

        /// <summary>
        /// Jogadores: trocou de peça vestida ou mostrou/escondeu o elmo (o metal muda de cor; cores, penteado e pele
        /// ficam os da criação).
        /// </summary>
        public void SetGear(Fx.CharacterPaint.Gear gear, bool hideHelmet)
        {
            _style = _style.WithHelmet(hideHelmet);
            Fx.CharacterPaint.Apply(transform, gear, _style);
        }

        /// <summary>Recebe o estado do snapshot.</summary>
        public void AddSample(double serverTime, in EntityState state)
        {
            if (_samples.Count > 0 && serverTime <= _samples[_samples.Count - 1].ServerTime) return;
            _samples.Add(new Sample { ServerTime = serverTime, Position = Game.Client.World.Ground.Point(state.X, state.Z), Yaw = state.Yaw });
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);

            if (Target != null) Target.SetHealth(state.Hp, Target.Hp.Max);

            var dead = (state.Flags & EntityState.FlagDead) != 0;
            if (dead != IsDead)
            {
                IsDead = dead;
                var animated = _body != null ? _body.GetComponentInChildren<Fx.VisualAnimator>(true) : null;
                // Monstro voltando (estava morto e reviveu): fumaça saindo do chão.
                if (!dead && Kind == EntityKind.Monster) Fx.ItemFx.MonsterSpawn(transform.position, Mathf.Max(0.6f, Target != null ? Target.Radius : 0.6f));
                if (animated != null)
                {
                    _body.SetActive(true);
                    animated.Dead = dead;
                    _hideBodyAt = dead && Kind != EntityKind.Player ? Time.time + 2.2f : float.PositiveInfinity;
                }
                else if (_body != null) _body.SetActive(!dead);
                if (dead && _ring != null) _ring.SetActive(false);
            }

            // Outro jogador sentado na fogueira.
            if (Kind == EntityKind.Player) Fx.VisualAnimator.SetResting(this, (state.Flags & EntityState.FlagResting) != 0);

            var windUp = (state.Flags & EntityState.FlagWindUp) != 0;
            if (windUp && !_windUp)
            {
                _telegraphStartedAt = Time.time;
                Fx.VisualAnimator.Attack(this, Fx.AttackStyle.ForMonster(TypeId));
            }
            _windUp = windUp;
            var enraged = (state.Flags & EntityState.FlagEnraged) != 0;
            if (enraged && !IsEnraged && Kind == EntityKind.Monster)
                Fx.VisualAnimator.Trigger(this, Target != null && Target.Elite ? "TauntLong" : "Taunt");
            IsEnraged = enraged;
        }

        /// <summary>Velocidade a partir da qual um deslocamento entre dois retratos é um salto, não uma corrida (m/s).</summary>
        private const float BlinkSpeed = 25f;

        private double _blinkedTo;

        private static void BlinkFlash(Vector3 at)
        {
            if (Fx.CombatFx.Instance != null) Fx.CombatFx.Instance.Flash(at + Vector3.up, new Color(0.6f, 0.8f, 1f), 2f, 4f, 0.25f);
        }

        /// <summary>Teleporta sem interpolar (ex.: renasceu longe).</summary>
        public void SnapTo(Vector3 position)
        {
            _samples.Clear();
            transform.position = Game.Client.World.Ground.Snap(position);
        }

        private void Update()
        {
            if (IsDead && Time.time >= _hideBodyAt && _body != null && _body.activeSelf) _body.SetActive(false);
            var session = NetworkSession.Instance;
            if (session == null || _samples.Count == 0) return;

            var renderTime = session.ServerNow - InterpolationDelay;
            Sample from = _samples[0], to = _samples[0];
            for (var i = 0; i < _samples.Count; i++)
            {
                if (_samples[i].ServerTime <= renderTime) from = _samples[i];
                if (_samples[i].ServerTime >= renderTime)
                {
                    to = _samples[i];
                    break;
                }
                to = _samples[i];
            }

            var span = to.ServerTime - from.ServerTime;
            var t = span > 0 ? Mathf.Clamp01((float)((renderTime - from.ServerTime) / span)) : 1f;
            // Andou mais rápido do que qualquer um corre (o espectro some e reaparece): pula em vez de deslizar.
            var jump = Vector3.Distance(from.Position, to.Position);
            if (span > 0 && jump > 3f && jump / span > BlinkSpeed)
            {
                if (t >= 0.5f && _blinkedTo != to.ServerTime)
                {
                    _blinkedTo = to.ServerTime;
                    BlinkFlash(from.Position);
                    BlinkFlash(to.Position);
                }
                t = t < 0.5f ? 0f : 1f;
            }
            transform.position = Vector3.Lerp(from.Position, to.Position, t);
            transform.rotation = Quaternion.Euler(0, Mathf.LerpAngle(from.Yaw, to.Yaw, t), 0);

            UpdateTelegraph();
        }

        private void UpdateTelegraph()
        {
            if (_telegraph == null) return;
            var show = _windUp && !IsDead && MonsterData != null;
            if (_telegraph.gameObject.activeSelf != show) _telegraph.gameObject.SetActive(show);
            if (!show) return;

            var progress = MonsterData.AttackWindupSeconds <= 0 ? 1f
                : Mathf.Clamp01((Time.time - _telegraphStartedAt) / MonsterData.AttackWindupSeconds);
            var size = (MonsterData.AttackRange + MonsterData.BodyRadius) * 2f * Mathf.Lerp(0.3f, 1f, progress);
            _telegraph.localScale = new Vector3(size, 0.01f, size);
        }
    }
}
