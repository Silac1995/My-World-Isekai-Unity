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
        var rel = _character.CharacterRelation.GetRelationshipWith(target);

        // Si on ne se connaît pas, on s'ignore
        if (rel == null) return;

        // --- 2. LOGIQUE D'AGRESSION (ENNEMIS) ---
        // On peut attaquer même si la cible est déjà en combat (principe de mêlée)
        if (rel.RelationValue <= -10 && target.IsAlive())
        {
            // 20% de chance d'attaquer directement
            if (Random.value < 0.2f)
            {
                Debug.Log($"<color=red>[Aggression]</color> {_character.CharacterName} repère son ennemi {target.CharacterName} et attaque !");
                PushBehaviour(new AttackTargetBehaviour(target));
                return;
            }
        }

        // --- 3. LOGIQUE SOCIALE ---
        // On n'initie le social QUE si la cible est "Free" (pas en combat, pas déjà en train de parler, pas KO)
        if (!target.IsFree()) return;

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
            Debug.Log($"<color=cyan>[Social Detection]</color> {_character.CharacterName} engage {target.CharacterName} (Chance: {interactionChance:P0}, Score: {rel.RelationValue})");
            
            if (_character.CharacterSpeech != null)
                _character.CharacterSpeech.Say("Hey! You!");

            PushBehaviour(new MoveToTargetBehaviour(this, target.gameObject, 7f, () =>
            {
                if (target == null || !target.IsAlive()) return;

                // On lance l'interaction qui va maintenant gérer la séquence de dialogue
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
        int score = rel?.RelationValue ?? 0;

        // Chance de base : 50%
        // +1% par point de relation (max 100% à 50+ relation)
        // -1% par point de relation négatif (min 0% à -50- relation)
        float responseChance = Mathf.Clamp(0.5f + (score / 100f), 0f, 1f);
        
        return Random.value < responseChance;
    }

    /// <summary>
    /// Choisi une action sociale (Talk ou Insult) basée sur la relation.
    /// </summary>
    public ICharacterInteractionAction GetRandomSocialAction(Character target)
    {
        if (_character.CharacterRelation == null) return new InteractionTalk();
        var rel = _character.CharacterRelation.GetRelationshipWith(target);
        int relationValue = rel?.RelationValue ?? 0;

        float roll = Random.value;
        float talkChance = 0.5f;

        if (relationValue >= 40) talkChance = 0.95f;      // Ami : 95% Talk
        else if (relationValue >= 10) talkChance = 0.8f;  // Sympa : 80% Talk
        else if (relationValue >= -10) talkChance = 0.5f; // Neutre : 50% Talk
        else talkChance = 0.15f;                           // Ennemi : 15% Talk

        if (roll < talkChance)
            return new InteractionTalk();
        else
            return new InteractionInsult();
    }
}
