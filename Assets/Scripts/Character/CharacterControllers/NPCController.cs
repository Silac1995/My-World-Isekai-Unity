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

    [Header("AI Scanning")]
    [SerializeField] private float scanInterval = 4f;
    private float lastScanTime;

    public override void Initialize()
    {
        base.Initialize();

        // On passe par _characterMovement pour configurer l'agent
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

        ResetStackTo(new WanderBehaviour(this));
        lastScanTime = Time.time + Random.Range(0f, scanInterval);
    }

    protected virtual void Update()
    {
        if (_wasDoingAction || _character.CharacterActions.CurrentAction != null)
        {
            // On délègue à CharacterGameController pour le lock de mouvement/flip
            base.Update();
            return;
        }

        // On ne scanne que si on est en train de flâner
        if (GetCurrentBehaviour<WanderBehaviour>() != null)
        {
            if (Time.time - lastScanTime > scanInterval)
            {
                lastScanTime = Time.time;
                TryFindRandomTarget();
            }
        }

        base.Update();
    }

    private void TryFindRandomTarget()
    {
        if (_character.CharacterAwareness == null) return;

        // On récupère les personnages visibles
        var visibleInteractables = _character.CharacterAwareness.GetVisibleInteractables();
        var candidates = new System.Collections.Generic.List<Character>();

        foreach (var interactable in visibleInteractables)
        {
            if (interactable is CharacterInteractable charInteractable)
            {
                Character target = charInteractable.Character;
                if (target != null && target.IsAlive())
                {
                    // On ne cible que ceux qui ne sont pas déjà en combat
                    if (target.CharacterCombat != null && !target.CharacterCombat.IsInBattle)
                    {
                        candidates.Add(target);
                    }
                }
            }
        }

        if (candidates.Count > 0)
        {
            // Chance de 15% de devenir agressif
            if (Random.value < 0.15f)
            {
                Character choice = candidates[Random.Range(0, candidates.Count)];
                Debug.Log($"<color=orange>[AI]</color> {_character.CharacterName} a repéré {choice.CharacterName} et décide de l'attaquer !");
                PushBehaviour(new AttackTargetBehaviour(choice));
            }
        }
    }
}