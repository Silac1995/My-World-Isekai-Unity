using UnityEngine;
using System.Linq;

public class NPCController : CharacterGameController
{
    [Header("Wander Settings")]
    [SerializeField] private float walkRadius = 50f;
    [SerializeField] private float minWaitTime = 2f;
    [SerializeField] private float maxWaitTime = 7f;

    public float WalkRadius { get => walkRadius; set => walkRadius = value; }
    public float MinWaitTime { get => minWaitTime; set => minWaitTime = value; }
    public float MaxWaitTime { get => maxWaitTime; set => maxWaitTime = value; }

    public override void Initialize()
    {
        base.Initialize();

        if (_characterMovement != null && _character != null)
        {
            var agent = _characterMovement.Agent; 
            if (agent != null)
            {
                agent.speed = _character.MovementSpeed;
                agent.acceleration = 50f;
                agent.angularSpeed = 0f;
            }
        }
        if (Agent != null) Agent.updateRotation = false;

        if (_character.CharacterAwareness != null)
        {
            _character.CharacterAwareness.OnCharacterDetected += HandleCharacterDetected;
        }

        ResetStackTo(new WanderBehaviour(this));
    }

    private void OnDestroy()
    {
        if (_character != null && _character.CharacterAwareness != null)
        {
            _character.CharacterAwareness.OnCharacterDetected -= HandleCharacterDetected;
        }
    }

    protected virtual void Update()
    {
        base.Update();
    }

    private void HandleCharacterDetected(Character target)
    {
        // 1. DISPONIBILITÉ DU NPC : On n'initie rien si on est déjà occupé (combat, interaction, KO)
        if (!_character.IsFree()) return;
        
        // On ne réagit qu'en mode Wander (balade)
        if (GetCurrentBehaviour<WanderBehaviour>() == null) return;

        if (_character.CharacterRelation == null) return;
        
        // --- 1. LOGIQUE D'INCOMPATIBILITÉ (PERSONNALITÉ) ---
        // On teste ça même si on ne se connaît pas encore (d'instinct)
        if (_character.CharacterProfile != null && target.CharacterProfile != null)
        {
            if (_character.CharacterProfile.GetCompatibilityWith(target.CharacterProfile) < 0)
            {
                // 20% de chance d'aller insulter spontanément
                if (Random.value < 0.2f)
                {
                    Debug.Log($"<color=orange>[Personality Clash]</color> {_character.CharacterName} déteste d'instinct {target.CharacterName} et va l'insulter !");
                    
                    if (_character.CharacterSpeech != null)
                        _character.CharacterSpeech.Say("You! I don't like your face!");

                    PushBehaviour(new MoveToTargetBehaviour(this, target.gameObject, 7f, () =>
                    {
                        if (target == null || !target.IsAlive()) return;
                        _character.CharacterInteraction.StartInteractionWith(target, new InteractionInsult());
                    }));
                    return;
                }
            }
        }

        var rel = _character.CharacterRelation.GetRelationshipWith(target);

        // Si on ne se connaît pas officiellement (et pas de clash de personnalité), on s'ignore
        if (rel == null) return;

        // --- 2. LOGIQUE D'AGRESSION (ENNEMIS) ---
        if (target.IsAlive() && rel.RelationValue <= -10)
        {
            if (Random.value < 0.2f)
            {
                Debug.Log($"<color=red>[Aggression]</color> {_character.CharacterName} repère son ennemi {target.CharacterName} et attaque !");
                PushBehaviour(new AttackTargetBehaviour(target));
                return;
            }
        }

        // --- 3. LOGIQUE SOCIALE ---
        if (!target.IsFree()) return;

        // UTILISATION DE LA LOGIQUE CENTRALISÉE
        float interactionChance = rel.GetInteractionChance();

        bool isSociallyStarved = false;
        if (_character.CharacterNeeds != null)
        {
            var socialNeed = _character.CharacterNeeds.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
            if (socialNeed != null && socialNeed.IsActive()) isSociallyStarved = true;
        }
        
        if (isSociallyStarved) interactionChance = Mathf.Max(interactionChance, 0.25f);

        if (Random.value < interactionChance)
        {
            Debug.Log($"<color=cyan>[Social Detection]</color> {_character.CharacterName} engage {target.CharacterName} (Chance: {interactionChance:P0}$)");
            
            if (_character.CharacterSpeech != null)
                _character.CharacterSpeech.Say("Hey! You!");

            PushBehaviour(new MoveToTargetBehaviour(this, target.gameObject, 7f, () =>
            {
                if (target == null || !target.IsAlive()) return;
                _character.CharacterInteraction.StartInteractionWith(target);
            }));
        }
    }

    /// <summary>
    /// Détermine si le NPC accepte de répondre à une interaction.
    /// </summary>
    public bool ShouldRespondTo(Character source)
    {
        if (_character.CharacterRelation == null) return false;
        var rel = _character.CharacterRelation.GetRelationshipWith(source);
        if (rel == null) return false;

        return Random.value < rel.GetResponseChance();
    }

    /// <summary>
    /// Choisi une action sociale (Talk ou Insult) basée sur la relation.
    /// </summary>
    public ICharacterInteractionAction GetRandomSocialAction(Character target)
    {
        if (_character.CharacterRelation == null) return new InteractionTalk();
        var rel = _character.CharacterRelation.GetRelationshipWith(target);
        if (rel == null) return new InteractionTalk();

        if (Random.value < rel.GetFavorableToneChance())
            return new InteractionTalk();
        else
            return new InteractionInsult();
    }
}
