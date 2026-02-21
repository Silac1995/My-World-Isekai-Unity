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
        if (_character.CharacterCombat.IsInBattle || _character.CharacterActions.CurrentAction != null) return;
        if (GetCurrentBehaviour<WanderBehaviour>() == null) return;

        if (_character.CharacterRelation == null) return;
        var rel = _character.CharacterRelation.GetRelationshipWith(target);

        // Si on ne se connaît pas, on s'ignore
        if (rel == null) return;

        // --- 1. TEST D'AGRESSION (ENNEMIS) ---
        if (rel.RelationValue <= -10)
        {
            // 20% de chance d'attaquer directement
            if (Random.value < 0.2f)
            {
                Debug.Log($"<color=red>[Aggression]</color> {_character.CharacterName} repère son ennemi {target.CharacterName} et attaque !");
                PushBehaviour(new AttackTargetBehaviour(target));
                return; // On arrête là, l'attaque prime sur le social
            }
        }

        // --- 2. LOGIQUE SOCIALE ---
        float interactionChance = Mathf.Clamp(0.02f + (rel.RelationValue / 80f), 0.02f, 0.5f);
        
        // Si on est "Enemy" (<= -10) et qu'on n'a pas attaqué au-dessus, on ignore sauf si faim de social
        if (rel.RelationValue <= -10) interactionChance = 0f; 

        bool isSociallyStarved = false;
        if (_character.CharacterNeeds != null)
        {
            var socialNeed = _character.CharacterNeeds.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
            if (socialNeed != null && socialNeed.IsActive()) isSociallyStarved = true;
        }
        
        if (isSociallyStarved) interactionChance = Mathf.Max(interactionChance, 0.25f);

        if (Random.value < interactionChance)
        {
            // --- DÉCISION DE L'ACTION ---
            ICharacterInteractionAction actionToPerform = DetermineSocialAction(rel.RelationValue);
            string shoutText = (actionToPerform is InteractionTalk) ? "Hey! Wait!" : "YOU! YES YOU!";

            Debug.Log($"<color=cyan>[Social Detection]</color> {_character.CharacterName} engage {target.CharacterName} (Chance: {interactionChance:P0}, Score: {rel.RelationValue}, Action: {actionToPerform.GetType().Name})");
            
            if (_character.CharacterSpeech != null)
                _character.CharacterSpeech.Say(shoutText);

            PushBehaviour(new MoveToTargetBehaviour(this, target.gameObject, 7f, () =>
            {
                if (target == null || !target.IsAlive()) return;

                _character.CharacterInteraction.StartInteractionWith(target, () => 
                {
                    _character.CharacterInteraction.PerformInteraction(actionToPerform);
                    _character.CharacterInteraction.EndInteraction();
                });
            }));
        }
    }

    public ICharacterInteractionAction DetermineSocialAction(int relationValue)
    {
        float roll = Random.value;
        float talkChance = 0.5f;

        if (relationValue >= 40) // Friend+
        {
            talkChance = 0.9f;
        }
        else if (relationValue >= 0) // Neutral
        {
            // De 60% (à 0) à 90% (à 40)
            talkChance = 0.6f + (relationValue / 40f) * 0.3f;
        }
        else if (relationValue > -10) // Negative
        {
            talkChance = 0.4f;
        }
        else // Enemy
        {
            talkChance = 0.25f;
        }

        if (roll < talkChance)
            return new InteractionTalk();
        else
            return new InteractionInsult();
    }
}
