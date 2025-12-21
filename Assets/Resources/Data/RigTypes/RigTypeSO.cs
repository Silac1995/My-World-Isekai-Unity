
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

[CreateAssetMenu(fileName = "BaseSpriteBone", menuName = "Scriptable Objects/BaseSpriteBone")]
public class RigTypeSO : ScriptableObject
{
    //[SerializeField] public List<GameObject> prefabs = new List<GameObject>();
    [SerializeField] public BaseSpritesLibrarySO baseSpritesLibrary;
}
