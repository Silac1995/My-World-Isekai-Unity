using UnityEngine;

public class NPCController : CharacterGameController
{
    [Header("Wander Settings")]
    [SerializeField] private float walkRadius = 50f;
    [SerializeField] private float minWaitTime = 0.5f;
    [SerializeField] private float maxWaitTime = 3f;
    public float WalkRadius
    {
        get => walkRadius;
        set => walkRadius = value;
    }

    public float MinWaitTime
    {
        get => minWaitTime;
        set => minWaitTime = value;
    }

    public float MaxWaitTime
    {
        get => maxWaitTime;
        set => maxWaitTime = value;
    }


    public override void Initialize()
    {
        base.Initialize();
        // On initialise la pile avec le Wander par défaut
        ResetStackTo(new WanderBehaviour(this));

        if (agent != null)
        {
            agent.updateRotation = false;
            agent.acceleration = 9999f;
            agent.speed = character != null ? character.MovementSpeed : 3.5f;
        }
    }
}