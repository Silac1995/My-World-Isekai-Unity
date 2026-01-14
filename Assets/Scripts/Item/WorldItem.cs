using UnityEngine;

public class WorldItem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _visualRoot; // Glisse l'objet "Visual" de ton prefab ici

    [Header("Data")]
    [SerializeField] private ItemInstance _itemInstance;
    public ItemInstance ItemInstance => _itemInstance;

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
            // On récupère le handler qui vient d'être instancié dans le Visual
            WearableHandlerBase handler = GetComponentInChildren<WearableHandlerBase>();

            if (handler != null)
            {
                // On utilise le handler pour appliquer les données de l'instance
                handler.Initialize(_itemInstance.ItemSO.SpriteLibraryAsset);
                handler.SetLibraryCategory(_itemInstance.ItemSO.CategoryName);

                if (_itemInstance is EquipmentInstance eq)
                {
                    if (eq.HavePrimaryColor()) handler.SetPrimaryColor(eq.PrimaryColor);
                    if (eq.HaveSecondaryColor()) handler.SetSecondaryColor(eq.SecondaryColor);
                    // Si tu as une couleur principale (Main)
                    handler.SetMainColor(Color.white); // Ou eq.MainColor si tu l'as ajoutée
                }
            }
            else
            {
                // Fallback : Si ce n'est pas un équipement avec handler (ex: une pomme)
                _itemInstance.InitializeWorldPrefab(gameObject);
            }
        }
    }

    private void AttachVisualPrefab(GameObject prefab)
    {
        if (_visualRoot == null)
        {
            Debug.LogError($"[WorldItem] _visualRoot (l'objet Visual) n'est pas assigné sur {gameObject.name}");
            return;
        }

        // Nettoyage des anciens visuels s'il y en a
        foreach (Transform child in _visualRoot)
        {
            Destroy(child.gameObject);
        }

        // Instanciation du prefab d'entrée à l'intérieur du Visual
        GameObject go = Instantiate(prefab, _visualRoot);

        // Reset des transforms pour être sûr qu'il soit bien centré
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
    }
}