using Game.Client.Fx;
using Game.Shared.Accounts;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Prévia do personagem (criação e seleção): o modelo da classe girando devagar, com as cores e a armadura, filmado
    /// por uma câmera própria para uma textura que a tela desenha. Fica no alto, longe do mundo (só o céu atrás).
    /// </summary>
    public sealed class CharacterPreview
    {
        private readonly Vector3 _spot;

        private GameObject _rig;
        private Camera _camera;
        private RenderTexture _texture;
        private GameObject _model;
        private CharacterClass _class;
        private CharacterPaint.Style? _style;
        private CharacterPaint.Gear _gear;
        private bool _painted;
        private string _weapon = "";
        private int _upgrade = -1;

        /// <param name="spot">Onde a prévia fica (no alto, longe do mundo); duas prévias ao mesmo tempo usam lugares diferentes.</param>
        public CharacterPreview(Vector3? spot = null) => _spot = spot ?? new Vector3(0f, 400f, 0f);

        public Texture Texture => _texture;

        /// <summary>
        /// A câmera enquadra um personagem de 1,9 m: o Guardião (2,3 m) é reduzido na prévia para caber inteiro.
        /// </summary>
        private static void FitHeight(GameObject model)
        {
            if (model == null) return;
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>()) bounds.Encapsulate(renderer.bounds);
            if (bounds.size.y > 2f) model.transform.localScale *= 1.9f / bounds.size.y;
        }

        public void Show(CharacterClass characterClass, CharacterPaint.Style style, CharacterPaint.Gear gear = default,
            string weaponId = "", int upgrade = 0)
        {
            if (_rig == null)
            {
                _rig = new GameObject("Prévia do personagem");
                _rig.transform.position = _spot;
                _texture = new RenderTexture(360, 440, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Prévia do personagem" };
                _camera = new GameObject("Câmera da prévia").AddComponent<Camera>();
                _camera.transform.SetParent(_rig.transform, false);
                _camera.transform.localPosition = new Vector3(0f, 1.15f, 3.4f);
                _camera.transform.LookAt(_spot + Vector3.up * 0.85f);
                _camera.fieldOfView = 30f;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
                _camera.targetTexture = _texture;
                _camera.depth = -10;
                _model = null;
                _style = null;
            }
            if (_model == null || characterClass != _class)
            {
                if (_model != null) Object.Destroy(_model);
                _class = characterClass;
                _model = VisualCatalog.Spawn(VisualCatalog.Class, ClassInfo.TypeId(characterClass), _rig.transform);
                FitHeight(_model);
                _style = null;
                _weapon = "";
                _upgrade = -1;
            }
            if (_model == null) return;
            // A arma vestida na mão (sem nada, fica a do próprio modelo).
            if (!string.IsNullOrEmpty(weaponId) && (weaponId != _weapon || upgrade != _upgrade))
            {
                _weapon = weaponId;
                _upgrade = upgrade;
                WeaponFx.SetLook(_model.transform, weaponId, upgrade);
            }
            if (!_style.Equals(style) || !gear.Equals(_gear))
            {
                _style = style;
                _gear = gear;
                CharacterPaint.Apply(_model.transform, gear, style);
            }
            _model.transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.unscaledTime * 0.6f) * 50f, 0f);
        }

        public void Hide()
        {
            if (_rig == null) return;
            Object.Destroy(_rig);
            if (_texture != null) _texture.Release();
            Object.Destroy(_texture);
            _rig = null;
            _model = null;
            _texture = null;
        }
    }
}
