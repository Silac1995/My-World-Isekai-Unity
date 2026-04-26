using UnityEngine;
using System.Linq;
using System.Collections.Generic;
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

    [Header("AI Controllers")]
    [SerializeField] private NPCBehaviourTree _behaviourTree;

    public NPCBehaviourTree BehaviourTree => _behaviourTree;
    public CharacterGoapController GoapController => _character != null ? _character.CharacterGoap : null;
    public bool HasBehaviourTree => _behaviourTree != null;

    // --- LEGACY AI STACK (To be removed in Phase 4) ---
    private Stack<IAIBehaviour> _behavioursStack = new Stack<IAIBehaviour>();
    public IAIBehaviour CurrentBehaviour => _behavioursStack.Count > 0 ? _behavioursStack.Peek() : null;

    public override void Initialize()
    {
        base.Initialize();
        if (Agent != null) Agent.updateRotation = false;

        // Look for the BT on the same GameObject if not assigned in the inspector
        if (_behaviourTree == null)
        {
            _behaviourTree = GetComponentInParent<NPCBehaviourTree>();
            if (_behaviourTree == null)
                _behaviourTree = GetComponent<NPCBehaviourTree>();
        }

        if (HasBehaviourTree)
        {
            // The BT handles everything: no HandleCharacterDetected nor initial WanderBehaviour
            Debug.Log($"<color=lime>[BT]</color> {_character.CharacterName} uses the Behaviour Tree.");
        }
        else
        {
            // Legacy mode: keep the old system
            if (_character.CharacterAwareness != null && IsServer)
            {
                _character.CharacterAwareness.OnCharacterDetected += HandleCharacterDetected;
            }
            if (IsServer) ResetStackTo(new WanderBehaviour(this));
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

    // --- LEGACY STACK METHODS (To be removed in Phase 4) ---

    public void SetBehaviour(IAIBehaviour behaviour) => ResetStackTo(behaviour);

    public void PushBehaviour(IAIBehaviour newBehaviour)
    {
        SafeResume();
        _behavioursStack.Push(newBehaviour);
        newBehaviour.Enter(_character);
    }

    public void PopBehaviour()
    {
        if (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(_character);
        }

        SafeResume();

        if (_behavioursStack.Count == 0 && _character.TryGetComponent<NPCController>(out var npc))
        {
            if (!npc.HasBehaviourTree)
            {
                ResetStackTo(new WanderBehaviour(npc));
            }
        }
    }

    public void ClearBehaviours()
    {
        while (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(_character);
        }

        if (_characterMovement != null)
        {
            _characterMovement.Stop();
            if (Agent != null && Agent.isOnNavMesh)
            {
                Agent.ResetPath();
            }
        }
    }

    public void ResetStackTo(IAIBehaviour baseBehaviour)
    {
        ClearBehaviours();
        SafeResume();
        _behavioursStack.Push(baseBehaviour);
        baseBehaviour.Enter(_character);
    }

    public bool HasBehaviour<T>() where T : IAIBehaviour => _behavioursStack.Any(b => b is T);

    public T GetCurrentBehaviour<T>() where T : class, IAIBehaviour => CurrentBehaviour as T;

    public List<string> GetBehaviourStackNames() => _behavioursStack.Select(b => b.GetType().Name).ToList();

    private void HandleCharacterDetected(Character target)
    {
        // 1. NPC AVAILABILITY: don't start anything if we're already busy (combat, interaction, KO)
        if (!_character.IsFree()) return;

        // We only react in Wander mode or while on a work break
        bool isWandering = GetCurrentBehaviour<WanderBehaviour>() != null || (HasBehaviourTree && _character.CharacterSchedule?.CurrentActivity == ScheduleActivity.Wander);
        bool isWorking = _character.CharacterSchedule?.CurrentActivity == ScheduleActivity.Work;
        bool isOnBreak = isWorking && _character.CharacterJob?.CurrentJob?.CurrentGoalName == "Idle";

        if (!isWandering && !isOnBreak) return;

        bool isTargetOnBreak = false;
        if (target.Controller is NPCController targetNPC)
        {
            bool isTargetWorking = target.CharacterSchedule?.CurrentActivity == ScheduleActivity.Work;
            isTargetOnBreak = isTargetWorking && target.CharacterJob?.CurrentJob?.CurrentGoalName == "Idle";
        }

        bool areCoworkers = _character.CharacterJob != null && target.CharacterJob != null &&
                            _character.CharacterJob.HasJob && target.CharacterJob.HasJob &&
                            _character.CharacterJob.Workplace == target.CharacterJob.Workplace;

        // Restriction: while on a work break, we only talk to colleagues
        if (isOnBreak && (!areCoworkers || !isTargetOnBreak)) return;

        // Restriction: if the target is on a work break, we can't talk to them unless we're their colleague (and also on break)
        if (isTargetOnBreak && (!areCoworkers || !isOnBreak)) return;

        if (_character.CharacterRelation == null) return;

        // --- 1. ASSISTANCE LOGIC (FRIENDS OR PARTY MEMBER IN COMBAT) ---
        // Priority: if it's a friend or party member in combat, help them no matter what
        if (target.CharacterCombat.IsInBattle && target.IsAlive())
        {
            bool isFriend = _character.CharacterRelation != null && _character.CharacterRelation.IsFriend(target);
            bool sameParty = _character.CharacterParty != null && _character.CharacterParty.IsInParty
                && target.CharacterParty != null && target.CharacterParty.IsInParty
                && _character.CharacterParty.PartyData.PartyId == target.CharacterParty.PartyData.PartyId;

            // Loyalty trait makes characters more likely to help acquaintances (not just friends)
            bool isAcquaintance = _character.CharacterRelation != null && _character.CharacterRelation.GetRelationshipWith(target)?.RelationValue >= 0;
            bool isLoyalHelp = _character.CharacterTraits != null && (_character.CharacterTraits.GetLoyalty() > 0.5f) && isAcquaintance;

            if (isFriend || sameParty || isLoyalHelp)
            {
                string helpMsg = sameParty ? "Protect the group!" : "Hang on, my friend! I'm coming!";
                Debug.Log($"<color=green>[Assistance]</color> {_character.CharacterName} sees their {(sameParty ? "teammate" : "friend")} {target.CharacterName} in combat and rushes to help!");
                
                if (_character.CharacterSpeech != null)
                    _character.CharacterSpeech.Say(helpMsg);

                _character.CharacterCombat.JoinBattleAsAlly(target);
                return;
            }
        }

        var rel = _character.CharacterRelation.GetRelationshipWith(target);

        // --- 2a. SPONTANEOUS AGGRESSION (trait-based, against anyone) ---
        // A highly aggressive character can attack even a stranger
        if (target.IsAlive() && _character.CharacterTraits != null)
        {
            float aggressivity = _character.CharacterTraits.GetAggressivity();
            
            // Gated curve to avoid permanent bloodbaths at low/medium aggressivity.
            // Must be highly aggressive (>= 0.7) to spontaneously attack a random stranger.
            if (aggressivity >= 0.7f)
            {
                // E.g. at 1.0 -> 0.3^2 = 0.09 * 0.2 = ~1.8% chance per target
                float aggroChance = Mathf.Pow(aggressivity - 0.7f, 2f) * 0.2f;

                if (aggroChance > 0f && Random.value < aggroChance)
                {
                    Debug.Log($"<color=red>[Aggression]</color> {_character.CharacterName} spontaneously attacks {target.CharacterName} (Aggressivity: {aggressivity:P0})!");
                    if (_character.CharacterSpeech != null)
                        _character.CharacterSpeech.Say("You're in my way!");
                    
                    if (BehaviourTree != null)
                    {
                        BehaviourTree.Blackboard.Set(Blackboard.KEY_COMBAT_TARGET, target);
                    }
                    return;
                }
            }
        }

        // --- 2b. AGGRESSION LOGIC (KNOWN ENEMIES) ---
        // We attack our enemies even if they're chatting!
        if (rel != null && target.IsAlive() && rel.RelationValue <= -10)
        {
            // Much gentler curve: Base 2% chance (+1% per 0.1 aggro).
            float aggroChance = 0.02f;
            if (_character.CharacterTraits != null)
            {
                aggroChance += _character.CharacterTraits.GetAggressivity() * 0.10f;
            }

            if (Random.value < aggroChance)
            {
                Debug.Log($"<color=red>[Aggression]</color> {_character.CharacterName} spots their enemy {target.CharacterName} and attacks! (Chance: {aggroChance:P0})");
                if (BehaviourTree != null)
                {
                    BehaviourTree.Blackboard.Set(Blackboard.KEY_COMBAT_TARGET, target);
                }
                return;
            }
        }

        // --- 3. SOCIAL SAFETY: if the target isn't free (combat, busy), we stop here for the social path ---
        if (!target.IsFree()) return;

        // --- WORK FOCUS: don't bother a worker (unless they're technically on a break) ---
        if (!isTargetOnBreak && target.Controller is NPCController targetNpc && targetNpc.CurrentBehaviour != null && targetNpc.CurrentBehaviour.GetType().Name == "WorkBehaviour") return;

        // --- 4. INCOMPATIBILITY LOGIC (PERSONALITY) ---
        // We test this even if we don't know each other yet (gut feeling)
        if (_character.CharacterProfile != null && target.CharacterProfile != null)
        {
            if (_character.CharacterProfile.GetCompatibilityWith(target.CharacterProfile) < 0)
            {
                // 20% chance to spontaneously go insult them
                if (Random.value < 0.2f)
                {
                    Debug.Log($"<color=orange>[Personality Clash]</color> {_character.CharacterName} instinctively dislikes {target.CharacterName} and is going to insult them!");
                    
                    if (_character.CharacterSpeech != null)
                        _character.CharacterSpeech.Say("You! I don't like your face!");

                    if (target != null && target.IsAlive())
                        _character.CharacterInteraction.StartInteractionWith(target, new InteractionInsult());
                    return;
                }
            }
        }

        // --- 5. LOGIQUE SOCIALE ---
        float sociability = _character.CharacterTraits != null ? _character.CharacterTraits.GetSociability() : 0.5f;

        // --- COLLEAGUES-ON-BREAK EXCEPTION ---
        if (isOnBreak && isTargetOnBreak && areCoworkers)
        {
            float breakChatChance = 0.5f + (sociability * 0.5f); // 50% to 100% chance of having a coffee chat
            if (Random.value < breakChatChance)
            {
                Debug.Log($"<color=cyan>[Social - Break]</color> {_character.CharacterName} is going to chat with their colleague {target.CharacterName} during their break.");
                if (_character.CharacterSpeech != null)
                    _character.CharacterSpeech.Say("Taking a break too, right? Need to stretch my hands.");

                if (target != null && target.IsAlive())
                {
                    var invitation = new InteractionStartDialogue();
                    if (invitation.CanExecute(_character, target))
                        invitation.Execute(_character, target);
                }
            }
            return;
        }

        // If we don't know each other, we can still go talk to the stranger depending on sociability
        if (rel == null)
        {
            // Only approach a stranger if sociable enough (e.g. sociability 0.5 = 15% base chance)
            float strangerChance = sociability * 0.3f;
            if (Random.value < strangerChance)
            {
                Debug.Log($"<color=cyan>[Social - Stranger]</color> {_character.CharacterName} approaches the stranger {target.CharacterName} (Sociability: {sociability:P0})");
                if (target != null && target.IsAlive())
                {
                    var invitation = new InteractionStartDialogue();
                    if (invitation.CanExecute(_character, target))
                        invitation.Execute(_character, target);
                }
            }
            return;
        }

        // If we already know each other: 70% chance of preferring a close friend over a stranger
        bool isFriendKnown = _character.CharacterRelation.IsFriend(target);
        bool preferFriend = isFriendKnown && (Random.value < 0.7f);

        float interactionChance = rel.GetInteractionChance();

        // Apply sociability
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

            if (target != null && target.IsAlive())
            {
                var invitation = new InteractionStartDialogue();
                if (invitation.CanExecute(_character, target))
                    invitation.Execute(_character, target);
            }
        }
    }

    /// <summary>
    /// Determines whether the NPC agrees to respond to an interaction.
    /// </summary>
    public bool ShouldRespondTo(Character source)
    {
        if (_character.CharacterRelation == null) return false;
        var rel = _character.CharacterRelation.GetRelationshipWith(source);
        if (rel == null) return false;

        return Random.value < rel.GetResponseChance();
    }

    /// <summary>
    /// Picks a social action (Talk, Invite, or Insult) based on the relationship.
    /// </summary>
    public ICharacterInteractionAction GetRandomSocialAction(Character target)
    {
        // First check if an invite is possible and roll for it (e.g. 50% chance if eligible)
        var inviteAction = new InteractionInviteCommunity();
        bool targetThinking = target.CharacterInvitation != null && target.CharacterInvitation.HasPendingInvitation;

        if (!targetThinking && inviteAction.CanExecute(_character, target))
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
        if (!IsServer) return;
        
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
        // 1. If we're already in combat, we're involved
        if (_character.CharacterCombat.IsInBattle) return;

        // 2. Look for a friend in this battle
        foreach (var team in manager.BattleTeams)
        {
            foreach (var participant in team.CharacterList)
            {
                if (_character.CharacterRelation != null && _character.CharacterRelation.IsFriend(participant))
                {
                    Debug.Log($"<color=green>[Battle Sensor]</color> {_character.CharacterName} sees their friend {participant.CharacterName} in the battle and joins them!");
                    _character.CharacterCombat.JoinBattleAsAlly(participant);
                    return;
                }
            }
        }

        // 3. Otherwise, we're not involved -> we leave
        if (BehaviourTree != null && BehaviourTree.Blackboard != null)
        {
            // Avoid spamming if we're already fleeing THIS battle
            if (BehaviourTree.Blackboard.Get<BattleManager>(Blackboard.KEY_FLEE_BATTLE_MANAGER) != manager)
            {
                Debug.Log($"<color=white>[Battle Sensor]</color> {_character.CharacterName} has nothing to do here, walks away.");
                BehaviourTree.Blackboard.Set(Blackboard.KEY_FLEE_BATTLE_MANAGER, manager);
            }
        }
    }
}
