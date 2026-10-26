using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.Player
{
    /// <summary>
    /// Câmera orbital em terceira pessoa: botão direito do mouse gira, roda do mouse aproxima.
    /// Evita atravessar paredes e oferece tremida de câmera para impactos.
    /// </summary>
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.5f, 0);

        [Header("Órbita")]
        [SerializeField] private float sensitivity = 0.2f;
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 70f;

        [Header("Zoom")]
        [SerializeField] private float distance = 7f;
        [SerializeField] private float minDistance = 2.5f;
        [SerializeField] private float maxDistance = 14f;
        [SerializeField] private float zoomStep = 0.01f;

        [Header("Colisão")]
        [SerializeField] private float collisionRadius = 0.3f;
        [SerializeField] private LayerMask collisionMask = ~0;

        private float _yaw;
        private float _pitch = 20f;
        private float _shakeAmplitude;
        private float _shakeEndsAt;
        private float _shakeDuration;

        // Suavização: a altura do pivô segue o jogador com um pequeno atraso (degraus e correções do servidor não
        // pulam na tela), e a câmera volta devagar depois de encostar num obstáculo (entra rápido, sai devagar).
        private float _pivotY = float.NaN;
        private float _pivotYVelocity;
        private float _shownDistance = float.NaN;
        private const float PivotSmoothSeconds = 0.08f;
        private const float DistanceReturnSpeed = 6f;

        public void SetTarget(Transform newTarget) => target = newTarget;

        /// <summary>Gira a câmera para olhar na direção dada (testes automáticos).</summary>
        public void LookAlong(Vector3 direction) => _yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        /// <summary>Treme a câmera. Uma tremida mais forte substitui a atual.</summary>
        public void Shake(float amplitude, float duration)
        {
            if (Time.unscaledTime < _shakeEndsAt && amplitude < _shakeAmplitude) return;
            _shakeAmplitude = amplitude;
            _shakeDuration = duration;
            _shakeEndsAt = Time.unscaledTime + duration;
        }

        private void Start()
        {
            if (target != null) _yaw = target.eulerAngles.y;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            ReadInput();

            var rotation = Quaternion.Euler(_pitch, _yaw, 0);
            var pivot = target.position + targetOffset;
            var dt = Time.unscaledDeltaTime;
            // Salto grande (renascer, dungeon): acompanha na hora.
            if (float.IsNaN(_pivotY) || Mathf.Abs(pivot.y - _pivotY) > 5f) _pivotY = pivot.y;
            else _pivotY = Mathf.SmoothDamp(_pivotY, pivot.y, ref _pivotYVelocity, PivotSmoothSeconds, Mathf.Infinity, dt);
            pivot.y = _pivotY;
            var desired = pivot - rotation * Vector3.forward * distance;

            // Encosta a câmera em obstáculos entre o jogador e ela.
            var toCamera = desired - pivot;
            var actualDistance = distance;
            if (Physics.SphereCast(pivot, collisionRadius, toCamera.normalized, out var hit, distance, collisionMask,
                    QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(target))
            {
                actualDistance = Mathf.Max(0.5f, hit.distance);
            }

            _shownDistance = float.IsNaN(_shownDistance) || actualDistance < _shownDistance
                ? actualDistance
                : Mathf.MoveTowards(_shownDistance, actualDistance, DistanceReturnSpeed * dt);
            transform.position = pivot - rotation * Vector3.forward * _shownDistance + ShakeOffset(rotation);
            transform.rotation = rotation;
        }

        private void ReadInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.rightButton.isPressed && !UI.GameWindows.PointerOverUi)
            {
                var delta = mouse.delta.ReadValue();
                _yaw += delta.x * sensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, minPitch, maxPitch);
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - scroll * zoomStep, minDistance, maxDistance);
        }

        private Vector3 ShakeOffset(Quaternion rotation)
        {
            var remaining = _shakeEndsAt - Time.unscaledTime;
            if (remaining <= 0) return Vector3.zero;
            var strength = _shakeAmplitude * (remaining / _shakeDuration);
            // Ruído suave em vez de um ponto ao acaso por quadro: treme sem "chiar".
            var t = Time.unscaledTime * 28f;
            var offset = new Vector2(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f) * (2f * strength);
            return rotation * new Vector3(offset.x, offset.y, 0);
        }
    }
}
