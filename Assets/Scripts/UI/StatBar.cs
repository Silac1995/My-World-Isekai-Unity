using UnityEngine;
using UnityEngine.UI;

public class StatBar : MonoBehaviour, ICharacterUIElement
{
    [SerializeField] private Slider bar;
    public Slider Bar => bar;

    private Character character;
    public Character Character => character;

    private System.Func<float> getCurrentValue;
    private System.Func<float> getMaxValue;

    public void Initialize(Character character)
    {
        this.character = character;

        // Définit les fonctions selon le type de barre
        switch (gameObject.name.ToLower())
        {
            case string name when name.Contains("health"):
                getCurrentValue = () => character.Stats.Health.CurrentAmount;
                getMaxValue = () => character.Stats.Health.MaxValue;
                break;
            case string name when name.Contains("stamina"):
                getCurrentValue = () => character.Stats.Stamina.CurrentAmount;
                getMaxValue = () => character.Stats.Stamina.MaxValue;
                break;
            case string name when name.Contains("mana"):
                getCurrentValue = () => character.Stats.Mana.CurrentAmount;
                getMaxValue = () => character.Stats.Mana.MaxValue;
                break;
            case string name when name.Contains("initiative"):
                getCurrentValue = () => character.Stats.Initiative.CurrentAmount;
                getMaxValue = () => character.Stats.Initiative.MaxValue;
                break;
            default:
                Debug.LogWarning($"Type de barre non reconnu pour {gameObject.name}.", this);
                enabled = false;
                return;
        }

        UpdateBar(); // Mise à jour initiale
    }

    private void Update()
    {
        if (character == null || getCurrentValue == null || getMaxValue == null)
            return;

        UpdateBar();
    }

    private void UpdateBar()
    {
        float max = getMaxValue();
        float current = getCurrentValue();

        if (bar != null && max > 0)
        {
            bar.value = current / max;
        }
    }
}
