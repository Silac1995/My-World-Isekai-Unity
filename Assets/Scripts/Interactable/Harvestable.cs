using System.Collections.Generic;
using UnityEngine;
using MWI.Interactables;

/// <summary>
/// Objet récoltable dans le monde (arbre, roche, veine de minerai...).
/// Hérite d'InteractableObject pour être interactif.
/// Produit une liste d'ItemSO quand un personnage le récolte.
/// Peut s'épuiser après un nombre de récoltes et respawn après un délai.
/// </summary>
public class Harvestable : InteractableObject
{
    [Header("Harvestable")]
    [SerializeField] private HarvestableCategory _category = HarvestableCategory.Wood;
    [SerializeField] private List<ItemSO> _outputItems = new List<ItemSO>();
    [SerializeField] private float _harvestDuration = 3f;
    [SerializeField] private bool _isDepletable = true;
    [SerializeField] private int _maxHarvestCount = 5;
    [SerializeField, Tooltip("Nombre de jours in-game avant la réapparition de la ressource")] 
    private int _respawnDelayDays = 1;

    private int _currentHarvestCount = 0;
    private bool _isDepleted = false;
    private int _targetRespawnDay = 0;

    public event System.Action<Harvestable> OnRespawned;

    // Visuels (optionnel)
    [Header("Visuals")]
    [SerializeField] private GameObject _visualRoot;

    /// <summary>Items que cet objet peut produire</summary>
    public IReadOnlyList<ItemSO> OutputItems => _outputItems;

    /// <summary>Catégorie de ressource pour filtrage</summary>
    public HarvestableCategory Category => _category;

    /// <summary>Temps nécessaire pour une récolte</summary>
    public float HarvestDuration => _harvestDuration;

    /// <summary>L'objet est-il épuisé ?</summary>
    public bool IsDepleted => _isDepleted;

    /// <summary>
    /// Remaining yield this harvestable can produce. int.MaxValue if non-depletable.
    /// </summary>
    public int RemainingYield => _isDepletable
        ? Mathf.Max(0, _maxHarvestCount - _currentHarvestCount)
        : int.MaxValue;

    /// <summary>Peut-on récolter cet objet ?</summary>
    public bool CanHarvest() => !_isDepleted && _outputItems.Count > 0;

    /// <summary>
    /// Vérifie si cet objet produit un item spécifique.
    /// Utilisé par les harvesters pour trouver des zones compatibles.
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
    /// Interaction : lance une CharacterHarvestAction pour récolter avec animation et durée.
    /// </summary>
    public override void Interact(Character interactor)
    {
        if (interactor == null || !CanHarvest()) return;
        if (interactor.CharacterActions == null) return;

        var gatherAction = new CharacterHarvestAction(interactor, this);
        interactor.CharacterActions.ExecuteAction(gatherAction);
    }

    /// <summary>
    /// Récolte et fait spawn l'item en WorldItem dans le monde.
    /// Retourne l'ItemSO récolté (null si échec).
    /// </summary>
    public ItemSO Harvest(Character harvester)
    {
        if (harvester == null || !CanHarvest()) return null;

        ItemSO harvestedItem = GetRandomOutput();
        if (harvestedItem == null) return null;

        _currentHarvestCount++;

        // Quest progress: if the harvester has an active HarvestResourceTask whose
        // target IS this harvestable, record one unit of progress so the HUD updates
        // live (and the quest auto-completes when TotalProgress >= Required). Server-only
        // because Harvest itself is server-only (CharacterActions.ApplyHarvestOnServer
        // gates this with `if (IsSpawned && !IsServer) return null;`).
        //
        // Recorded BEFORE Deplete so the final harvest goes through the "Completed" state
        // path (TotalProgress >= Required while IsValid still true) instead of "Expired"
        // (IsValid flips false the moment we Deplete). End-user impact is the same — the
        // quest disappears from the log either way — but Completed is the natural state.
        NotifyHarvesterQuestProgress(harvester);

        if (_isDepletable && _currentHarvestCount >= _maxHarvestCount)
        {
            Deplete();
        }

        Debug.Log($"<color=green>[Harvest]</color> {harvester.CharacterName} a récolté {harvestedItem.ItemName}.");
        return harvestedItem;
    }

    private void NotifyHarvesterQuestProgress(Character harvester)
    {
        if (harvester == null || harvester.CharacterQuestLog == null) return;
        foreach (var quest in harvester.CharacterQuestLog.ActiveQuests)
        {
            if (quest is HarvestResourceTask hrt && hrt.HarvestableTarget == this)
            {
                quest.RecordProgress(harvester, 1);
                return;
            }
        }
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
    /// Épuise la ressource. Cache les visuels et s'abonne à l'événement de nouveau jour.
    /// </summary>
    private void Deplete()
    {
        _isDepleted = true;

        if (MWI.Time.TimeManager.Instance != null)
        {
            _targetRespawnDay = MWI.Time.TimeManager.Instance.CurrentDay + _respawnDelayDays;
            MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        }

        if (_visualRoot != null)
            _visualRoot.SetActive(false);

        Debug.Log($"<color=orange>[Harvest]</color> {gameObject.name} est épuisé. Respawn prévu au jour {_targetRespawnDay}.");
    }

    private void HandleNewDay()
    {
        if (MWI.Time.TimeManager.Instance != null && MWI.Time.TimeManager.Instance.CurrentDay >= _targetRespawnDay)
        {
            Respawn();
        }
    }

    /// <summary>
    /// Respawn la ressource. Remet les visuels et le compteur à zéro.
    /// </summary>
    private void Respawn()
    {
        _isDepleted = false;
        _currentHarvestCount = 0;

        if (MWI.Time.TimeManager.Instance != null)
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;

        if (_visualRoot != null)
            _visualRoot.SetActive(true);

        Debug.Log($"<color=green>[Harvest]</color> {gameObject.name} a respawn !");
        OnRespawned?.Invoke(this);
    }

    private void OnDestroy()
    {
        if (_isDepleted && MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
    }
}
