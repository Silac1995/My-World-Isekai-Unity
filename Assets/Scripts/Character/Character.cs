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
    [SerializeField] private CharacterEquipment equipment;

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
    public CharacterEquipment CharacterEquipment => equipment;

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
        AdjustCapsuleCollider();
    }

    public void InitializeStats(float health, float mana, float strength, float agility)
    {
        stats.InitializeStats(health, mana, strength, agility);
    }

    public void InitializeRace(RaceSO raceData)
    {
        race = raceData ?? throw new System.ArgumentNullException(nameof(raceData));
        Stats.MoveSpeed.IncreaseBaseValue(race.bonusSpeed);
        controller.Initialize();
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
        // Désactive le NPCController
        NPCController npcCtrl = GetComponent<NPCController>();
        if (npcCtrl != null)
        {
            npcCtrl.enabled = false;
        }

        // Désactive NavMeshAgent
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
        }

        // Active le PlayerController
        PlayerController playerCtrl = GetComponent<PlayerController>();
        if (playerCtrl != null)
        {
            playerCtrl.enabled = true;
            playerCtrl.Initialize();
            controller = playerCtrl;
        }
        else
        {
            Debug.LogError("PlayerController manquant", this);
        }

        // Rigidbody pour contrôle joueur
        if (rb != null)
        {
            rb.isKinematic = false;
        }
    }

    public void SwitchToNPCController()
    {
        // Désactive le PlayerController
        PlayerController playerCtrl = GetComponent<PlayerController>();
        if (playerCtrl != null)
        {
            playerCtrl.enabled = false;
        }

        // Active le NPCController
        NPCController npcCtrl = GetComponent<NPCController>();
        if (npcCtrl != null)
        {
            npcCtrl.enabled = true;
            npcCtrl.Initialize();
            controller = npcCtrl;
        }
        else
        {
            Debug.LogError("NPCController manquant", this);
        }

        // Active le NavMeshAgent
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = true;
            agent.updatePosition = true;
            agent.updateRotation = false;
        }

        // Rigidbody en kinematic pour IA
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


    //Action on item
    public void UseConsumable(ConsumableInstance consumable)
    {

    }

    public void EquipGear(EquipmentInstance equipment)
    {

    }

    public void DropItem(ItemInstance itemToDrop)
    {
        if (itemToDrop == null) return;

        // 1. Chargement dynamique du prefab depuis Resources
        GameObject worldItemPrefab = Resources.Load<GameObject>("Prefabs/WorldItem");
        if (worldItemPrefab == null)
        {
            Debug.LogError("[Character] Drop impossible : Prefab 'WorldItem' introuvable dans Resources/Prefabs");
            return;
        }

        // 2. Calcul de la position devant le personnage
        Vector3 dropPos = transform.position + (transform.forward * 1.5f);
        dropPos.y = transform.position.y;

        // 3. Instanciation
        GameObject go = Instantiate(worldItemPrefab, dropPos, Quaternion.identity);
        go.name = $"WorldItem_{itemToDrop.ItemSO.ItemName}";

        // 4. Initialisation du composant WorldItem (Essentiel pour ton ItemInteractable)
        // On utilise GetComponentsInChildren au cas où le script est sur un enfant du prefab
        WorldItem worldItem = go.GetComponentInChildren<WorldItem>();

        if (worldItem != null)
        {
            // On injecte l'instance. C'est ce qui remplira le "get" de ton ItemInteractable
            worldItem.Initialize(itemToDrop);

            // 5. Application visuelle (Couleur)
            if (itemToDrop is EquipmentInstance equipment && equipment.HaveCustomizedColor())
            {
                MeshRenderer visualRenderer = go.GetComponentInChildren<MeshRenderer>();
                if (visualRenderer != null)
                {
                    visualRenderer.material.color = equipment.CustomizedColor;
                }
            }

            Debug.Log($"<color=green>[Drop Success]</color> {itemToDrop.ItemSO.ItemName} initialisé au sol.");
        }
        else
        {
            // Si on arrive ici, l'interaction renverra l'erreur [FATAL] car WorldItem est absent
            Debug.LogError($"[Drop Error] Le prefab {go.name} n'a pas de composant WorldItem !");
        }
    }
}