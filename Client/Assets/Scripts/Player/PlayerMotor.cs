using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.Player
{
    /// <summary>
    /// Movimento em terceira pessoa relativo à câmera: correr (WASD), andar devagar (Shift) e esquivar (Espaço).
    /// Corre por padrão: segurar Shift o caminho todo até as regiões de caça cansava.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Movimento")]
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float runSpeed = 7.5f;
        [SerializeField] private float turnSpeedDegrees = 720f;
        [SerializeField] private float gravity = -25f;

        [Header("Esquiva")]
        [SerializeField] private float dodgeDistance = 4.5f;
        [SerializeField] private float dodgeDuration = 0.22f;
        [SerializeField] private float dodgeCooldown = 0.8f;

        [Header("Referências")]
        [SerializeField] private Transform cameraTransform;

        private CharacterController _controller;
        private float _verticalVelocity;
        private Vector3 _dodgeDirection;
        private float _dodgeEndsAt;
        private float _nextDodgeAt;

        /// <summary>Verdadeiro durante a esquiva.</summary>
        public bool IsDodging => Time.time < _dodgeEndsAt;

        /// <summary>Velocidade horizontal atual (para animação e HUD).</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>Quando falso, o jogador não anda (ex.: durante um golpe pesado).</summary>
        public bool MovementLocked { get; set; }

        /// <summary>Morto: não anda nem esquiva.</summary>
        public bool Dead { get; set; }

        /// <summary>Correção pequena do servidor, aplicada aos poucos em vez de um salto seco.</summary>
        private Vector3 _correction;
        private float _correctionTime;

        /// <summary>Até esta distância a correção do servidor desliza; acima dela, teleporta.</summary>
        private const float SmoothCorrectionMeters = 3f;
        private const float SmoothCorrectionSeconds = 0.1f;

        public void SetCamera(Transform cam) => cameraTransform = cam;

        /// <summary>Quantas esquivas já começaram (a rede avisa o servidor quando muda).</summary>
        public int DodgeCount { get; private set; }

        private Vector3 _lastStepPosition;
        private float _stepDistance;

        /// <summary>Toca um passo a cada passada (mais longa correndo), alternando o tom entre os pés.</summary>
        private void PlaySteps(bool running)
        {
            var position = transform.position;
            var moved = new Vector2(position.x - _lastStepPosition.x, position.z - _lastStepPosition.z).magnitude;
            _lastStepPosition = position;
            if (moved > 1.5f || IsDodging) return; // teleporte (correção do servidor) ou esquiva não são passos
            _stepDistance += moved;
            var stride = running ? 2.1f : 1.5f;
            if (_stepDistance < stride) return;
            _stepDistance = 0f;
            _leftFoot = !_leftFoot;
            Fx.SoundPlayer.Play(Fx.SoundId.Step, running ? 0.35f : 0.25f, _leftFoot ? 0.08f : 0.12f);
            if (running) Fx.ItemFx.Dust(position + transform.right * (_leftFoot ? -0.15f : 0.15f), 3, 0.6f);
        }

        private bool _leftFoot;

        /// <summary>Move o jogador na hora, sem colisão (correção do servidor, renascer), já em cima do chão.</summary>
        public void Teleport(Vector3 position)
        {
            _controller.enabled = false;
            transform.position = Game.Client.World.Ground.Snap(position) + Vector3.up * 0.05f;
            _controller.enabled = true;
            _verticalVelocity = 0;
        }

        /// <summary>
        /// Correção de posição do servidor: pequena, desliza em ~0,1 s (o jogador quase não percebe); grande (renascer,
        /// entrar na dungeon), teleporta.
        /// </summary>
        public void Correct(Vector3 position)
        {
            var offset = position - transform.position;
            offset.y = 0;
            if (offset.magnitude > SmoothCorrectionMeters)
            {
                _correctionTime = 0;
                Teleport(position);
                return;
            }
            _correction = offset;
            _correctionTime = SmoothCorrectionSeconds;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            // Nulo enquanto digita no chat: o personagem fica parado (mas a gravidade e a esquiva em curso continuam).
            var keyboard = UI.GameInput.Keyboard;
            var input = keyboard != null ? ReadMoveInput(keyboard) : Vector2.zero;
            var moveDirection = ToWorldDirection(input);

            if (UI.KeyBindings.Pressed(UI.GameAction.Dodge) && Time.time >= _nextDodgeAt && !Dead)
            {
                // Esquiva na direção do movimento; parado, esquiva para trás. O corpo vira para onde vai: a animação é
                // de rolar para a frente, e de lado ou de costas o personagem deslizava.
                _dodgeDirection = moveDirection.sqrMagnitude > 0.01f ? moveDirection : -transform.forward;
                transform.rotation = Quaternion.LookRotation(_dodgeDirection);
                _dodgeEndsAt = Time.time + dodgeDuration;
                _nextDodgeAt = Time.time + dodgeCooldown;
                DodgeCount++;
                Fx.VisualAnimator.Trigger(this, "Dodge");
                Fx.ItemFx.Dust(transform.position, 10, 1.4f);
            }

            Vector3 horizontal;
            if (IsDodging)
            {
                horizontal = _dodgeDirection * (dodgeDistance / dodgeDuration);
            }
            else if (MovementLocked)
            {
                horizontal = Vector3.zero;
            }
            else
            {
                var speed = UI.KeyBindings.Held(UI.GameAction.Run) ? walkSpeed : runSpeed;
                horizontal = moveDirection * speed;
                if (moveDirection.sqrMagnitude > 0.01f) FaceDirection(moveDirection);
            }

            if (_controller.isGrounded && _verticalVelocity < 0) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;

            CurrentSpeed = horizontal.magnitude;
            var correction = Vector3.zero;
            if (_correctionTime > 0)
            {
                correction = _correction * Mathf.Min(1f, Time.deltaTime / _correctionTime);
                _correction -= correction;
                _correctionTime -= Time.deltaTime;
            }
            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime + correction);
            StayOnBridge();
            if (CurrentSpeed > 0.5f) PlaySteps(CurrentSpeed > walkSpeed + 0.1f);
            else _lastStepPosition = transform.position;
        }

        /// <summary>A ponte não tem colisão: em cima dela, o jogador sobe até as tábuas e para de cair.</summary>
        private void StayOnBridge()
        {
            var position = transform.position;
            var deck = Game.Client.World.Ground.BridgeDeck(position.x, position.z);
            if (float.IsNegativeInfinity(deck) || position.y >= deck - 0.01f) return;
            _controller.Move(Vector3.up * (deck - position.y));
            if (_verticalVelocity < 0) _verticalVelocity = -2f;
        }

        /// <summary>Gira o jogador para olhar um ponto (usado ao atacar um alvo).</summary>
        public void FacePoint(Vector3 point)
        {
            var direction = point - transform.position;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        private void FaceDirection(Vector3 direction)
        {
            var target = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeedDegrees * Time.deltaTime);
        }

        private static Vector2 ReadMoveInput(Keyboard keyboard)
        {
            var x = (UI.KeyBindings.Held(UI.GameAction.MoveRight) ? 1 : 0) - (UI.KeyBindings.Held(UI.GameAction.MoveLeft) ? 1 : 0);
            var y = (UI.KeyBindings.Held(UI.GameAction.MoveForward) ? 1 : 0) - (UI.KeyBindings.Held(UI.GameAction.MoveBack) ? 1 : 0);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private Vector3 ToWorldDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.01f) return Vector3.zero;
            var reference = cameraTransform != null ? cameraTransform : transform;
            var forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
            return (forward * input.y + right * input.x).normalized;
        }
    }
}
