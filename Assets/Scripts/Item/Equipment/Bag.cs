using UnityEngine;
using System.Collections.Generic;
using UnityEngine.U2D.Animation;
using System.Reflection;

public class Bag : MonoBehaviour
{
    [System.Serializable]
    public class BagAnchor
    {
        public string anchorKey;
        public Transform transform;
    }

    [Header("Visual References")]
    [SerializeField] private GameObject _visualContainer;
    [SerializeField] private List<BagAnchor> _anchors = new List<BagAnchor>();

    private Transform _inventoryContainer;

    // Déclaration des champs pour la Reflection (pour éviter les erreurs de contexte)
    private FieldInfo _rootBoneField;
    private FieldInfo _boneTransformsField;
    private FieldInfo _bindPosesField;

    private void Awake()
    {
        // Initialisation de la Reflection
        _rootBoneField = typeof(SpriteSkin).GetField("m_RootBone", BindingFlags.NonPublic | BindingFlags.Instance);
        _boneTransformsField = typeof(SpriteSkin).GetField("m_BoneTransforms", BindingFlags.NonPublic | BindingFlags.Instance);
        _bindPosesField = typeof(SpriteSkin).GetField("m_BindPoses", BindingFlags.NonPublic | BindingFlags.Instance);

        CreateInventoryContainer();
        RefreshAnchors();
    }

    private void CreateInventoryContainer()
    {
        _inventoryContainer = transform.Find("Inventory");
        if (_inventoryContainer == null)
        {
            GameObject go = new GameObject("Inventory");
            _inventoryContainer = go.transform;
            _inventoryContainer.SetParent(this.transform);
            _inventoryContainer.localPosition = Vector3.zero;
            _inventoryContainer.localRotation = Quaternion.identity;
        }
    }

    [ContextMenu("Refresh Anchors")]
    public void RefreshAnchors()
    {
        _anchors.Clear();

        // On scanne TOUT l'objet du sac (y compris les enfants de Line, etc.)
        // On utilise true pour inclure même ce qui est désactivé
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);

        foreach (Transform child in allChildren)
        {
            // On cherche spécifiquement ton bone
            if (child.name.Contains("bone_weaponAnchor"))
            {
                _anchors.Add(new BagAnchor { anchorKey = "Weapon", transform = child });
                Debug.Log($"<color=cyan>[Bag]</color> Anchor trouvé dans la hiérarchie : {child.name}");
            }
        }

        if (_anchors.Count == 0)
        {
            Debug.LogWarning($"<color=red>[Bag]</color> Aucun 'bone_weaponAnchor' trouvé dans {gameObject.name}. Vérifie la hiérarchie !");
        }
    }

    public void InitializeWeaponBones(GameObject instantiatedWeapon, Transform bagAnchor)
    {
        if (instantiatedWeapon == null || bagAnchor == null) return;

        // A. POSITIONNEMENT PHYSIQUE
        // On le place sur le bone AVANT d'activer le skinning
        instantiatedWeapon.transform.SetParent(bagAnchor, false);
        instantiatedWeapon.transform.localPosition = Vector3.zero;
        instantiatedWeapon.transform.localRotation = Quaternion.identity;
        instantiatedWeapon.transform.localScale = Vector3.one;

        SpriteSkin[] skins = instantiatedWeapon.GetComponentsInChildren<SpriteSkin>(true);

        foreach (var skin in skins)
        {
            // On désactive pour "ré-écrire" le cerveau du composant
            skin.enabled = false;

            // B. REFLECTION : Assignation du Bone du Sac
            _rootBoneField?.SetValue(skin, bagAnchor);
            _boneTransformsField?.SetValue(skin, new Transform[] { bagAnchor });

            // C. LE FIX DE POSITION (Bind Pose)
            if (_bindPosesField != null)
            {
                Matrix4x4[] bindPoses = new Matrix4x4[1];
                // Identity force le mesh de l'épée à s'aligner parfaitement sur l'axe du bone
                bindPoses[0] = Matrix4x4.identity;
                _bindPosesField.SetValue(skin, bindPoses);
            }

            // D. RESET DES ENFANTS VISUELS (Line, Color_Main...)
            // Le SpriteSkin déplace souvent le GameObject sur lequel il est. On le remet à zéro.
            skin.transform.localPosition = Vector3.zero;
            skin.transform.localRotation = Quaternion.identity;

            skin.enabled = true;
        }

        Debug.Log($"<color=cyan>[Bag]</color> Weapon skinning linked to {bagAnchor.name}");
    }
    public List<Transform> GetAllWeaponAnchors()
    {
        List<Transform> weaponTransforms = new List<Transform>();
        foreach (var anchor in _anchors)
        {
            if (anchor.anchorKey == "Weapon")
                weaponTransforms.Add(anchor.transform);
        }
        return weaponTransforms;
    }
}