using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Objet récoltable dans le monde (arbre, roche, veine de minerai...).
/// Hérite d'InteractableObject pour être interactif.
/// Produit une liste d'ItemSO quand un personnage le récolte.
/// Peut s'épuiser après un nombre de récoltes et respawn après un délai.
/// </summary>
public class GatherableObject : InteractableObject
{
    [Header("Gatherable")]
    [SerializeField] private List<ItemSO> _outputItems = new List<ItemSO>();
    [SerializeField] private float _gatherDuration = 3f;
    [SerializeField] private bool _isDepletable = true;
    [SerializeField] private int _maxGatherCount = 5;
    [SerializeField] private float _respawnTime = 60f;

    private int _currentGatherCount = 0;
    private bool _isDepleted = false;
    private float _respawnTimer = 0f;

    public event System.Action<GatherableObject> OnRespawned;

    // Visuels (optionnel)
    [Header("Visuals")]
    [SerializeField] private GameObject _visualRoot;

    /// <summary>Items que cet objet peut produire</summary>
    public IReadOnlyList<ItemSO> OutputItems => _outputItems;

    /// <summary>Temps nécessaire pour une récolte</summary>
    public float GatherDuration => _gatherDuration;

    /// <summary>L'objet est-il épuisé ?</summary>
    public bool IsDepleted => _isDepleted;

    /// <summary>Peut-on récolter cet objet ?</summary>
    public bool CanGather() => !_isDepleted && _outputItems.Count > 0;

    /// <summary>
    /// Vérifie si cet objet produit un item spécifique.
    /// Utilisé par les gatherers pour trouver des zones compatibles.
    /// </summary>
    public bool HasOutput(ItemSO item)
    {
        if (item == null) return false;
        return _outputItems.Contains(item);
    }

    /// <summary>
    /// Vérifie si cet objet produit au moins un des items de la liste.
    /// </summary>
    public bool HasAnyOutput(List<ItemSO> items)
    {
        if (items == null) return false;
        foreach (var item in items)
        {
            if (HasOutput(item)) return true;
        }
        return false;
    }

    /// <summary>
    /// Interaction : lance une CharacterGatherAction pour récolter avec animation et durée.
    /// </summary>
    public override void Interact(Character interactor)
    {
        if (interactor == null || !CanGather()) return;
        if (interactor.CharacterActions == null) return;

        var gatherAction = new CharacterGatherAction(interactor, this);
        interactor.CharacterActions.ExecuteAction(gatherAction);
    }

    /// <summary>
    /// Récolte et fait spawn l'item en WorldItem dans le monde.
    /// Retourne l'ItemSO récolté (null si échec).
    /// </summary>
    public ItemSO Gather(Character gatherer)
    {
        if (gatherer == null || !CanGather()) return null;

        ItemSO harvestedItem = GetRandomOutput();
        if (harvestedItem == null) return null;

        _currentGatherCount++;

        if (_isDepletable && _currentGatherCount >= _maxGatherCount)
        {
            Deplete();
        }

        Debug.Log($"<color=green>[Gather]</color> {gatherer.CharacterName} a récolté {harvestedItem.ItemName}.");
        return harvestedItem;
    }

    /// <summary>
    /// Retourne un item aléatoire parmi les outputs.
    /// </summary>
    private ItemSO GetRandomOutput()
    {
        if (_outputItems.Count == 0) return null;
        return _outputItems[Random.Range(0, _outputItems.Count)];
    }

    /// <summary>
    /// Épuise la ressource. Cache les visuels et lance le timer de respawn.
    /// </summary>
    private void Deplete()
    {
        _isDepleted = true;
        _respawnTimer = _respawnTime;

        if (_visualRoot != null)
            _visualRoot.SetActive(false);

        Debug.Log($"<color=orange>[Gather]</color> {gameObject.name} est épuisé. Respawn dans {_respawnTime}s.");
    }

    /// <summary>
    /// Respawn la ressource. Remet les visuels et le compteur à zéro.
    /// </summary>
    private void Respawn()
    {
        _isDepleted = false;
        _currentGatherCount = 0;

        if (_visualRoot != null)
            _visualRoot.SetActive(true);

        Debug.Log($"<color=green>[Gather]</color> {gameObject.name} a respawn !");
        OnRespawned?.Invoke(this);
    }

    private void Update()
    {
        if (!_isDepleted) return;

        _respawnTimer -= UnityEngine.Time.deltaTime;
        if (_respawnTimer <= 0f)
        {
            Respawn();
        }
    }
}
