using UnityEngine;

public class GrassManager : MonoBehaviour
{
    [Range(0f, 50f)]
    [SerializeField] private float _windStrength = 1.0f;

    // Le nom doit être exactement celui dans le Reference du Blackboard du Shader
    private static readonly int _windStrengthID = Shader.PropertyToID("_WindStrength");

    void Update()
    {
        // On envoie la valeur au shader globalement
        Shader.SetGlobalFloat(_windStrengthID, _windStrength);
    }
}