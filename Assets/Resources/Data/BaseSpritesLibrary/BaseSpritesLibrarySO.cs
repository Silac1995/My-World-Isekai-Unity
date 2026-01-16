using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

[CreateAssetMenu(fileName = "BaseSpriteLibraries", menuName = "Scriptable Objects/Base Sprite Libraries")]
public class BaseSpritesLibrarySO : ScriptableObject
{
    [Header("Animations")]
    [SerializeField] private RuntimeAnimatorController _defaultAnimatorController;
    public RuntimeAnimatorController DefaultAnimatorController => _defaultAnimatorController;
    // Body Parts
    [SerializeField] private SpriteLibraryAsset body_HairLibrary;
    [SerializeField] private SpriteLibraryAsset body_EyebrowsLibrary;
    [SerializeField] private SpriteLibraryAsset body_EyesLibrary;
    [SerializeField] private SpriteLibraryAsset body_FaceLibrary;
    [SerializeField] private SpriteLibraryAsset body_NoseLibrary;
    [SerializeField] private SpriteLibraryAsset body_MouthLibrary;
    [SerializeField] private SpriteLibraryAsset body_EarsLibrary;
    [SerializeField] private SpriteLibraryAsset body_FeetLibrary;
    [SerializeField] private SpriteLibraryAsset body_HandsLibrary;
    [SerializeField] private SpriteLibraryAsset body_ChestLibrary; //can be either male chest or bewbs
    [SerializeField] private SpriteLibraryAsset body_NipplesLibrary; //can be either male chest or bewbs

    // Underwear
    [SerializeField] private SpriteLibraryAsset underwear_UpperLibrary;
    [SerializeField] private SpriteLibraryAsset underwear_LowerLibrary;

    // Clothing
    [SerializeField] private SpriteLibraryAsset clothing_HatLibrary;
    [SerializeField] private SpriteLibraryAsset clothing_UpperLibrary;
    [SerializeField] private SpriteLibraryAsset clothing_LowerLibrary;
    [SerializeField] private SpriteLibraryAsset clothing_GlovesLibrary;
    [SerializeField] private SpriteLibraryAsset clothing_ShoesLibrary;

    // Armor
    [SerializeField] private SpriteLibraryAsset armor_HeadLibrary;
    [SerializeField] private SpriteLibraryAsset armor_UpperLibrary;
    [SerializeField] private SpriteLibraryAsset armor_LowerLibrary;
    [SerializeField] private SpriteLibraryAsset armor_GlovesLibrary;
    [SerializeField] private SpriteLibraryAsset armor_BootsLibrary;

    // Weapons
    [SerializeField] private SpriteLibraryAsset weapon_swordsLibrary;

    // Public Getters
    public SpriteLibraryAsset Body_HairLibrary => body_HairLibrary;
    public SpriteLibraryAsset Body_EyebrowsLibrary => body_EyebrowsLibrary;
    public SpriteLibraryAsset Body_EyesLibrary => body_EyesLibrary;
    public SpriteLibraryAsset Body_FaceLibrary => body_FaceLibrary;
    public SpriteLibraryAsset Body_NoseLibrary => body_NoseLibrary;
    public SpriteLibraryAsset Body_MouthLibrary => body_MouthLibrary;
    public SpriteLibraryAsset Body_EarsLibrary => body_EarsLibrary;
    public SpriteLibraryAsset Body_FeetLibrary => body_FeetLibrary;
    public SpriteLibraryAsset Body_HandsLibrary => body_HandsLibrary;
    public SpriteLibraryAsset Body_ChestLibrary => body_ChestLibrary;
    public SpriteLibraryAsset Body_NipplesLibrary => body_NipplesLibrary;

    public SpriteLibraryAsset Underwear_UpperLibrary => underwear_UpperLibrary;
    public SpriteLibraryAsset Underwear_LowerLibrary => underwear_LowerLibrary;

    public SpriteLibraryAsset Clothing_HatLibrary => clothing_HatLibrary;
    public SpriteLibraryAsset Clothing_UpperLibrary => clothing_UpperLibrary;
    public SpriteLibraryAsset Clothing_LowerLibrary => clothing_LowerLibrary;
    public SpriteLibraryAsset Clothing_GlovesLibrary => clothing_GlovesLibrary;
    public SpriteLibraryAsset Clothing_ShoesLibrary => clothing_ShoesLibrary;

    public SpriteLibraryAsset Armor_HeadLibrary => armor_HeadLibrary;
    public SpriteLibraryAsset Armor_UpperLibrary => armor_UpperLibrary;
    public SpriteLibraryAsset Armor_LowerLibrary => armor_LowerLibrary;
    public SpriteLibraryAsset Armor_GlovesLibrary => armor_GlovesLibrary;
    public SpriteLibraryAsset Armor_BootsLibrary => armor_BootsLibrary;

    public SpriteLibraryAsset Weapon_SwordsLibrary => weapon_swordsLibrary;

}
