using System.Collections.Generic;
using UnityEngine;

public class SpellInstance
{
    private Spell spellAsset;
    private Character characterOwner;
    private float remainingCooldown;

    // Liste des instances d'effets de statut du sort
    private List<CharacterStatusEffectInstance> characterStatusEffectsInstance;

    private GameObject visualPrefab;

    public SpellInstance(Spell spellAsset, Character owner)
    {
        this.spellAsset = spellAsset;
        this.characterOwner = owner;
        this.remainingCooldown = 0f;
        this.visualPrefab = spellAsset.VisualPrefab;

        // Initialisation des instances d'effets de statut à partir des effets du spellAsset
        characterStatusEffectsInstance = new List<CharacterStatusEffectInstance>();

        if (spellAsset.CharacterStatusEffects != null)
        {
            foreach (var effectAsset in spellAsset.CharacterStatusEffects)
            {
                var instance = new CharacterStatusEffectInstance(effectAsset, characterOwner, characterOwner);
                characterStatusEffectsInstance.Add(instance);
            }
        }
    }

    public Spell SpellAsset => spellAsset;
    public float RemainingCooldown => remainingCooldown;

    public bool CanCast()
    {
        if (spellAsset == null || characterOwner?.Stats?.Mana == null)
            return false;

        return remainingCooldown <= 0f &&
               characterOwner.Stats.Mana.CurrentValue >= spellAsset.Cost;
    }

    public float Cost => spellAsset.Cost;

    public void Cast()
    {
        if (!CanCast())
            return;

        characterOwner.Stats.Mana.DecreaseCurrentAmount(Cost);

        // Déclencher le cooldown
        remainingCooldown = ComputeCooldown();
    }

    public void Update(float deltaTime)
    {
        if (remainingCooldown > 0f)
            remainingCooldown -= deltaTime;
    }

    private float ComputeCooldown()
    {
        // Plus tard : modifié par les stats du lanceur, etc.
        return 5f;
    }
}
