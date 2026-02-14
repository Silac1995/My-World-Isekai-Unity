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

        // On passe par _characterMovement pour configurer l'agent
        if (_characterMovement != null && _character != null)
        {
            var agent = _characterMovement.Agent; // J'ai ajouté l'accesseur dans CharacterMovement plus bas
            if (agent != null)
            {
                agent.speed = _character.MovementSpeed;
                agent.acceleration = 50f;
                agent.angularSpeed = 0f;
            }
        }
        Agent.updateRotation = false;

        ResetStackTo(new WanderBehaviour(this));
    }
}