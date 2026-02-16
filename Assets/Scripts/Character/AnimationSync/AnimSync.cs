using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gère la synchronisation d'animations entre deux personnages.
/// Exemple : poignée de main, danse, high-five, etc.
/// </summary>
public class AnimSync : MonoBehaviour
{
    #region Serialized Fields
    [Header("Characters")]
    [SerializeField] private Character _initiator;
    [SerializeField] private Character _target;

    [Header("Animation Settings")]
    [SerializeField] private string _syncTriggerName = "SyncAnimation";
    [SerializeField] private int _syncAnimationHash;
    
    [Header("Positioning")]
    [SerializeField] private bool _alignPositions = true;
    [SerializeField] private float _targetDistance = 1.5f;
    [SerializeField] private bool _faceEachOther = true;
    #endregion

    #region Private Fields
    private bool _isSyncing;
    private float _syncStartTime;
    private float _syncDuration;
    #endregion

    #region Events
    public event Action<Character, Character> OnSyncStarted;
    public event Action<Character, Character> OnSyncEnded;
    #endregion

    #region Properties
    public bool IsSyncing => _isSyncing;
    public Character Initiator => _initiator;
    public Character Target => _target;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        // Auto-initialise l'initiator depuis le Character du GameObject
        Initialize();

        // Cache le hash du trigger pour optimiser
        if (!string.IsNullOrEmpty(_syncTriggerName))
        {
            _syncAnimationHash = Animator.StringToHash(_syncTriggerName);
        }
    }

    private void Update()
    {
        if (_isSyncing)
        {
            // Vérifie si la synchronisation est terminée
            if (Time.time >= _syncStartTime + _syncDuration)
            {
                EndSync();
            }
        }
    }

    private void OnDestroy()
    {
        if (_isSyncing)
            EndSync();

        // Nettoie les events pour que les subscribers ne gardent pas de ref vers ce composant
        OnSyncStarted = null;
        OnSyncEnded = null;
        _initiator = null;
        _target = null;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Initialise la synchronisation avec deux personnages.
    /// </summary>
    public void Initialize(Character initiator, Character target)
    {
        _initiator = initiator;
        _target = target;
    }

    /// <summary>
    /// Initialise l'initiator à partir du Character attaché au même GameObject.
    /// </summary>
    public void Initialize()
    {
        _initiator = GetComponent<Character>();
        if (_initiator == null)
        {
            Debug.LogWarning("<color=red>[AnimSync]</color> Aucun Character trouvé sur ce GameObject.");
        }
    }

    /// <summary>
    /// Démarre la synchronisation d'animation avec une cible spécifique.
    /// </summary>
    /// <param name="target">Le personnage cible pour la synchronisation</param>
    /// <param name="triggerName">Nom du trigger d'animation à déclencher</param>
    /// <param name="duration">Durée de la synchronisation (0 = auto-détection)</param>
    public void StartSync(Character target, string triggerName = null, float duration = 0f)
    {
        _target = target;
        StartSync(triggerName, duration);
    }

    /// <summary>
    /// Démarre la synchronisation d'animation.
    /// </summary>
    /// <param name="triggerName">Nom du trigger d'animation à déclencher</param>
    /// <param name="duration">Durée de la synchronisation (0 = auto-détection)</param>
    public void StartSync(string triggerName = null, float duration = 0f)
    {
        if (!ValidateSync()) return;

        // Utilise le trigger par défaut si aucun n'est fourni
        string trigger = string.IsNullOrEmpty(triggerName) ? _syncTriggerName : triggerName;
        int triggerHash = Animator.StringToHash(trigger);

        // Positionne les personnages si nécessaire
        if (_alignPositions)
        {
            AlignCharacters();
        }

        // Démarre l'animation sur les deux personnages
        TriggerAnimation(_initiator, triggerHash);
        TriggerAnimation(_target, triggerHash);

        // Détermine la durée
        _syncDuration = duration > 0f ? duration : GetAnimationDuration(_initiator, trigger);
        _syncStartTime = Time.time;
        _isSyncing = true;

        // Notifie les observateurs
        OnSyncStarted?.Invoke(_initiator, _target);

        // MULTIPLAYER NOTE: Ici, tu devras envoyer un message réseau pour démarrer la sync
        // Exemple: NetworkManager.SendSyncStart(_initiator.NetworkId, _target.NetworkId, triggerHash, _syncDuration);
        
        Debug.Log($"<color=cyan>[AnimSync]</color> Synchronisation démarrée entre {_initiator.CharacterName} et {_target.CharacterName}");
    }

    /// <summary>
    /// Arrête la synchronisation immédiatement.
    /// </summary>
    public void EndSync()
    {
        if (!_isSyncing) return;

        _isSyncing = false;

        // Notifie les observateurs
        OnSyncEnded?.Invoke(_initiator, _target);

        Debug.Log($"<color=cyan>[AnimSync]</color> Synchronisation terminée entre {_initiator.CharacterName} et {_target.CharacterName}");
        
        // MULTIPLAYER NOTE: Ici, tu devras envoyer un message réseau pour synchroniser la fin
        // Exemple: NetworkManager.SendSyncEnd(_initiator.NetworkId, _target.NetworkId);
    }

    /// <summary>
    /// Vérifie si un Character est actuellement en train de synchroniser une animation.
    /// </summary>
    /// <param name="character">Le personnage à vérifier</param>
    /// <returns>True si le personnage est en synchronisation</returns>
    public static bool IsCharacterSyncing(Character character)
    {
        if (character == null) return false;

        // Cherche tous les AnimSync sur ce Character
        AnimSync[] syncs = character.GetComponents<AnimSync>();
        foreach (AnimSync sync in syncs)
        {
            if (sync.IsSyncing) return true;
        }

        return false;
    }
    #endregion

    #region Private Methods
    private bool ValidateSync()
    {
        if (_initiator == null)
        {
            Debug.LogWarning("<color=red>[AnimSync]</color> Impossible de synchroniser : Initiator est null.");
            return false;
        }

        if (_target == null)
        {
            Debug.LogWarning("<color=red>[AnimSync]</color> Impossible de synchroniser : Target est null.");
            return false;
        }

        if (_isSyncing)
        {
            Debug.LogWarning("<color=yellow>[AnimSync]</color> Une synchronisation est déjà en cours.");
            return false;
        }

        // Vérifie si l'initiator est déjà en train de synchroniser avec un autre AnimSync
        if (IsCharacterSyncing(_initiator))
        {
            Debug.LogWarning($"<color=yellow>[AnimSync]</color> {_initiator.CharacterName} est déjà en train de synchroniser une animation.");
            return false;
        }

        // Vérifie si la cible est déjà en train de synchroniser
        if (IsCharacterSyncing(_target))
        {
            Debug.LogWarning($"<color=yellow>[AnimSync]</color> {_target.CharacterName} est déjà occupé dans une synchronisation.");
            return false;
        }

        return true;
    }

    private void AlignCharacters()
    {
        if (_initiator == null || _target == null) return;

        // Calcule le point médian sur l'axe X uniquement
        float midpointX = (_initiator.transform.position.x + _target.transform.position.x) / 2f;
        
        // Utilise la position Y et Z de l'initiator comme référence (ou moyenne)
        float sharedY = (_initiator.transform.position.y + _target.transform.position.y) / 2f;
        float sharedZ = (_initiator.transform.position.z + _target.transform.position.z) / 2f;

        // Calcule la direction sur l'axe X (gauche ou droite)
        float directionX = Mathf.Sign(_target.transform.position.x - _initiator.transform.position.x);

        // Positionne les personnages à la distance voulue uniquement sur l'axe X
        Vector3 initiatorPos = _initiator.transform.position;
        initiatorPos.x = midpointX - directionX * (_targetDistance / 2f);
        initiatorPos.y = sharedY;
        initiatorPos.z = sharedZ;
        _initiator.transform.position = initiatorPos;

        Vector3 targetPos = _target.transform.position;
        targetPos.x = midpointX + directionX * (_targetDistance / 2f);
        targetPos.y = sharedY;
        targetPos.z = sharedZ;
        _target.transform.position = targetPos;

        // Fait se faire face si nécessaire
        if (_faceEachOther)
        {
            FaceCharacters();
        }
    }

    private void FaceCharacters()
    {
        if (_initiator == null || _target == null) return;

        // Calcule la direction pour que chaque personnage regarde l'autre
        Vector3 initiatorToTarget = (_target.transform.position - _initiator.transform.position).normalized;
        Vector3 targetToInitiator = -initiatorToTarget;

        // Applique la rotation (pour un jeu 2D, tu peux utiliser transform.right ou localScale.x)
        // Ici j'assume que tu utilises le système de flip par scale
        float initiatorDirection = Mathf.Sign(initiatorToTarget.x);
        float targetDirection = Mathf.Sign(targetToInitiator.x);

        // Ajuste le scale sur l'axe X pour faire face à l'autre personnage
        Vector3 initiatorScale = _initiator.transform.localScale;
        initiatorScale.x = Mathf.Abs(initiatorScale.x) * initiatorDirection;
        _initiator.transform.localScale = initiatorScale;

        Vector3 targetScale = _target.transform.localScale;
        targetScale.x = Mathf.Abs(targetScale.x) * targetDirection;
        _target.transform.localScale = targetScale;
    }

    private void TriggerAnimation(Character character, int triggerHash)
    {
        if (character?.CharacterVisual?.CharacterAnimator?.Animator == null) return;

        character.CharacterVisual.CharacterAnimator.Animator.SetTrigger(triggerHash);
    }

    private float GetAnimationDuration(Character character, string clipName)
    {
        if (character?.CharacterVisual?.CharacterAnimator == null) return 1f;

        // Tente de récupérer la durée depuis le cache
        float duration = character.CharacterVisual.CharacterAnimator.GetCachedDuration(clipName);
        
        // Fallback sur la durée du clip actuel
        if (duration <= 0f)
        {
            duration = character.CharacterVisual.CharacterAnimator.GetCurrentClipDuration();
        }

        return duration > 0f ? duration : 1f; // Durée par défaut de 1s
    }
    #endregion

    #region Context Menu (Debug)
    [ContextMenu("Start Test Sync")]
    private void DebugStartSync()
    {
        StartSync();
    }

    [ContextMenu("End Sync")]
    private void DebugEndSync()
    {
        EndSync();
    }
    #endregion
}
