using System.Collections.Generic;
using UnityEngine;

public class CharacterWounds : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private WoundDataSO _woundData;
    [SerializeField] private int _maxWounds = 10;
    [SerializeField] private float _woundRadius = 0.5f;
    [SerializeField] private int _sortingOrderOffset = 1;
    [SerializeField] private bool _debugNoMask = false;

    [Header("References")]
    [SerializeField] private Character _character;
    [SerializeField] private Transform _visualParent;

    private Queue<GameObject> _activeWounds = new Queue<GameObject>();

    private void Awake()
    {
        if (_character == null) _character = GetComponentInParent<Character>();
    }

    private void Start()
    {
        if (_character != null && _character.CharacterCombat != null)
        {
            _character.CharacterCombat.OnDamageTaken += HandleDamageTaken;
            _character.OnUnconsciousChanged += HandleUnconsciousChanged;
        }
    }

    private void OnDestroy()
    {
        if (_character != null)
        {
            if (_character.CharacterCombat != null)
                _character.CharacterCombat.OnDamageTaken -= HandleDamageTaken;
            _character.OnUnconsciousChanged -= HandleUnconsciousChanged;
        }
    }

    private void HandleDamageTaken(float amount, MeleeDamageType type)
    {
        if (_woundData == null) return;
        Sprite woundSprite = _woundData.GetRandomSprite(type);
        if (woundSprite == null) return;
        SpawnWound(woundSprite);
    }

    private void SpawnWound(Sprite sprite)
    {
        GameObject woundGo = new GameObject($"Wound_{sprite.name}");
        
        // Simple parenting to character or specific visual parent
        Transform parent = _visualParent != null ? _visualParent : _character.transform;
        woundGo.transform.SetParent(parent);
        
        // Simple random position in a circle around center
        Vector2 randomPoint = Random.insideUnitCircle * _woundRadius;
        woundGo.transform.localPosition = new Vector3(randomPoint.x, randomPoint.y + 1f, -0.01f);
        woundGo.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

        // Setup Renderer
        SpriteRenderer sr = woundGo.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.maskInteraction = _debugNoMask ? SpriteMaskInteraction.None : SpriteMaskInteraction.VisibleInsideMask;
        
        // Sorting
        if (_character.CharacterVisual != null)
        {
            sr.sortingLayerName = _character.CharacterVisual.AllRenderers.Length > 0 ? _character.CharacterVisual.AllRenderers[0].sortingLayerName : "Character";
            sr.sortingOrder = _character.CharacterVisual.GetMaxSortingOrder() + _sortingOrderOffset;
        }

        // Manage Queue
        _activeWounds.Enqueue(woundGo);
        if (_activeWounds.Count > _maxWounds)
        {
            Destroy(_activeWounds.Dequeue());
        }
    }

    private void HandleUnconsciousChanged(bool unconscious)
    {
        if (!unconscious) ClearAllWounds();
    }

    public void ClearAllWounds()
    {
        while (_activeWounds.Count > 0) Destroy(_activeWounds.Dequeue());
    }
}
