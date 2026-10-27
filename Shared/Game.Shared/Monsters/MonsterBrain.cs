using System;
using System.Numerics;
using Game.Shared.Combat;

namespace Game.Shared.Monsters
{
    /// <summary>Estado da IA do monstro.</summary>
    public enum MonsterState
    {
        Idle,
        Wander,
        Chase,
        WindUp,
        Return,
        Dead,

        /// <summary>Fugindo de quem bate (coelho com pouca vida).</summary>
        Flee,

        /// <summary>Preparando a investida: abaixa a cabeça e mira (o aviso aparece).</summary>
        ChargeWindUp,

        /// <summary>Correndo em linha reta até o ponto mirado.</summary>
        Charge,

        /// <summary>Recuando depois de bater (aranha).</summary>
        Retreat,
    }

    /// <summary>O que a IA pede ao corpo do monstro neste tick.</summary>
    public enum MonsterActionKind
    {
        /// <summary>Ficar parado.</summary>
        None,

        /// <summary>Andar até <see cref="MonsterAction.Point"/> (na velocidade vezes <see cref="MonsterAction.SpeedFactor"/>).</summary>
        MoveTo,

        /// <summary>Parar e virar para <see cref="MonsterAction.Point"/>.</summary>
        Face,

        /// <summary>Começou a preparar o golpe: mostrar o aviso.</summary>
        AttackStarted,

        /// <summary>O golpe saiu: quem controla o corpo confere alcance/esquiva e aplica o dano.</summary>
        AttackLanded,

        /// <summary>Voltou para casa: recuperar a vida.</summary>
        ResetHealth,

        /// <summary>Some e reaparece em <see cref="MonsterAction.Point"/> (espectro).</summary>
        Blink,
    }

    /// <summary>Pedido da IA para este tick.</summary>
    public readonly struct MonsterAction
    {
        public MonsterActionKind Kind { get; }
        public Vector2 Point { get; }

        /// <summary>Multiplica a velocidade de andar (investida, fuga, fúria).</summary>
        public float SpeedFactor { get; }

        public MonsterAction(MonsterActionKind kind, Vector2 point = default, float speedFactor = 1f)
        {
            Kind = kind;
            Point = point;
            SpeedFactor = speedFactor;
        }

        public static MonsterAction None => new MonsterAction(MonsterActionKind.None);
    }

    /// <summary>
    /// IA do monstro, sem motor gráfico: recebe posições no plano do chão (X, Z do mundo como X, Y) e devolve o que o
    /// corpo deve fazer. O cliente usa no modo offline; o servidor usa a mesma IA.
    ///
    /// Base: parado/passeando → (alvo perto e agressivo, ou provocado) → persegue → no alcance prepara o golpe → o golpe
    /// sai → persegue de novo. Longe demais de casa → volta, recupera a vida e esquece a provocação.
    /// Cada <see cref="MonsterBehavior"/> muda um pedaço disso (fugir, investir, recuar, sumir e reaparecer, enfurecer).
    /// O estado do comportamento fica aqui, e não no <see cref="MonsterData"/>, que é o mesmo para o grupo inteiro.
    /// </summary>
    public sealed class MonsterBrain
    {
        private const float ArriveDistance = 0.4f;

        private readonly MonsterData _data;
        private readonly IRandomSource _random;
        private bool _provoked;
        private double _nextAttackAt = double.NegativeInfinity;
        private double _windupEndsAt;
        private double _nextWanderAt;
        private Vector2 _wanderPoint;

        // Comportamentos
        private double _stateEndsAt;
        private Vector2 _runPoint;
        private bool _fled;
        private double _nextChargeAt;
        private double _nextBlinkAt;
        private double _noticedAt = double.NaN;

        public MonsterState State { get; private set; } = MonsterState.Idle;
        public Vector2 Home { get; private set; }
        public MonsterData Data => _data;

        /// <summary>Quando o golpe em preparo vai sair (válido em <see cref="MonsterState.WindUp"/>).</summary>
        public double WindupEndsAt => _windupEndsAt;

        /// <summary>Preparando um golpe (normal ou a investida): o aviso fica visível.</summary>
        public bool IsWindingUp => State is MonsterState.WindUp or MonsterState.ChargeWindUp;

        /// <summary>Furioso: o cavaleiro com pouca vida bate mais rápido e anda mais rápido.</summary>
        public bool Enraged { get; private set; }

        /// <summary>Já foi provocado (perseguindo quem bateu).</summary>
        public bool Provoked => _provoked;

        public MonsterBrain(MonsterData data, Vector2 home, IRandomSource random)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Home = home;
        }

        /// <summary>Chamar quando o monstro apanha: passa a perseguir mesmo se for passivo.</summary>
        public void Provoke()
        {
            if (State != MonsterState.Dead && State != MonsterState.Return) _provoked = true;
        }

        public void Kill() => State = MonsterState.Dead;

        /// <summary>Renasce parado em casa.</summary>
        public void Revive(double now)
        {
            State = MonsterState.Idle;
            _provoked = false;
            _nextAttackAt = double.NegativeInfinity;
            _nextWanderAt = now + 2;
            ForgetFight(now);
        }

        /// <summary>
        /// Um passo da IA. <paramref name="target"/> é a posição do alvo (jogador) ou nulo se não há alvo válido;
        /// <paramref name="hpFraction"/> é a vida do monstro de 0 a 1 (alguns comportamentos dependem dela).
        /// </summary>
        public MonsterAction Tick(double now, Vector2 self, Vector2? target, float hpFraction = 1f)
        {
            if (State != MonsterState.Dead && _data.Behavior == MonsterBehavior.Berserk && !Enraged && hpFraction <= MonsterBehaviors.BerserkAtHp)
                Enraged = true;

            // Coelho com pouca vida entra em pânico e foge, até no meio do preparo do golpe (uma vez por luta).
            if (_data.Behavior == MonsterBehavior.Skittish && !_fled && target.HasValue && hpFraction <= MonsterBehaviors.FleeAtHp &&
                State is MonsterState.Chase or MonsterState.WindUp)
            {
                _fled = true;
                State = MonsterState.Flee;
                _stateEndsAt = now + MonsterBehaviors.FleeSeconds;
                _runPoint = Away(self, target.Value, MonsterBehaviors.FleeDistance);
                return new MonsterAction(MonsterActionKind.MoveTo, _runPoint, MonsterBehaviors.FleeSpeed);
            }

            switch (State)
            {
                case MonsterState.Dead:
                    return MonsterAction.None;

                case MonsterState.Return:
                    if (Vector2.Distance(self, Home) <= ArriveDistance)
                    {
                        State = MonsterState.Idle;
                        _nextWanderAt = now + 2;
                        ForgetFight(now);
                        return new MonsterAction(MonsterActionKind.ResetHealth);
                    }
                    return new MonsterAction(MonsterActionKind.MoveTo, Home);

                case MonsterState.Idle:
                case MonsterState.Wander:
                    if (!target.HasValue || !WantsToFight(self, target.Value)) _noticedAt = double.NaN;
                    else if (!_provoked && _data.NoticeSeconds > 0 && (double.IsNaN(_noticedAt) || now - _noticedAt < _data.NoticeSeconds))
                    {
                        // Notou: vira para o alvo e espera um instante antes de atacar.
                        if (double.IsNaN(_noticedAt)) _noticedAt = now;
                        return new MonsterAction(MonsterActionKind.Face, target.Value);
                    }
                    if (target.HasValue && WantsToFight(self, target.Value))
                    {
                        _noticedAt = double.NaN;
                        State = MonsterState.Chase;
                        // Começo da luta: a investida e o sumiço só ficam prontos depois de um respiro.
                        _nextChargeAt = Math.Max(_nextChargeAt, now + MonsterBehaviors.OpeningSeconds);
                        _nextBlinkAt = Math.Max(_nextBlinkAt, now + MonsterBehaviors.OpeningSeconds);
                        return Chase(now, self, target.Value);
                    }
                    return Wander(now, self);

                case MonsterState.Chase:
                    if (!target.HasValue) return GoHome();
                    return Chase(now, self, target.Value);

                case MonsterState.WindUp:
                    if (now < _windupEndsAt)
                        return target.HasValue ? new MonsterAction(MonsterActionKind.Face, target.Value) : MonsterAction.None;
                    _nextAttackAt = now + AttackCooldown;
                    if (_data.Behavior == MonsterBehavior.HitAndRun && target.HasValue)
                    {
                        // Bate e recua: corre um pouco para longe antes de voltar.
                        State = MonsterState.Retreat;
                        _stateEndsAt = now + MonsterBehaviors.RetreatSeconds;
                        _runPoint = Away(self, target.Value, MonsterBehaviors.RetreatDistance);
                    }
                    else
                    {
                        State = MonsterState.Chase;
                    }
                    return new MonsterAction(MonsterActionKind.AttackLanded, target ?? self);

                case MonsterState.Flee:
                case MonsterState.Retreat:
                    if (now >= _stateEndsAt || Vector2.Distance(self, _runPoint) <= ArriveDistance)
                    {
                        State = target.HasValue ? MonsterState.Chase : MonsterState.Idle;
                        return MonsterAction.None;
                    }
                    if (Vector2.Distance(self, Home) > _data.LeashRadius) return GoHome();
                    return new MonsterAction(MonsterActionKind.MoveTo, _runPoint,
                        State == MonsterState.Flee ? MonsterBehaviors.FleeSpeed : MonsterBehaviors.RetreatSpeed);

                case MonsterState.ChargeWindUp:
                    if (now < _stateEndsAt)
                        return target.HasValue ? new MonsterAction(MonsterActionKind.Face, target.Value) : MonsterAction.None;
                    // Mira onde o alvo está agora e passa um pouco do ponto: quem não sair da frente leva.
                    var aim = target ?? _runPoint;
                    var line = aim - self;
                    var length = line.Length();
                    _runPoint = length > 0.01f ? aim + line / length * MonsterBehaviors.ChargeOvershoot : aim;
                    State = MonsterState.Charge;
                    _stateEndsAt = now + MonsterBehaviors.ChargeMaxSeconds;
                    return new MonsterAction(MonsterActionKind.MoveTo, _runPoint, MonsterBehaviors.ChargeSpeed);

                case MonsterState.Charge:
                    var hit = target.HasValue && Vector2.Distance(self, target.Value) <= _data.AttackRange;
                    if (hit || now >= _stateEndsAt || Vector2.Distance(self, _runPoint) <= ArriveDistance)
                    {
                        State = MonsterState.Chase;
                        _nextAttackAt = now + AttackCooldown;
                        // O golpe da investida só sai se ela pegou o alvo no caminho.
                        return hit ? new MonsterAction(MonsterActionKind.AttackLanded, target!.Value) : MonsterAction.None;
                    }
                    return new MonsterAction(MonsterActionKind.MoveTo, _runPoint, MonsterBehaviors.ChargeSpeed);

                default:
                    return MonsterAction.None;
            }
        }

        private float AttackCooldown => _data.AttackCooldownSeconds * (Enraged ? MonsterBehaviors.BerserkCooldownFactor : 1f);

        private float ChaseSpeed => Enraged ? MonsterBehaviors.BerserkSpeed : 1f;

        private bool WantsToFight(Vector2 self, Vector2 target) =>
            _provoked || (_data.Aggressive && Vector2.Distance(self, target) <= _data.AggroRadius);

        private MonsterAction Chase(double now, Vector2 self, Vector2 target)
        {
            if (Vector2.Distance(self, Home) > _data.LeashRadius) return GoHome();
            var distance = Vector2.Distance(self, target);

            // Touro: de longe, prepara a investida.
            if (_data.Behavior == MonsterBehavior.Charger && now >= _nextChargeAt &&
                distance >= MonsterBehaviors.ChargeMinDistance && distance <= MonsterBehaviors.ChargeMaxDistance)
            {
                _nextChargeAt = now + MonsterBehaviors.ChargeCooldownSeconds;
                State = MonsterState.ChargeWindUp;
                _stateEndsAt = now + MonsterBehaviors.ChargeWindupSeconds;
                _runPoint = target;
                return new MonsterAction(MonsterActionKind.AttackStarted, target);
            }

            // Espectro: se o alvo se afasta, some e reaparece perto dele.
            if (_data.Behavior == MonsterBehavior.Blink && now >= _nextBlinkAt && distance >= MonsterBehaviors.BlinkMinDistance)
            {
                _nextBlinkAt = now + MonsterBehaviors.BlinkCooldownSeconds;
                // Reaparece do outro lado do alvo (atrás dele, visto de onde o espectro estava).
                return new MonsterAction(MonsterActionKind.Blink, Away(target, self, MonsterBehaviors.BlinkLandDistance));
            }

            if (distance <= _data.AttackRange)
            {
                if (now < _nextAttackAt) return new MonsterAction(MonsterActionKind.Face, target);
                State = MonsterState.WindUp;
                _windupEndsAt = now + _data.AttackWindupSeconds;
                return new MonsterAction(MonsterActionKind.AttackStarted, target);
            }
            return new MonsterAction(MonsterActionKind.MoveTo, target, ChaseSpeed);
        }

        /// <summary>Ponto a <paramref name="distance"/> metros de <paramref name="from"/>, do lado oposto a <paramref name="threat"/>.</summary>
        private Vector2 Away(Vector2 from, Vector2 threat, float distance)
        {
            var direction = from - threat;
            if (direction.LengthSquared() < 0.0001f)
            {
                var angle = _random.NextDouble() * Math.PI * 2;
                direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            }
            return from + Vector2.Normalize(direction) * distance;
        }

        private MonsterAction GoHome()
        {
            State = MonsterState.Return;
            _provoked = false;
            return new MonsterAction(MonsterActionKind.MoveTo, Home);
        }

        /// <summary>A luta acabou (morreu ou voltou para casa): fuga, fúria e recargas voltam ao começo.</summary>
        private void ForgetFight(double now)
        {
            _fled = false;
            Enraged = false;
            _noticedAt = double.NaN;
            _nextChargeAt = now + MonsterBehaviors.OpeningSeconds;
            _nextBlinkAt = now + MonsterBehaviors.OpeningSeconds;
        }

        private MonsterAction Wander(double now, Vector2 self)
        {
            if (_data.WanderRadius <= 0) return MonsterAction.None;

            if (State == MonsterState.Wander)
            {
                if (Vector2.Distance(self, _wanderPoint) > ArriveDistance)
                    return new MonsterAction(MonsterActionKind.MoveTo, _wanderPoint);
                State = MonsterState.Idle;
                _nextWanderAt = now + 3 + _random.NextDouble() * 4;
                return MonsterAction.None;
            }

            if (now < _nextWanderAt) return MonsterAction.None;
            var angle = _random.NextDouble() * Math.PI * 2;
            var distance = _data.WanderRadius * Math.Sqrt(_random.NextDouble());
            _wanderPoint = Home + new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
            State = MonsterState.Wander;
            return new MonsterAction(MonsterActionKind.MoveTo, _wanderPoint);
        }
    }
}
