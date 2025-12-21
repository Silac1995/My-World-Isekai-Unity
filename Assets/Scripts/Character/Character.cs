using NUnit.Framework.Constraints;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TextCore.Text;

[RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody))]
public class Character : MonoBehaviour
{
    [Header("Basic Info")]
    [SerializeField] private string characterName;
    [SerializeField] private CharacterBio characterBio;

    [Header("Stats")]
    [SerializeField] private CharacterStats stats;

    [Header("Race")]
    [SerializeField] private RaceSO race;

    [Header("RigidBody & Collider")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CapsuleCollider col;

    [Header("Scripts")]
    [SerializeField] private CharacterBodyPartsController bodyPartsController;
    [SerializeField] private CharacterActions characterActions;
    [SerializeField] private CharacterVisual characterVisual;
    [SerializeField] private CharacterGameController controller;

    private Transform visualRoot;
    private GameObject currentVisualInstance;
    private static BattleManager battleManagerPrefab;
    private BattleManager battleManager;
    private CharacterInteraction characterInteraction;
    

    // public events
    public event Action<Character> OnDeath; // Événement déclenché à la mort

    // Properties
    public string CharacterName
    {
        get { return characterName; }
        set { characterName = value; }
    }
    public float MovementSpeed => stats.MoveSpeed.CurrentValue;
    public Rigidbody Rigidbody => rb ?? throw new System.NullReferenceException($"Rigidbody missing on {gameObject.name}");
    public CapsuleCollider Collider => col ?? throw new System.NullReferenceException($"CapsuleCollider missing on {gameObject.name}");
    public CharacterStats Stats => stats;
    public RaceSO Race => race;
    public Transform VisualRoot => visualRoot;
    public GameObject CurrentVisualInstance => currentVisualInstance;
    public CharacterGameController Controller => controller;
    public CharacterVisual CharacterVisual => characterVisual;
    public BattleManager BattleManager => battleManager;
    public CharacterActions CharacterActions => characterActions;
    public CharacterInteraction CharacterInteraction => characterInteraction;
    public CharacterBio CharacterBio => characterBio;

    protected virtual void Awake()
    {
        // Sécurité : vérifie quand même
        if (rb == null || col == null)
        {
            Debug.LogError($"{name} a des références manquantes !");
            enabled = false;
            return;
        }
        battleManagerPrefab = Resources.Load<BattleManager>("Prefabs/BattleManagerPrefab");

        characterInteraction = new CharacterInteraction(this);

        InitializeComponents();
    }

    private void InitializeComponents()
    {
        rb.freezeRotation = true;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    public void InitializeAll()
    {
        if (controller == null)
        {
            controller = GetComponent<CharacterGameController>() ?? gameObject.AddComponent<CharacterGameController>();
        }
        controller.Initialize();
        AdjustCapsuleCollider();
    }

    public void InitializeStats(float health, float mana, float strength, float agility)
    {
        stats = new CharacterStats(this, health, mana, strength, agility);
    }

    public void InitializeRace(RaceSO raceData)
    {
        race = raceData ?? throw new System.ArgumentNullException(nameof(raceData));
        Stats.MoveSpeed.IncreaseBaseValue(race.bonusSpeed);
    }

    public void InitializeAnimator()
    {
        if (controller != null)
            controller.InitializeAnimator();
    }

    public void InitializeSpriteRenderers()
    {
        if (characterVisual != null)
            characterVisual.InitializeSpriteRenderers();
    }

    public void AssignVisualRoot(Transform root)
    {
        visualRoot = root ?? throw new System.ArgumentNullException(nameof(root), $"Attempting to assign null visualRoot on {name}");
    }

    private void AdjustCapsuleCollider()
    {
        if (currentVisualInstance == null) return;

        // Chercher un CapsuleCollider dans le gameobject Visual ou ses enfants
        CapsuleCollider visualCollider = currentVisualInstance.GetComponentInChildren<CapsuleCollider>();

        if (visualCollider != null && col != null)
        {
            // Copier tous les paramètres du collider enfant vers le collider parent
            col.center = visualCollider.center;
            col.radius = visualCollider.radius;
            col.height = visualCollider.height;
            col.direction = visualCollider.direction;
            col.isTrigger = visualCollider.isTrigger;
            col.material = visualCollider.material;

            Debug.Log($"Copied collider parameters from {visualCollider.gameObject.name} to {gameObject.name}");

            // Détruire le collider enfant
            if (Application.isPlaying)
            {
                Destroy(visualCollider);
            }
            else
            {
                DestroyImmediate(visualCollider);
            }

            Debug.Log($"Destroyed child collider on {visualCollider.gameObject.name}");
        }
    }

    public bool IsPlayer() => controller is PlayerController;

    public void SetController<T>() where T : CharacterGameController
    {
        if (controller != null)
            Destroy(controller);

        controller = gameObject.AddComponent<T>();
        controller.Initialize();
    }

    public virtual void Die()
    {
        Debug.Log($"{name} is dead.");
        OnDeath?.Invoke(this);

        // Désactivation collider & rigidbody
        if (col != null) col.enabled = false;
        if (rb != null) rb.isKinematic = true;

        // Déclenche animation morte si besoin
        Controller.enabled = false;
        Controller.Animator.SetBool("isDead", true);
    }

    public void TakeDamage(float damage = 1)
    {
        if (!IsAlive()) return;

        Stats.Health.CurrentAmount -= damage;
        Debug.Log($"{name} took {damage} damage, remaining health: {Stats.Health.CurrentAmount}");

        if (Stats.Health.CurrentAmount <= 0)
        {
            Die();
        }
    }
    [ContextMenu("Take Damage")]
    public void TakeDamage()
    {
        TakeDamage(50f);
    }

    public bool IsInBattle()
    {
        if (battleManager == null) return false;
        return true;
    }
    public void JoinBattle(BattleManager manager)
    {
        if (manager == null)
            throw new ArgumentNullException(nameof(manager));
        battleManager = manager;
    }
    public void LeaveBattle()
    {
        battleManager = null;
    }

    public bool IsAlive()
    {
        if (Stats.Health.CurrentAmount <= 0) return false;
        return true;
    }

    public void StartFight(Character target)
    {
        if (!IsAlive())
        {
            Debug.LogWarning($"{CharacterName} is dead and cannot start a fight.");
            return;
        }

        if (target == null)
        {
            Debug.LogError("Target character is null.");
            return;
        }

        if (!target.IsAlive())
        {
            Debug.LogWarning($"{target.CharacterName} is dead and cannot be targeted for a fight.");
            return;
        }

        if (IsInBattle())
        {
            Debug.LogWarning($"{CharacterName} is already in a battle and cannot start another one.");
            return;
        }

        if (target.IsInBattle())
        {
            Debug.LogWarning($"{target.CharacterName} is already in a battle and cannot be targeted for a new fight.");
            return;
        }

        if (battleManagerPrefab == null)
        {
            Debug.LogError("BattleManagerPrefab is not assigned! Please assign it before starting fights.");
            return;
        }

        GameObject battleManagerGO = Instantiate(battleManagerPrefab.gameObject);
        BattleManager battleManager = battleManagerGO.GetComponent<BattleManager>();

        if (battleManager == null)
        {
            Debug.LogError("The instantiated BattleManagerPrefab does not have a BattleManager component.");
            return;
        }

        BattleTeam team1 = new BattleTeam();
        BattleTeam team2 = new BattleTeam();

        team1.AddCharacter(this);
        team2.AddCharacter(target);

        battleManager.AddTeam(team1);
        battleManager.AddTeam(team2);

        battleManager.Initialize(this, target);

        JoinBattle(battleManager);
        target.JoinBattle(battleManager);

        Debug.Log($"Fight started between {CharacterName} and {target.CharacterName}");
    }


    // switch to npc/player
    public void SwitchToPlayerController()
    {
        // Désactive le NPCController s'il existe
        NPCController npcCtrl = GetComponent<NPCController>();
        if (npcCtrl != null)
        {
            npcCtrl.enabled = false;
        }

        // Désactive NavMeshAgent (IA) si présent
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
        }

        // Active le PlayerController s'il existe
        PlayerController playerCtrl = GetComponent<PlayerController>();
        if (playerCtrl != null)
        {
            playerCtrl.enabled = true;
            playerCtrl.Initialize();
        }

        // Rigidbody doit rester actif pour le contrôle du joueur
        if (rb != null)
        {
            rb.isKinematic = false;
        }
    }
    public void SwitchToNPCController()
    {
        // Désactive le PlayerController s'il existe
        PlayerController playerCtrl = GetComponent<PlayerController>();
        if (playerCtrl != null)
        {
            playerCtrl.enabled = false;
        }

        // Active le NPCController s'il existe
        NPCController npcCtrl = GetComponent<NPCController>();
        if (npcCtrl != null)
        {
            npcCtrl.enabled = true;
            npcCtrl.Initialize();
        }

        // Active le NavMeshAgent si présent
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = true;
            agent.updatePosition = true;
            agent.updateRotation = false; // rotation gérée manuellement dans NPCController
        }

        // Rigidbody en kinematic pour éviter les conflits physiques avec l'IA
        if (rb != null)
        {
            rb.isKinematic = true;
        }
    }

    public void SwitchToPlayerInteractionDetector()
    {
        // Désactive le NPCInteractionDetector s'il existe
        NPCInteractionDetector npcDetector = GetComponent<NPCInteractionDetector>();
        if (npcDetector != null)
        {
            npcDetector.enabled = false;
        }

        // Active le PlayerInteractionDetector s'il existe
        PlayerInteractionDetector playerDetector = GetComponent<PlayerInteractionDetector>();
        if (playerDetector != null)
        {
            playerDetector.enabled = true;
        }
        else
        {
            // Crée le composant si jamais il est manquant
            playerDetector = gameObject.AddComponent<PlayerInteractionDetector>();
        }
    }

    public void SwitchToNPCInteractionDetector()
    {
        // Désactive le PlayerInteractionDetector s'il existe
        PlayerInteractionDetector playerDetector = GetComponent<PlayerInteractionDetector>();
        if (playerDetector != null)
        {
            playerDetector.enabled = false;
        }

        // Active le NPCInteractionDetector s'il existe
        NPCInteractionDetector npcDetector = GetComponent<NPCInteractionDetector>();
        if (npcDetector != null)
        {
            npcDetector.enabled = true;
        }
        else
        {
            // Crée le composant si jamais il est manquant
            npcDetector = gameObject.AddComponent<NPCInteractionDetector>();
        }
    }


    [ContextMenu("Switch To Player")]
    public void SwitchToPlayer()
    {
        // Switch controller
        SwitchToPlayerController();

        // Switch interaction detector
        SwitchToPlayerInteractionDetector();
    }

    [ContextMenu("Switch To NPC")]
    public void SwitchToNPC()
    {
        // Switch controller
        SwitchToNPCController();

        // Switch interaction detector
        SwitchToNPCInteractionDetector();
    }

    public bool IsFree()
    {
        return !IsInBattle() && !CharacterInteraction.IsInInteraction();
    }

}