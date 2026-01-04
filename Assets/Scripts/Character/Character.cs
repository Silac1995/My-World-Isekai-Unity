using System;
using UnityEngine;
using UnityEngine.AI;

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
    private NavMeshAgent cachedNavMeshAgent;
    private static BattleManager battleManagerPrefab;
    private static GameObject worldItemPrefab;
    private BattleManager battleManager;
    private CharacterInteraction characterInteraction;
    private bool isDead;
    private const string BATTLE_MANAGER_PATH = "Prefabs/BattleManagerPrefab";
    private const string WORLD_ITEM_PATH = "Prefabs/WorldItem";
    private const float DROP_DISTANCE = 1.5f;

    public event Action<Character> OnDeath;

    // Properties
    public string CharacterName
    {
        get => characterName;
        set => characterName = value;
    }
    public float MovementSpeed => stats?.MoveSpeed.CurrentValue ?? 0f;
    public Rigidbody Rigidbody => rb ?? throw new NullReferenceException($"Rigidbody missing on {gameObject.name}");
    public CapsuleCollider Collider => col ?? throw new NullReferenceException($"CapsuleCollider missing on {gameObject.name}");
    public CharacterStats Stats => stats ?? throw new NullReferenceException($"CharacterStats missing on {gameObject.name}");
    public RaceSO Race => race;
    public Transform VisualRoot => visualRoot;
    public GameObject CurrentVisualInstance => currentVisualInstance;
    public CharacterGameController Controller => controller;
    public CharacterVisual CharacterVisual => characterVisual;
    public BattleManager BattleManager => battleManager;
    public CharacterActions CharacterActions => characterActions;
    public CharacterInteraction CharacterInteraction => characterInteraction ?? throw new NullReferenceException($"CharacterInteraction not initialized on {gameObject.name}");
    public CharacterBio CharacterBio => characterBio;
    public CharacterEquipment CharacterEquipment => equipment;

    protected virtual void Awake()
    {
        if (rb == null || col == null)
        {
            Debug.LogError($"{name} a des références manquantes !");
            enabled = false;
            return;
        }

        if (battleManagerPrefab == null)
            battleManagerPrefab = Resources.Load<BattleManager>(BATTLE_MANAGER_PATH);
        
        if (worldItemPrefab == null)
            worldItemPrefab = Resources.Load<GameObject>(WORLD_ITEM_PATH);

        cachedNavMeshAgent = GetComponent<NavMeshAgent>();
        characterInteraction = new CharacterInteraction(this);
        isDead = false;

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
        if (stats == null)
        {
            Debug.LogError($"{name} : CharacterStats not assigned!", this);
            return;
        }

        stats.InitializeStats(health, mana, strength, agility);
    }

    public void InitializeRace(RaceSO raceData)
    {
        race = raceData ?? throw new ArgumentNullException(nameof(raceData));
        
        if (stats == null)
        {
            Debug.LogError($"{name} : Cannot initialize race without stats!", this);
            return;
        }

        Stats.MoveSpeed.IncreaseBaseValue(race.bonusSpeed);
        
        if (controller != null)
            controller.Initialize();
        else
            Debug.LogWarning($"{name} : Controller not assigned when initializing race.", this);
    }

    public void InitializeSpriteRenderers()
    {
        if (characterVisual != null)
            characterVisual.InitializeSpriteRenderers();
    }

    public void AssignVisualRoot(Transform root)
    {
        visualRoot = root ?? throw new ArgumentNullException(nameof(root), $"Attempting to assign null visualRoot on {name}");
    }

    public void AssignVisualInstance(GameObject visualInstance)
    {
        currentVisualInstance = visualInstance;
    }

    private void AdjustCapsuleCollider()
    {
        if (currentVisualInstance == null)
        {
            Debug.LogWarning($"{name} : currentVisualInstance not assigned, skipping collider adjustment.", this);
            return;
        }

        CapsuleCollider visualCollider = currentVisualInstance.GetComponentInChildren<CapsuleCollider>();

        if (visualCollider != null && col != null)
        {
            col.center = visualCollider.center;
            col.radius = visualCollider.radius;
            col.height = visualCollider.height;
            col.direction = visualCollider.direction;
            col.isTrigger = visualCollider.isTrigger;
            col.material = visualCollider.material;

            if (Application.isPlaying)
                Destroy(visualCollider);
            else
                DestroyImmediate(visualCollider);
        }
    }

    public bool IsPlayer() => controller is PlayerController;

    public bool IsAlive() => !isDead;

    public virtual void Die()
    {
        if (isDead)
            return;

        isDead = true;
        Debug.Log($"{name} is dead.");
        OnDeath?.Invoke(this);

        if (col != null)
            col.enabled = false;
        if (rb != null)
            rb.isKinematic = true;

        if (Controller != null)
        {
            Controller.enabled = false;
            if (Controller.Animator != null)
                Controller.Animator.SetBool("isDead", true);
        }
    }

    public void TakeDamage(float damage = 1)
    {
        if (!IsAlive() || stats == null)
            return;

        stats.Health.CurrentAmount -= damage;
        Debug.Log($"{name} took {damage} damage, remaining health: {stats.Health.CurrentAmount}");

        if (stats.Health.CurrentAmount <= 0)
            Die();
    }

    [ContextMenu("Take Damage")]
    public void TakeDamage()
    {
        TakeDamage(50f);
    }

    public bool IsInBattle() => battleManager != null;

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

    public void StartFight(Character target)
    {
        if (!ValidateFightStart(target))
            return;

        BattleManager newBattleManager = CreateBattleManager();
        if (newBattleManager == null)
            return;

        InitializeBattle(newBattleManager, target);
    }

    private bool ValidateFightStart(Character target)
    {
        if (!IsAlive())
        {
            Debug.LogWarning($"{CharacterName} is dead and cannot start a fight.");
            return false;
        }

        if (target == null)
        {
            Debug.LogError("Target character is null.");
            return false;
        }

        if (!target.IsAlive())
        {
            Debug.LogWarning($"{target.CharacterName} is dead and cannot be targeted for a fight.");
            return false;
        }

        if (IsInBattle())
        {
            Debug.LogWarning($"{CharacterName} is already in a battle and cannot start another one.");
            return false;
        }

        if (target.IsInBattle())
        {
            Debug.LogWarning($"{target.CharacterName} is already in a battle and cannot be targeted for a new fight.");
            return false;
        }

        return true;
    }

    private BattleManager CreateBattleManager()
    {
        if (battleManagerPrefab == null)
        {
            Debug.LogError("BattleManagerPrefab is not assigned! Please assign it before starting fights.");
            return null;
        }

        GameObject battleManagerGO = Instantiate(battleManagerPrefab.gameObject);
        BattleManager manager = battleManagerGO.GetComponent<BattleManager>();

        if (manager == null)
        {
            Debug.LogError("The instantiated BattleManagerPrefab does not have a BattleManager component.");
            Destroy(battleManagerGO);
            return null;
        }

        return manager;
    }

    private void InitializeBattle(BattleManager manager, Character target)
    {
        BattleTeam team1 = new BattleTeam();
        BattleTeam team2 = new BattleTeam();

        team1.AddCharacter(this);
        team2.AddCharacter(target);

        manager.AddTeam(team1);
        manager.AddTeam(team2);
        manager.Initialize(this, target);

        JoinBattle(manager);
        target.JoinBattle(manager);

        Debug.Log($"Fight started between {CharacterName} and {target.CharacterName}");
    }

    public void SwitchToPlayer()
    {
        SwitchController<PlayerController>(GetComponent<NPCController>());
        SwitchInteractionDetector<PlayerInteractionDetector, NPCInteractionDetector>();
    }

    public void SwitchToNPC()
    {
        SwitchController<NPCController>(GetComponent<PlayerController>());
        SwitchInteractionDetector<NPCInteractionDetector, PlayerInteractionDetector>();
    }

    [ContextMenu("Switch To Player")]
    public void SwitchToPlayerContext() => SwitchToPlayer();

    [ContextMenu("Switch To NPC")]
    public void SwitchToNPCContext() => SwitchToNPC();

    private void SwitchController<TTarget>(CharacterGameController toDisable) where TTarget : CharacterGameController
    {
        if (toDisable != null)
            toDisable.enabled = false;

        TTarget target = GetComponent<TTarget>();
        if (target != null)
        {
            target.enabled = true;
            target.Initialize();
            controller = target;
        }
        else
        {
            Debug.LogError($"{typeof(TTarget).Name} missing", this);
            return;
        }

        bool isNPCMode = typeof(TTarget) == typeof(NPCController);
        ConfigureRigidbodyForMode(isNPCMode);
        ConfigureNavMeshAgentForMode(isNPCMode);
    }

    private void ConfigureRigidbodyForMode(bool isNPCMode)
    {
        if (rb != null)
            rb.isKinematic = isNPCMode;
    }

    private void ConfigureNavMeshAgentForMode(bool isNPCMode)
    {
        if (cachedNavMeshAgent == null)
            return;

        cachedNavMeshAgent.enabled = isNPCMode;

        if (isNPCMode)
        {
            cachedNavMeshAgent.updatePosition = true;
            cachedNavMeshAgent.updateRotation = false;
        }
    }

    private void SwitchInteractionDetector<TTarget, TDisable>()
        where TTarget : MonoBehaviour
        where TDisable : MonoBehaviour
    {
        TDisable toDisable = GetComponent<TDisable>();
        if (toDisable != null)
            toDisable.enabled = false;

        TTarget target = GetComponent<TTarget>();
        if (target != null)
        {
            target.enabled = true;
        }
        else
        {
            gameObject.AddComponent<TTarget>();
        }
    }

    public bool IsFree() => !IsInBattle() && !CharacterInteraction.IsInteracting;

    public void UseConsumable(ConsumableInstance consumable)
    {
        // TODO: Implémenter
    }

    public void EquipGear(EquipmentInstance equipment)
    {
        // TODO: Implémenter
    }

    public void DropItem(ItemInstance itemToDrop)
    {
        if (itemToDrop == null || stats == null)
            return;

        if (worldItemPrefab == null)
        {
            Debug.LogError("[Character] Drop impossible : Prefab 'WorldItem' introuvable dans Resources/Prefabs");
            return;
        }

        Vector3 dropPos = transform.position + (transform.forward * DROP_DISTANCE);
        dropPos.y = transform.position.y;

        GameObject go = Instantiate(worldItemPrefab, dropPos, Quaternion.identity);
        go.name = $"WorldItem_{itemToDrop.ItemSO.ItemName}";

        WorldItem worldItem = go.GetComponentInChildren<WorldItem>();

        if (worldItem != null)
        {
            worldItem.Initialize(itemToDrop);

            if (itemToDrop is EquipmentInstance equipmentInstance && equipmentInstance.HaveCustomizedColor())
            {
                MeshRenderer visualRenderer = go.GetComponentInChildren<MeshRenderer>();
                if (visualRenderer != null)
                    visualRenderer.material.color = equipmentInstance.CustomizedColor;
            }

            Debug.Log($"<color=green>[Drop Success]</color> {itemToDrop.ItemSO.ItemName} initialisé au sol.");
        }
        else
        {
            Debug.LogError($"[Drop Error] Le prefab {go.name} n'a pas de composant WorldItem !");
        }
    }
}