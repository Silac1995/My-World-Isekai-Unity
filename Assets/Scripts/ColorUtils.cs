using UnityEngine;

public static class ColorUtils
{
    /// <summary>
    /// Convertit un code hex en Color Unity.
    /// </summary>
    /// <param name="hex">Code hex sans # ou avec # (ex: "FFE2CD" ou "#FFE2CD")</param>
    /// <returns>La couleur Unity correspondante. Retourne Color.white si le hex est invalide.</returns>
    public static Color HexToColor(string hex)
    {
        if (!hex.StartsWith("#"))
            hex = "#" + hex;

        if (ColorUtility.TryParseHtmlString(hex, out Color color))
        {
            return color;
        }
        else
        {
            Debug.LogError($"[ColorUtils] Impossible de convertir le code hex '{hex}' en Color. Retourne Color.white.");
            return Color.white;
        }
    }
}
