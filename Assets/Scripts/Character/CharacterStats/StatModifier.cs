using UnityEngine;

/// <summary>
/// Représente une modification appliquée à une statistique.
/// Permet de garder une trace de l'origine (Source) du bonus/malus pour pouvoir le retirer spécifiquement plus tard.
/// </summary>
public class StatModifier
{
    public float Value { get; private set; }
    public object Source { get; private set; }

    public StatModifier(float value, object source)
    {
        Value = value;
        Source = source;
    }
}
