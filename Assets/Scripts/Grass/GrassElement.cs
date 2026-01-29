using UnityEngine;

public class GrassElement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float _bendStrength = 5f;
    [SerializeField] private float _restoreSpeed = 5f;
    // Agrandis cette valeur dans l'inspecteur (ex: 1.5f ou 2.0f) pour une détection plus large
    [SerializeField] private float _detectionRadius = 1.2f;

    [Header("Color Settings")]
    [SerializeField] private float _colorTransitionSpeed = 2f;

    [Header("Detection Settings")]
    [SerializeField] private LayerMask _triggerLayer;

    private SpriteRenderer _spriteRenderer;
    private MaterialPropertyBlock _propBlock;
    private static readonly int _ColorID = Shader.PropertyToID("_Color");

    private Quaternion _initialRotation;
    private Quaternion _targetRotation;
    private Color _initialColor;
    private Color _targetColor;
    private Color _currentColor;

    private bool _hasPlayerInside = false;
    private bool _hasChangedColor = false;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _propBlock = new MaterialPropertyBlock();
        _initialRotation = transform.rotation;
        _targetRotation = _initialRotation;

        _initialColor = _spriteRenderer.sharedMaterial.GetColor(_ColorID);
        _currentColor = _initialColor;
        _targetColor = _initialColor;

        enabled = false;
    }

    private void Update()
    {
        // On vérifie si un objet du layer RigidBody est dans le périmètre élargi
        _hasPlayerInside = Physics.CheckSphere(transform.position, _detectionRadius, _triggerLayer);

        if (!_hasPlayerInside)
        {
            _targetRotation = _initialRotation;
        }

        // Application fluide des mouvements
        transform.rotation = Quaternion.Lerp(transform.rotation, _targetRotation, Time.deltaTime * _restoreSpeed);

        // Transition de couleur pastel rainbow
        if (_currentColor != _targetColor)
        {
            _currentColor = Color.Lerp(_currentColor, _targetColor, Time.deltaTime * _colorTransitionSpeed);
            _spriteRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(_ColorID, _currentColor);
            _spriteRenderer.SetPropertyBlock(_propBlock);
        }

        // Mise en veille si tout est immobile et hors de portée
        if (!_hasPlayerInside && Quaternion.Angle(transform.rotation, _initialRotation) < 0.1f)
        {
            transform.rotation = _initialRotation;
            enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & _triggerLayer) != 0)
        {
            if (!_hasPlayerInside)
            {
                enabled = true;
                CalculateInitialBend(other.transform.position);

                if (!_hasChangedColor)
                {
                    // Rainbow pas trop saturé
                    _targetColor = Color.HSVToRGB(Random.value, 0.5f, 0.8f);
                    _hasChangedColor = true;
                }
            }
            _hasPlayerInside = true;
        }
    }

    private void CalculateInitialBend(Vector3 actorPosition)
    {
        Vector3 diff = transform.position - actorPosition;
        diff.y = 0;
        Vector3 dir = diff.normalized;
        if (dir == Vector3.zero) dir = Vector3.forward;

        // On applique la pliure max basée sur la direction d'entrée
        _targetRotation = _initialRotation * Quaternion.Euler(dir.z * (_bendStrength * 0.5f), 0, -dir.x * _bendStrength);
    }

    // Très utile pour visualiser la zone de détection dans la scène Unity
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, _detectionRadius);
    }
}