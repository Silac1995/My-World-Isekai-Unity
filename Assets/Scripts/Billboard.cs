using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera mainCam;

    private void Start()
    {
        mainCam = Camera.main;
    }


    private void LateUpdate()
    {
        if (mainCam == null) return;

        // Alignement complet face à la caméra
        transform.forward = mainCam.transform.forward;
    }
}
