using UnityEngine;
using System.Linq;
using MWI.AI;

public class NPCController : CharacterGameController
{
    [Header("Wander Settings")]
    [SerializeField] private float walkRadius = 50f;
    [SerializeField] private float minWaitTime = 2f;
    [SerializeField] private float maxWaitTime = 7f;

    public float WalkRadius { get => walkRadius; set => walkRadius = value; }
    public float MinWaitTime { get => minWaitTime; set => minWaitTime = value; }
    public float MaxWaitTime { get => maxWaitTime; set => maxWaitTime = value; }

    // Référence au BT (null si pas de BT)
    private NPCBehaviourTree _behaviourTree;
    public NPCBehaviourTree BehaviourTree => _behaviourTree;
    public bool HasBehaviourTree => _behaviourTree != null;

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

        // Chercher le BT sur le même GameObject
        _behaviourTree = GetComponentInParent<NPCBehaviourTree>();
        if (_behaviourTree == null)
            _behaviourTree = GetComponent<NPCBehaviourTree>();

        if (HasBehaviourTree)
        {
            // Le BT gère tout : pas de HandleCharacterDetected ni de WanderBehaviour initial
            Debug.Log($"<color=lime>[BT]</color> {_character.CharacterName} utilise le Behaviour Tree.");
        }
        else
        {
            // Mode legacy : on garde l'ancien système
            if (_character.CharacterAwareness != null)
            {
                _character.CharacterAwareness.OnCharacterDetected += HandleCharacterDetected;
            }
            ResetStackTo(new WanderBehaviour(this));
        }
    }

    private void OnDestroy()
    {
        if (!HasBehaviourTree && _character != null && _character.CharacterAwareness != null)
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
        
        // --- 1. LOGIQUE D'ENTRAIDE (AMIS OU GROUPE EN COMBAT) ---
        // Priorité : Si c'est un ami ou un membre du groupe en combat, on l'aide peu importe le reste
        if (target.CharacterCombat.IsInBattle && target.IsAlive())
        {
            bool isFriend = _character.CharacterRelation != null && _character.CharacterRelation.IsFriend(target);
            bool sameParty = _character.CurrentParty != null && _character.CurrentParty == target.CurrentParty;

            // Loyalty trait makes characters more likely to help acquaintances (not just friends)
            bool isAcquaintance = _character.CharacterRelation != null && _character.CharacterRelation.GetRelationshipWith(target)?.RelationValue >= 0;
            bool isLoyalHelp = _character.CharacterTraits != null && (_character.CharacterTraits.GetLoyalty() > 0.5f) && isAcquaintance;

            if (isFriend || sameParty || isLoyalHelp)
            {
                string helpMsg = sameParty ? "Protect the group!" : "Hang on, my friend! I'm coming!";
                Debug.Log($"<color=green>[Assistance]</color> {_character.CharacterName} voit son {(sameParty ? "coéquipier" : "ami")} {target.CharacterName} en combat et fonce l'aider !");
                
                if (_character.CharacterSpeech != null)
                    _character.CharacterSpeech.Say(helpMsg);

                _character.CharacterCombat.JoinBattleAsAlly(target);
                return;
            }
        }

        var rel = _character.CharacterRelation.GetRelationshipWith(target);

        // --- 2a. AGRESSION SPONTANÉE (trait-based, contre n'importe qui) ---
        // Un personnage très agressif peut attaquer même un inconnu
        if (target.IsAlive() && _character.CharacterTraits != null)
        {
            float aggressivity = _character.CharacterTraits.GetAggressivity();
            // Squared curve: 0.1 → 0.3%, 0.5 → 7.5%, 1.0 → 30% max chance per detection
            float aggroChance = aggressivity * aggressivity * 0.3f;
            if (aggroChance > 0f && Random.value < aggroChance)
            {
                Debug.Log($"<color=red>[Aggression]</color> {_character.CharacterName} attaque {target.CharacterName} de façon spontanée (Aggressivity: {aggressivity:P0})!");
                if (_character.CharacterSpeech != null)
                    _character.CharacterSpeech.Say("You're in my way!");
                PushBehaviour(new AttackTargetBehaviour(target));
                return;
            }
        }

        // --- 2b. LOGIQUE D'AGRESSION (ENNEMIS CONNUS) ---
        // On attaque nos ennemis même s'ils sont en train de papoter !
        if (rel != null && target.IsAlive() && rel.RelationValue <= -10)
        {
            float aggroChance = 0.2f;
            if (_character.CharacterTraits != null)
            {
                aggroChance += _character.CharacterTraits.GetAggressivity() * 0.5f;
            }

            if (Random.value < aggroChance)
            {
                Debug.Log($"<color=red>[Aggression]</color> {_character.CharacterName} repère son ennemi {target.CharacterName} et attaque ! (Chance: {aggroChance:P0})");
                PushBehaviour(new AttackTargetBehaviour(target));
                return;
            }
        }

        // --- 3. SÉCURITÉ SOCIALE : Si la cible n'est pas libre (combat, busy), on s'arrête ici pour le social ---
        if (!target.IsFree()) return;
        
        // --- 4. LOGIQUE D'INCOMPATIBILITÉ (PERSONNALITÉ) ---
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

        // --- 5. LOGIQUE SOCIALE ---
        float sociability = _character.CharacterTraits != null ? _character.CharacterTraits.GetSociability() : 0.5f;

        // Si on ne se connaît pas, on peut quand même aller parler à l'inconnu selon la sociabilité
        if (rel == null)
        {
            // Only approach a stranger if sociable enough (e.g. sociability 0.5 = 15% base chance)
            float strangerChance = sociability * 0.3f;
            if (Random.value < strangerChance)
            {
                Debug.Log($"<color=cyan>[Social - Stranger]</color> {_character.CharacterName} s'approche de l'inconnu {target.CharacterName} (Sociabilité: {sociability:P0})");
                PushBehaviour(new MoveToInteractionBehaviour(this, target));
            }
            return;
        }

        // Si on se connait: 70% de chance de préférer un ami proche à un inconnu
        bool isFriendKnown = _character.CharacterRelation.IsFriend(target);
        bool preferFriend = isFriendKnown && (Random.value < 0.7f);

        float interactionChance = rel.GetInteractionChance();
        
        // Application de la sociabilité
        interactionChance += (sociability - 0.5f);
        
        // Boost si besoin social non satisfait
        bool isSociallyStarved = false;
        if (_character.CharacterNeeds != null)
        {
            var socialNeed = _character.CharacterNeeds.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
            if (socialNeed != null && socialNeed.IsActive()) isSociallyStarved = true;
        }
        
        if (isSociallyStarved) interactionChance = Mathf.Max(interactionChance, 0.25f);

        // 70% bias toward friends: when detecting a non-friend acquaintance or stranger,
        // skip 70% of the time (preferring to approach friends who may be nearby instead)
        if (!isFriendKnown && Random.value < 0.7f)
        {
            return;
        }
        
        // Clamp the final chance
        interactionChance = Mathf.Clamp01(interactionChance);

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
    /// Choisi une action sociale (Talk, Invite, ou Insult) basée sur la relation.
    /// </summary>
    public ICharacterInteractionAction GetRandomSocialAction(Character target)
    {
        // First check if an invite is possible and roll for it (e.g. 50% chance if eligible)
        var inviteAction = new InteractionInviteCommunity();
        if (inviteAction.CanExecute(_character, target))
        {
            if (Random.value > 0.5f)
            {
                return inviteAction;
            }
        }

        // --- NEW: Check if mentorship is possible ---
        var mentorAction = new InteractionMentorship(null);
        if (mentorAction.CanExecute(_character, target))
        {
            // 40% chance to naturally ask for a lesson if we meet someone who can teach us
            if (Random.value < 0.4f)
            {
                return mentorAction;
            }
        }

        if (_character.CharacterRelation == null) return new InteractionTalk();
        var rel = _character.CharacterRelation.GetRelationshipWith(target);
        if (rel == null) return new InteractionTalk();

        // Base chance from relationship quality
        float talkChance = rel.GetFavorableToneChance();

        // Sociability boosts the chance of being friendly (0.0 sociability = -0.3 penalty, 1.0 = +0.3 bonus)
        if (_character.CharacterTraits != null)
        {
            float sociabilityBonus = (_character.CharacterTraits.GetSociability() - 0.5f) * 0.6f;
            talkChance += sociabilityBonus;
        }
        talkChance = Mathf.Clamp01(talkChance);

        if (Random.value < talkChance)
            return new InteractionTalk();
        else
            return new InteractionInsult();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("BattleZone"))
        {
            if (other.TryGetComponent<BattleManager>(out var manager))
            {
                HandleBattleZoneEntry(manager);
            }
        }
    }

    private void HandleBattleZoneEntry(BattleManager manager)
    {
        // 1. Si on est déjà en combat, on est concerné
        if (_character.CharacterCombat.IsInBattle) return;

        // 2. Chercher un ami dans cette bataille
        foreach (var team in manager.BattleTeams)
        {
            foreach (var participant in team.CharacterList)
            {
                if (_character.CharacterRelation != null && _character.CharacterRelation.IsFriend(participant))
                {
                    Debug.Log($"<color=green>[Battle Sensor]</color> {_character.CharacterName} voit son ami {participant.CharacterName} dans la bataille et le rejoint !");
                    _character.CharacterCombat.JoinBattleAsAlly(participant);
                    return;
                }
            }
        }

        // 3. Sinon, on n'est pas concerné -> On dégage
        if (!HasBehaviour<MoveOutOfBattleZoneBehaviour>())
        {
            Debug.Log($"<color=white>[Battle Sensor]</color> {_character.CharacterName} n'a rien à faire ici, il s'éloigne.");
            PushBehaviour(new MoveOutOfBattleZoneBehaviour(manager));
        }
    }
}
