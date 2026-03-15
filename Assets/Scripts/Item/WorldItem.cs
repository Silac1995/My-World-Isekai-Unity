using UnityEngine;

[RequireComponent(typeof(UnityEngine.Rendering.SortingGroup))]
public class WorldItem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _visualRoot; // Glisse l'objet "Visual" de ton prefab ici

    [Header("Data")]
    [SerializeField] private ItemInstance _itemInstance;
    [SerializeField] private Collider _interactionZone; // Optional custom interaction zone
    
    public ItemInstance ItemInstance => _itemInstance;
    public Collider InteractionZone => _interactionZone;
    public UnityEngine.Rendering.SortingGroup SortingGroup { get; private set; }
    public bool IsBeingCarried { get; set; } = false;

    private void Awake()
    {
        SortingGroup = GetComponent<UnityEngine.Rendering.SortingGroup>();
    }

    public void Initialize(ItemInstance instance)
    {
        _itemInstance = instance;

        if (_itemInstance != null && _itemInstance.ItemSO != null)
        {
            // 1. Instanciation du visuel (le prefab de l'item)
            GameObject prefabVisual = _itemInstance.ItemSO.ItemPrefab;
            if (prefabVisual != null)
            {
                AttachVisualPrefab(prefabVisual);
            }

            // 2. APPLICATION DES COULEURS ET LIBRARY
            // On recupere le handler qui vient d'etre instancie dans le Visual
            WearableHandlerBase handler = GetComponentInChildren<WearableHandlerBase>();

            if (handler != null)
            {
                // On utilise le handler pour appliquer les donnees de l'instance
                handler.Initialize(_itemInstance.ItemSO.SpriteLibraryAsset);
                handler.SetLibraryCategory(_itemInstance.ItemSO.CategoryName);

                if (_itemInstance is EquipmentInstance eq)
                {
                    if (eq.HavePrimaryColor()) handler.SetPrimaryColor(eq.PrimaryColor);
                    if (eq.HaveSecondaryColor()) handler.SetSecondaryColor(eq.SecondaryColor);
                    // Si tu as une couleur principale (Main)
                    handler.SetMainColor(Color.white); // Ou eq.MainColor si tu l'as ajoutee
                }
            }
            else
            {
                // Fallback : Si ce n'est pas un equipement avec handler (ex: une pomme)
                _itemInstance.InitializeWorldPrefab(gameObject);
            }
        }
    }

    private void AttachVisualPrefab(GameObject prefab)
    {
        if (_visualRoot == null)
        {
            Debug.LogError($"[WorldItem] _visualRoot (l'objet Visual) n'est pas assigne sur {gameObject.name}");
            return;
        }

        // Nettoyage des anciens visuels s'il y en a
        foreach (Transform child in _visualRoot)
        {
            Destroy(child.gameObject);
        }

        // Instanciation du prefab d'entree a l'interieur du Visual
        GameObject go = Instantiate(prefab, _visualRoot);

        // Reset des transforms pour etre sur qu'il soit bien centre
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
    }

    /// <summary>
    /// Instancie le WorldItem prefab de l'ItemSO et l'initialise dans le monde.
    /// Utilisé quand on veut drop un item au sol (ex: deposit).
    /// </summary>
    public static void SpawnWorldItem(ItemSO itemSO, Vector3 position)
    {
        GameObject prefab = itemSO.WorldItemPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"<color=orange>[Gather]</color> Pas de WorldItemPrefab sur {itemSO.ItemName}, item non spawné.");
            return;
        }

        GameObject worldItemGo = Object.Instantiate(prefab, position, Quaternion.identity);
        worldItemGo.name = $"WorldItem_{itemSO.ItemName}";

        ItemInstance instance = itemSO.CreateInstance();

        if (worldItemGo.TryGetComponent(out WorldItem worldItem))
        {
            worldItem.Initialize(instance);
        }
        else
        {
            Debug.LogError($"<color=red>[Gather]</color> Le prefab de {itemSO.ItemName} n'a pas de composant WorldItem !");
            Object.Destroy(worldItemGo);
        }
    }

    /// <summary>
    /// Instancie le WorldItem prefab en utilisant une instance existante (pour préserver sa durabilité, couleurs, etc).
    /// </summary>
    public static void SpawnWorldItem(ItemInstance instance, Vector3 position)
    {
        if (instance == null || instance.ItemSO == null) return;

        GameObject prefab = instance.ItemSO.WorldItemPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"<color=orange>[Gather]</color> Pas de WorldItemPrefab sur {instance.ItemSO.ItemName}, item non spawné.");
            return;
        }

        GameObject worldItemGo = Object.Instantiate(prefab, position, Quaternion.identity);
        worldItemGo.name = $"WorldItem_{instance.ItemSO.ItemName}";

        if (worldItemGo.TryGetComponent(out WorldItem worldItem))
        {
            worldItem.Initialize(instance);
        }
        else
        {
            Debug.LogError($"<color=red>[Gather]</color> Le prefab de {instance.ItemSO.ItemName} n'a pas de composant WorldItem !");
            Object.Destroy(worldItemGo);
        }
    }
}
