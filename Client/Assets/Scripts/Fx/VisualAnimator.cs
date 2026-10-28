using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Liga um modelo com Animator aos parâmetros padrão do jogo, se o Animator Controller tiver cada um:
    /// <c>Speed</c> (float, metros por segundo), <c>Dead</c> (bool), <c>Attack</c>, <c>Hit</c> e <c>Dodge</c> (triggers).
    /// Parâmetro que não existe é ignorado; assim um modelo só com "andar" já funciona.
    /// </summary>
    public sealed class VisualAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int DeadId = Animator.StringToHash("Dead");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int AttackKindId = Animator.StringToHash("AttackKind");
        private static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
        private static readonly int RestingId = Animator.StringToHash("Resting");

        private readonly HashSet<int> _parameters = new HashSet<int>();
        private Animator _animator;
        private ProceduralRig _rig;
        private Vector3 _lastPosition;
        private float _speed;

        /// <summary>Morto (o objeto do jogo avisa; o modelo pode ter uma pose de queda).</summary>
        public bool Dead { get; set; }

        /// <summary>Sentado na fogueira; andar inicia a animação de levantar.</summary>
        public bool Resting { get; set; }

        /// <summary>Marca sentado ou de pé no modelo filho de <paramref name="owner"/>.</summary>
        public static void SetResting(Component owner, bool resting)
        {
            if (owner == null) return;
            var visual = owner.GetComponentInChildren<VisualAnimator>(true);
            if (visual != null) visual.Resting = resting;
        }

        /// <summary>Dispara um trigger (Attack, Hit, Dodge) no modelo filho de <paramref name="owner"/>, se houver.</summary>
        public static void Trigger(Component owner, string trigger)
        {
            if (owner == null) return;
            var visual = owner.GetComponentInChildren<VisualAnimator>();
            if (visual != null) visual.Fire(trigger);
        }

        /// <summary>Golpe com estilo (<see cref="AttackStyle"/>): escolhe a animação e dispara o Attack.</summary>
        /// <param name="speed">Ritmo do golpe (o Excellent sai um pouco mais rápido).</param>
        public static void Attack(Component owner, int style, float speed = 1f)
        {
            if (owner == null) return;
            var visual = owner.GetComponentInChildren<VisualAnimator>();
            if (visual == null) return;
            if (visual._rig != null) visual._rig.Trigger("Attack");
            if (visual._animator == null) return;
            if (visual._parameters.Contains(AttackKindId)) visual._animator.SetInteger(AttackKindId, style);
            if (visual._parameters.Contains(AttackSpeedId)) visual._animator.SetFloat(AttackSpeedId, speed);
            if (visual._parameters.Contains(AttackId)) visual._animator.SetTrigger(AttackId);
        }

        /// <summary>Marca morto ou vivo no modelo filho de <paramref name="owner"/>.</summary>
        public static void SetDead(Component owner, bool dead)
        {
            if (owner == null) return;
            var visual = owner.GetComponentInChildren<VisualAnimator>(true);
            if (visual != null) visual.Dead = dead;
        }

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            _rig = GetComponentInChildren<ProceduralRig>();
            _lastPosition = Anchor.position;
            if (_animator == null)
            {
                // Modelo em partes (animais, golem): a animação é por código.
                enabled = _rig != null;
                return;
            }
            _animator.applyRootMotion = false; // quem move é o jogo (servidor ou controle), não a animação
            foreach (var parameter in _animator.parameters) _parameters.Add(parameter.nameHash);
        }

        private void Update()
        {
            // A velocidade vem do objeto do jogo (pai), não do modelo: o avanço visual do golpe não conta como andar.
            var position = Anchor.position;
            var delta = position - _lastPosition;
            delta.y = 0;
            _lastPosition = position;
            var instant = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime : 0f;
            _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-Time.deltaTime * 12f));
            if (_rig != null)
            {
                _rig.Speed = _speed;
                _rig.Dead = Dead;
            }
            if (_animator == null) return;
            if (_parameters.Contains(SpeedId)) _animator.SetFloat(SpeedId, _speed);
            if (_parameters.Contains(DeadId)) _animator.SetBool(DeadId, Dead);
            if (_parameters.Contains(RestingId)) _animator.SetBool(RestingId, Resting && !Dead);
        }

        private Transform Anchor => transform.parent != null ? transform.parent : transform;

        private void Fire(string trigger)
        {
            if (_rig != null) _rig.Trigger(trigger);
            if (trigger == "Hit") HitFlash.Play(gameObject);
            var id = Animator.StringToHash(trigger);
            if (_animator != null && _parameters.Contains(id)) _animator.SetTrigger(id);
        }
    }
}
