using UnityEngine;

public class WearableBagHandler : WearableHandlerBase
{
    protected override GameObject[] GetAllParts()
    {
        // On retourne l'objet lui-même. 
        // Le HandlerBase fera un .transform.Find("Color_Primary") sur cet objet.
        return new GameObject[] { this.gameObject };
    }
}