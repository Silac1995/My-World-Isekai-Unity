using UnityEngine;

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

        // --- Abonnement Social ---
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
        // On n'interagit pas si on est déjà occupé ou en combat
        if (_character.CharacterCombat.IsInBattle || _character.CharacterActions.CurrentAction != null) return;
        if (GetCurrentBehaviour<WanderBehaviour>() == null) return;

        // Vérification de la relation
        if (_character.CharacterRelation == null) return;
        var rel = _character.CharacterRelation.GetRelationshipWith(target);

        // Si on ne se connaît pas, on s'ignore (pas d'agression/interaction spontanée)
        if (rel == null || !rel.HasMet) return;

        // Calcul de la probabilité sociale basée sur le score (max 50% pour 100 points)
        float interactionChance = Mathf.Clamp(rel.RelationValue / 200f, 0f, 0.5f);

        if (Random.value < interactionChance)
        {
            Debug.Log($"<color=cyan>[Social]</color> {_character.CharacterName} reconnaît son ami {target.CharacterName} et va lui parler !");
            
            // On s'approche
            PushBehaviour(new MoveToTargetBehaviour(this, target.gameObject, 7f, () =>
            {
                if (target == null || !target.IsAlive()) return;

                // Lancement de l'interaction Talk
                _character.CharacterInteraction.StartInteractionWith(target, () => 
                {
                    _character.CharacterInteraction.PerformInteraction(new InteractionTalk());
                    _character.CharacterInteraction.EndInteraction();
                });
            }));
        }
    }
}
