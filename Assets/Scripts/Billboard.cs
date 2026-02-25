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

        // Rotation uniquement sur l'axe Y (garde le sprite vertical)
        // Evite que le sprite penetre dans les murs 3D derriere le personnage
        Vector3 camForward = mainCam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude > 0.001f)
        {
            transform.forward = camForward;
        }
    }
}