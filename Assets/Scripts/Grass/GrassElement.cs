using UnityEngine;

public class GrassElement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float _bendStrength = 35f;
    [SerializeField] private float _restoreSpeed = 5f;
    [SerializeField] private float _updateInterval = 0.05f;

    [Header("Color Settings")]
    [SerializeField] private float _colorTransitionSpeed = 2f;

    private SpriteRenderer _spriteRenderer;
    private MaterialPropertyBlock _propBlock;
    private static readonly int _ColorID = Shader.PropertyToID("_Color");

    private Quaternion _initialRotation;
    private Quaternion _targetRotation;

    private Color _initialColor;
    private Color _targetColor;
    private Color _currentColor;

    private int _collidersInside = 0;
    private bool _isChangingColor = false;
    private float _nextUpdateTime;
    private Vector3 _lastActorPos;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _propBlock = new MaterialPropertyBlock();

        _initialRotation = transform.rotation;
        _targetRotation = _initialRotation;

        // On récupère la couleur d'origine. 
        // Note: sharedMaterial est OK ici car on ne fait que lire la valeur par défaut.
        _initialColor = _spriteRenderer.sharedMaterial.GetColor(_ColorID);
        _currentColor = _initialColor;
        _targetColor = _initialColor;

        enabled = false;
    }

    private void Update()
    {
        // 1. Transition de la rotation
        transform.rotation = Quaternion.Lerp(transform.rotation, _targetRotation, Time.deltaTime * _restoreSpeed);

        // 2. Transition de la couleur (Lerp)
        if (_currentColor != _targetColor)
        {
            _currentColor = Color.Lerp(_currentColor, _targetColor, Time.deltaTime * _colorTransitionSpeed);

            _spriteRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(_ColorID, _currentColor);
            _spriteRenderer.SetPropertyBlock(_propBlock);
        }

        // 3. Mise en veille optimisée
        bool isRotating = Quaternion.Angle(transform.rotation, _initialRotation) > 0.1f;

        // On calcule la différence de couleur manuellement (Distance RGB)
        float colorDiff = Mathf.Abs(_currentColor.r - _targetColor.r) +
                          Mathf.Abs(_currentColor.g - _targetColor.g) +
                          Mathf.Abs(_currentColor.b - _targetColor.b);

        bool isColoring = colorDiff > 0.01f;

        if (_collidersInside <= 0 && !isRotating && !isColoring)
        {
            transform.rotation = _initialRotation;
            // On s'assure que la couleur est bien fixée à la cible avant de dormir
            _currentColor = _targetColor;
            enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        _collidersInside++;
        enabled = true;

        if (!_isChangingColor)
        {
            // Rainbow pastel : Hue aléatoire, Saturation 0.5, Valeur 0.8
            _targetColor = Color.HSVToRGB(Random.value, 0.5f, 0.8f);
            _isChangingColor = true;
        }

        CalculateBending(other.transform.position);
    }

    private void OnTriggerStay(Collider other)
    {
        if (Time.time >= _nextUpdateTime)
        {
            CalculateBending(other.transform.position);
            _nextUpdateTime = Time.time + _updateInterval;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        _collidersInside--;
        if (_collidersInside <= 0)
        {
            _collidersInside = 0;
            _targetRotation = _initialRotation;
            // Optionnel : décommente la ligne suivante si tu veux que l'herbe redevienne verte
            // _targetColor = _initialColor; _isChangingColor = false;
        }
    }

    private void CalculateBending(Vector3 actorPosition)
    {
        // 1. Optimisation : Si le perso a bougé de moins de 1cm, on ignore
        if (Vector3.Distance(actorPosition, _lastActorPos) < 0.01f) return;
        _lastActorPos = actorPosition;

        // 2. Calcul de direction stabilisé
        Vector3 diff = transform.position - actorPosition;

        // On ignore la différence de hauteur (Y) pour le calcul de direction au sol
        diff.y = 0;

        // On ajoute une petite marge pour éviter la division par zéro si on est pile dessus
        float distance = diff.magnitude + 0.0001f;
        Vector3 direction = diff / distance;

        // 3. Application de la rotation
        float bendZ = -direction.x * _bendStrength;
        float bendX = direction.z * (_bendStrength * 0.5f);

        _targetRotation = _initialRotation * Quaternion.Euler(bendX, 0, bendZ);
    }
}