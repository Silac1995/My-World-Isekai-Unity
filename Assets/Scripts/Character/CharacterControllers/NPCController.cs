using UnityEngine;

public class NPCController : CharacterGameController
{
    [Header("Wander Settings")]
    [SerializeField] private float walkRadius = 50f;
    [SerializeField] private float minWaitTime = 2f;
    [SerializeField] private float maxWaitTime = 7f;
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

        if (agent != null && character != null)
        {
            // On synchronise la vitesse de l'agent avec celle définie dans le Character
            agent.speed = character.MovementSpeed;

            // On booste l'accélération pour que le NPC atteigne sa vitesse instantanément 
            // comme le ferait un joueur (évite l'effet de démarrage lourd)
            agent.acceleration = 50f;
            agent.angularSpeed = 0f; // On gère la rotation nous-mêmes ou via le flip
        }

        ResetStackTo(new WanderBehaviour(this));
    }
}