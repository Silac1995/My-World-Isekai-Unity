using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattleManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private List<BattleTeam> _teams = new List<BattleTeam>();
    [SerializeField] private BattleTeam _battleTeamInitiator;
    [SerializeField] private BattleTeam _battleTeamTarget;
    [SerializeField] private Collider _battleZone;
    [SerializeField] private Unity.AI.Navigation.NavMeshModifierVolume _battleZoneModifier;
    [SerializeField] private LineRenderer _battleZoneLine;
    [SerializeField] private Vector3 _baseBattleZoneSize = new Vector3(25f, 35f, 10f);
    [SerializeField] private float _perParticipantGrowthRate = 0.3f;
    [SerializeField] private int _participantsPerTier = 6;

    [Header("Initiative System")]
    [SerializeField] private float _ticksPerSecond = 10f;
    private float _tickTimer = 0f;

    private bool _isBattleEnded = false;

    // --- NOUVEAU SYSTÈME : ENGAGEMENTS DE COMBAT ---
    private List<CombatEngagement> _activeEngagements = new List<CombatEngagement>();

    // Liste pour le debug
    [SerializeField] private List<Character> _allParticipants = new List<Character>();

    [Header("Debug — Engagements actifs")]
    [SerializeField] private List<string> _debugEngagements = new List<string>();

    public List<BattleTeam> BattleTeams => _teams;
    public bool IsBattleEnded => _isBattleEnded;

    public void Initialize(Character initiator, Character target)
    {
        if (initiator == null || target == null) return;

        // 1. Setup des équipes (On s'assure qu'il n'y en a QUE 2)
        _teams.Clear();
        _battleTeamInitiator = new BattleTeam();
        _battleTeamTarget = new BattleTeam();

        _battleTeamInitiator.AddCharacter(initiator);
        _battleTeamTarget.AddCharacter(target);

        _teams.Add(_battleTeamInitiator);
        _teams.Add(_battleTeamTarget);

        // 2. Création physique de la zone
        CreateBattleZone(initiator, target);

        // 3. Inscription des participants
        RegisterParticipants();

        // 4. Créer l'engagement initial entre l'initiateur et la cible
        RequestEngagement(initiator, target);

        // 5. Rendu visuel UNIQUE (pas dans Update)
        DrawBattleZoneOutline();

        Debug.Log($"<color=orange>[Battle]</color> Combat lance : {initiator.name} vs {target.name}");
    }

    private void Update()
    {
        if (_isBattleEnded) return;

        // Gestion du temps de combat (Ticks d'initiative)
        _tickTimer += Time.deltaTime;
        float tickPeriod = 1f / _ticksPerSecond;

        if (_tickTimer >= tickPeriod)
        {
            _tickTimer -= tickPeriod;
            PerformBattleTick();
        }

        // Debug : mise à jour de la liste d'engagements pour l'Inspector
        UpdateDebugEngagements();
    }

    private void UpdateDebugEngagements()
    {
        _debugEngagements.Clear();
        for (int i = 0; i < _activeEngagements.Count; i++)
        {
            var e = _activeEngagements[i];
            string groupA = string.Join(", ", e.GroupA.Members
                .Where(m => m != null)
                .Select(m => m.CharacterName));
            string groupB = string.Join(", ", e.GroupB.Members
                .Where(m => m != null)
                .Select(m => m.CharacterName));
            _debugEngagements.Add($"[{i}] A: [{groupA}]  vs  B: [{groupB}]");
        }
    }

    private void PerformBattleTick()
    {
        foreach (var character in _allParticipants)
        {
            if (character == null || !character.IsAlive()) continue;

            if (character.CharacterCombat != null)
            {
                character.CharacterCombat.UpdateInitiativeTick();
            }
        }
    }

    private void CreateBattleZone(Character a, Character b)
    {
        // On s'assure que le manager est neutre en rotation/scale
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        // Cleanup
        var oldColliders = gameObject.GetComponents<BoxCollider>();
        foreach (var old in oldColliders) Destroy(old);

        // On utilise un BoxCollider pour les limites
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        gameObject.tag = "BattleZone";
        
        box.isTrigger = true;

        // Taille de base définie par l'user
        box.size = _baseBattleZoneSize;
        
        // Position initiale : milieu entre les deux combattants
        Vector3 center = (a.transform.position + b.transform.position) / 2f;
        transform.position = new Vector3(center.x, a.transform.position.y, center.z);

        // --- NOUVEAU : RÉSOLUTION DES SUPERPOSITIONS ---
        ResolveZoneOverlap();

        _battleZone = box;

        // --- NOUVEAU : AJOUT DU MODIFICATEUR DE NAVMESH ---
        // Permet d'augmenter le coût de la zone pour que les PNJs l'évitent
        if (_battleZoneModifier != null)
        {
            _battleZoneModifier.size = box.size;
            _battleZoneModifier.center = box.center;
        }
    }

    private void ResolveZoneOverlap()
    {
        int maxAttempts = 5;
        // On utilise la taille de base pour la détection initiale
        Vector3 halfExtents = _baseBattleZoneSize / 2f;

        for (int i = 0; i < maxAttempts; i++)
        {
            // On cherche d'autres zones de combat (le tag "BattleZone" est crucial ici)
            Collider[] overlaps = Physics.OverlapBox(transform.position, halfExtents, Quaternion.identity);
            bool foundOverlap = false;

            foreach (var other in overlaps)
            {
                // On s'ignore soi-même et on ne cible que les autres BattleZones
                if (other.gameObject != gameObject && other.CompareTag("BattleZone"))
                {
                    foundOverlap = true;
                    
                    // Calcul d'une direction de répulsion (de l'autre vers nous)
                    Vector3 pushDir = (transform.position - other.transform.position);
                    pushDir.y = 0; // On ne décale que sur le plan horizontal
                    
                    if (pushDir.sqrMagnitude < 0.01f) 
                        pushDir = Vector3.right; // Fallback si positions identiques
                    else
                        pushDir.Normalize();

                    // Décalage d'un demi-diamètre pour sortir de la zone de collision
                    float shiftAmount = _baseBattleZoneSize.x * 0.5f;
                    Vector3 targetPos = transform.position + pushDir * shiftAmount;

                    // --- NOUVEAU : VALIDATION NAVMESH ---
                    // On ne décale QUE si la destination est majoritairement sur le NavMesh
                    if (IsZoneValidOnNavMesh(targetPos))
                    {
                        transform.position = targetPos;
                        Debug.Log($"<color=cyan>[Battle]</color> Superposition detectee ! Decalage vers zone navigable : {transform.position}");
                    }
                    else
                    {
                        // Si le décalage nous sort du NavMesh, on tente une direction perpendiculaire
                        Vector3 altDir = Quaternion.Euler(0, 90, 0) * pushDir;
                        Vector3 altPos = transform.position + altDir * shiftAmount;

                        if (IsZoneValidOnNavMesh(altPos))
                        {
                            transform.position = altPos;
                            Debug.Log($"<color=cyan>[Battle]</color> Superposition detectee ! Sortie NavMesh evitee, decalage lateral : {transform.position}");
                        }
                        else
                        {
                            Debug.LogWarning($"<color=red>[Battle]</color> Impossible d'eviter la superposition sans sortir du NavMesh. On s'arrete ici.");
                            return;
                        }
                    }
                    break; // On re-check à la prochaine itération de la boucle for
                }
            }

            if (!foundOverlap) break;
        }

        // SECURITE FINALE : Si la zone finale est toujours hors NavMesh (cas du spawn initial), on la ramène
        if (!IsZoneValidOnNavMesh(transform.position))
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                transform.position = hit.position;
            }
        }
    }

    private bool IsZoneValidOnNavMesh(Vector3 position)
    {
        // On vérifie une grille de 9 points (3x3) dans la zone pour s'assurer que 50% sont sur le NavMesh
        int pointsOnNavMesh = 0;
        int totalPoints = 9;
        
        float halfX = _baseBattleZoneSize.x * 0.5f;
        float halfZ = _baseBattleZoneSize.z * 0.5f;

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                // On échantillonne un peu en retrait des bords (80%) pour éviter les faux négatifs sur les bordures
                Vector3 samplePoint = position + new Vector3(x * halfX * 0.8f, 0, z * halfZ * 0.8f);
                if (UnityEngine.AI.NavMesh.SamplePosition(samplePoint, out UnityEngine.AI.NavMeshHit hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    pointsOnNavMesh++;
                }
            }
        }

        // On exige au moins 5 points sur 9 (55%) pour valider la zone
        return pointsOnNavMesh >= 5;
    }

    private void RegisterParticipants()
    {
        _allParticipants.Clear();

        foreach (var team in _teams)
        {
            foreach (var character in team.CharacterList)
            {
                if (character == null) continue;
                RegisterCharacter(character);
            }
        }
    }

    private void RegisterCharacter(Character character)
    {
        if (_allParticipants.Contains(character)) return;

        _allParticipants.Add(character);

        if (character.CharacterCombat != null)
        {
            character.CharacterCombat.JoinBattle(this);
            character.CharacterCombat.ConsumeInitiative();

            if (!character.IsPlayer())
            {
                Character bestEnemy = GetBestTargetFor(character);
                if (bestEnemy != null)
                {
                    character.Controller.PushBehaviour(new CombatBehaviour(this, bestEnemy));
                    RequestEngagement(character, bestEnemy);
                }
            }
        }

        character.OnIncapacitated -= HandleCharacterIncapacitated;
        character.OnIncapacitated += HandleCharacterIncapacitated;
        character.OnDeath -= HandleCharacterIncapacitated;
        character.OnDeath += HandleCharacterIncapacitated;

        UpdateBattleZoneWith(character);
        Debug.Log($"<color=white>[Battle]</color> {character.CharacterName} a rejoint le combat.");
    }

    public void AddParticipant(Character newParticipant, Character target, bool asAlly = false)
    {
        if (newParticipant == null || target == null || _isBattleEnded) return;

        // On trouve l'équipe de la cible
        BattleTeam targetTeam = GetTeamOf(target);
        if (targetTeam == null) return;

        if (asAlly)
        {
            // On le met dans la MÊME équipe que la cible
            targetTeam.AddCharacter(newParticipant);
        }
        else
        {
            // On le met dans l'équipe ADVERSE de la cible
            BattleTeam enemyTeam = (targetTeam == _battleTeamInitiator) ? _battleTeamTarget : _battleTeamInitiator;
            enemyTeam.AddCharacter(newParticipant);
        }

        RegisterCharacter(newParticipant);
    }

    private void UpdateBattleZoneWith(Character character)
    {
        if (_battleZone == null || _allParticipants.Count == 0) return;
        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        // 1. Calcul du nombre de participants valides
        int count = _allParticipants.Count(p => p != null);

        // 2. Calcul de la taille par paliers (Ex: chaque 6 persos)
        // Multiplier = 1 + (Nombre de paliers * Taux)
        int tiers = (count - 1) / _participantsPerTier;
        // La zone peut grandir à l'infini
        
        float multiplier = 1f + (tiers * _perParticipantGrowthRate);

        // On n'applique le multiplicateur qu'à X et Z (le sol). Y reste fixe.
        box.size = new Vector3(_baseBattleZoneSize.x * multiplier, _baseBattleZoneSize.y, _baseBattleZoneSize.z * multiplier);

        // --- NOUVEAU : METTRE À JOUR LE MODIFICATEUR DE NAVMESH ---
        if (_battleZoneModifier != null)
        {
            _battleZoneModifier.size = box.size;
            _battleZoneModifier.center = box.center;
        }

        DrawBattleZoneOutline();
    }

    #region Helpers
    public BattleTeam GetTeamOf(Character character)
    {
        return _teams.FirstOrDefault(t => t.CharacterList.Contains(character));
    }

    public BattleTeam GetOpponentTeamOf(Character character)
    {
        BattleTeam myTeam = GetTeamOf(character);
        if (myTeam == null) 
        {
            Debug.LogWarning($"<color=red>[Battle]</color> {character.CharacterName} n'est dans aucune équipe ! Impossible de trouver son opposant.");
            return null;
        }
        return (myTeam == _battleTeamInitiator) ? _battleTeamTarget : _battleTeamInitiator;
    }

    /// <summary>
    /// Trouve le meilleur ennemi à cibler.
    /// Priorise les ennemis dont l'engagement n'est pas plein pour l'équipe de l'attaquant.
    /// Si tout est plein, choisit un ennemi valide.
    /// </summary>
    public Character GetBestTargetFor(Character attacker)
    {
        if (attacker == null) return null;

        BattleTeam myTeam = GetTeamOf(attacker);
        BattleTeam opponentTeam = GetOpponentTeamOf(attacker);
        if (opponentTeam == null) return null;

        List<Character> aliveEnemies = opponentTeam.CharacterList.Where(e => e != null && e.IsAlive()).ToList();
        if (aliveEnemies.Count == 0) return null;

        // 1. Séparer en cibles "libres" (pas d'engagement ou engagement non plein) et cibles "pleines"
        List<Character> availableTargets = new List<Character>();
        List<Character> fullTargets = new List<Character>();

        foreach (var enemy in aliveEnemies)
        {
            CombatEngagement enemyEngagement = _activeEngagements.Find(e => e.GroupA.Members.Contains(enemy) || e.GroupB.Members.Contains(enemy));
            
            if (enemyEngagement == null || !enemyEngagement.IsFullFor(myTeam))
            {
                availableTargets.Add(enemy);
            }
            else
            {
                fullTargets.Add(enemy);
            }
        }

        // 2. Si on a des cibles "libres", on prend la plus proche
        if (availableTargets.Count > 0)
        {
            return GetClosestFromList(attacker.transform.position, availableTargets);
        }

        // 3. Fallback : Tout est plein, on prend au hasard pour rejoindre une mêlée et la diviser ensuite
        return fullTargets[Random.Range(0, fullTargets.Count)];
    }

    private Character GetClosestFromList(Vector3 position, List<Character> characters)
    {
        Character closest = null;
        float minDistance = float.MaxValue;
        foreach (var character in characters)
        {
            float dist = Vector3.Distance(position, character.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = character;
            }
        }
        return closest;
    }

    // --- GESTION DES ENGAGEMENTS (COMBAT SLOTS) ---
    
    /// <summary>
    /// Un attaquant demande un slot de combat autour de sa cible.
    /// Le Manager cherche un engagement existant ou en crée un nouveau.
    /// </summary>
    public CombatEngagement RequestEngagement(Character attacker, Character target)
    {
        if (target == null || attacker == null) return null;

        // 1. Quitter l'engagement actuel s'il y en a un
        LeaveCurrentEngagement(attacker);

        // 2. Chercher si un engagement existe déjà pour cette cible
        CombatEngagement engagement = _activeEngagements.Find(e => 
            e.GroupA.Members.Contains(target) || e.GroupB.Members.Contains(target)
        );

        BattleTeam attackerTeam = GetTeamOf(attacker);
        BattleTeam targetTeam = GetTeamOf(target);

        // 3. Si pas trouvé par cible, chercher un engagement PROCHE
        //    pour éviter que deux formations se chevauchent
        if (engagement == null)
        {
            float mergeDistance = 10f; // Distance max pour fusionner deux engagements
            float bestDist = float.MaxValue;

            foreach (var existing in _activeEngagements)
            {
                // Ne fusionner QUE si l'engagement existant contient les bonnes équipes et N'EST PAS PLEIN
                if (existing.TeamA != targetTeam && existing.TeamB != targetTeam) continue;
                if (existing.IsFullFor(attackerTeam)) continue;

                // Calculer le centre de l'engagement existant
                Vector3 engagementCenter = Vector3.zero;
                bool hasCenter = false;
                
                if (existing.GroupA.TryGetCenter(out Vector3 centerA) && existing.GroupB.TryGetCenter(out Vector3 centerB))
                {
                    engagementCenter = (centerA + centerB) / 2f;
                    hasCenter = true;
                }
                else if (existing.GroupA.TryGetCenter(out Vector3 cA))
                {
                    engagementCenter = cA;
                    hasCenter = true;
                }
                else if (existing.GroupB.TryGetCenter(out Vector3 cB))
                {
                    engagementCenter = cB;
                    hasCenter = true;
                }

                if (hasCenter)
                {
                    float dist = Vector3.Distance(target.transform.position, engagementCenter);
                    if (dist < mergeDistance && dist < bestDist)
                    {
                        bestDist = dist;
                        engagement = existing;
                    }
                }
            }
        }

        // 4. Sinon, on crée la mêlée
        if (engagement == null)
        {
            if (targetTeam != null && attackerTeam != null)
            {
                engagement = new CombatEngagement(this, targetTeam, attackerTeam);
                // On ajoute tout de suite la cible pour initialiser le groupe
                engagement.JoinEngagement(target); 
                _activeEngagements.Add(engagement);
            }
        }

        // 5. On rejoint l'escarmouche pour avoir un slot
        if (engagement != null)
        {
            engagement.JoinEngagement(attacker);
            // S'assurer que la cible est aussi dans l'engagement
            engagement.JoinEngagement(target);

            // 6. Vérifier si l'engagement doit être séparé en deux
            if (engagement.NeedsSplit())
            {
                SplitEngagement(engagement);
            }
        }

        return engagement;
    }

    /// <summary>
    /// Divise un engagement en deux si l'un de ses côtés dépasse la limite.
    /// Les combattants qui sont déplacés dans le nouvel engagement changeront de cible.
    /// </summary>
    private void SplitEngagement(CombatEngagement originalEngagement)
    {
        if (originalEngagement == null) return;

        // 1. Créer le nouvel engagement
        CombatEngagement newEngagement = new CombatEngagement(this, originalEngagement.TeamA, originalEngagement.TeamB);
        _activeEngagements.Add(newEngagement);

        // 2. Extraire la moitié des participants de chaque côté
        List<Character> groupAKeep = new List<Character>();
        List<Character> groupAMove = new List<Character>();
        for (int i = 0; i < originalEngagement.GroupA.Members.Count; i++)
        {
            if (i < originalEngagement.GroupA.Members.Count / 2)
                groupAKeep.Add(originalEngagement.GroupA.Members[i]);
            else
                groupAMove.Add(originalEngagement.GroupA.Members[i]);
        }

        List<Character> groupBKeep = new List<Character>();
        List<Character> groupBMove = new List<Character>();
        for (int i = 0; i < originalEngagement.GroupB.Members.Count; i++)
        {
            if (i < originalEngagement.GroupB.Members.Count / 2)
                groupBKeep.Add(originalEngagement.GroupB.Members[i]);
            else
                groupBMove.Add(originalEngagement.GroupB.Members[i]);
        }

        // 3. Déplacer vers le nouveau, on ne doit pas garder ceux qui sont déplacés
        foreach (var c in groupAMove)
        {
            originalEngagement.LeaveEngagement(c);
            newEngagement.JoinEngagement(c);
        }
        foreach (var c in groupBMove)
        {
            originalEngagement.LeaveEngagement(c);
            newEngagement.JoinEngagement(c);
        }

        Debug.Log($"<color=cyan>[Battle]</color> Escarmouche trop grande : elle est divisée en deux.");

        // 4. Forcer un changement de cible pour que les membres redirigent la formation correctement
        ForceRetarget(newEngagement);
    }

    private void ForceRetarget(CombatEngagement engagement)
    {
        List<Character> allMembers = new List<Character>();
        allMembers.AddRange(engagement.GroupA.Members);
        allMembers.AddRange(engagement.GroupB.Members);

        foreach (var character in allMembers)
        {
            if (character == null || !character.IsAlive()) continue;

            // Retirer l'ancien comportement
            var combatBehaviour = character.Controller.GetCurrentBehaviour<CombatBehaviour>();
            if (combatBehaviour != null)
            {
                Character bestEnemy = GetBestTargetFor(character);
                if (bestEnemy != null && bestEnemy != combatBehaviour.Target)
                {
                    combatBehaviour.SetCurrentTarget(bestEnemy);
                }
            }
        }
    }

    /// <summary>
    /// Retire le personnage de toutes les formations d'attaque actives.
    /// À appeler quand il change de cible ou meurt.
    /// </summary>
    public void LeaveCurrentEngagement(Character attacker)
    {
        foreach (var engagement in _activeEngagements)
        {
            engagement.LeaveEngagement(attacker);
        }
    }

    /// <summary>
    /// Nettoie les escarmouches terminées (ex: cible morte)
    /// </summary>
    private void CleanupEngagements()
    {
        _activeEngagements.RemoveAll(e => e.IsFinished());
    }
    #endregion

    private void HandleCharacterIncapacitated(Character incapacitatedCharacter)
    {
        if (_isBattleEnded) return;

        // --- NOUVEAU : DIAGNOSTIC DE FIN DE COMBAT ---
        int initiatorAlive = _battleTeamInitiator.CharacterList.Count(c => c != null && c.IsAlive());
        int targetAlive = _battleTeamTarget.CharacterList.Count(c => c != null && c.IsAlive());
        
        string initiatorNames = string.Join(", ", _battleTeamInitiator.CharacterList.Where(c => c != null && c.IsAlive()).Select(c => c.CharacterName));
        string targetNames = string.Join(", ", _battleTeamTarget.CharacterList.Where(c => c != null && c.IsAlive()).Select(c => c.CharacterName));

        Debug.Log($"<color=white>[Battle Check]</color> {incapacitatedCharacter.CharacterName} tombe. " +
                  $"Team Initiateur : {initiatorAlive}/{_battleTeamInitiator.CharacterList.Count} ({initiatorNames}) | " +
                  $"Team Cible : {targetAlive}/{_battleTeamTarget.CharacterList.Count} ({targetNames})");

        // 1. Vérifier si le combat est terminé (UNE des deux équipes principales est éliminée)
        if (_battleTeamInitiator.IsTeamEliminated() || _battleTeamTarget.IsTeamEliminated())
        {
            Debug.Log($"<color=red>[Battle]</color> Elimination detectee. Fin du combat.");
            EndBattle();
            return;
        }

        // 2. Si le combat continue, on redirige ceux qui tapaient l'incapacité
        RedirectIncapacitated(incapacitatedCharacter);
    }

    private void RedirectIncapacitated(Character victim)
    {
        // On nettoie l'engagement centré sur le personnage tombé (libère les slots des attaquants)
        CleanupEngagements();
        
        foreach (var participant in _allParticipants)
        {
            if (participant == null || participant.IsIncapacitated) continue;

            var combatBehaviour = participant.Controller.GetCurrentBehaviour<CombatBehaviour>();

            if (combatBehaviour != null)
            {
                if (!combatBehaviour.HasTarget || combatBehaviour.Target == victim)
                {
                    Character nextTarget = GetBestTargetFor(participant);

                    if (nextTarget != null)
                    {
                        combatBehaviour.SetCurrentTarget(nextTarget);
                        Debug.Log($"<color=yellow>[Battle]</color> {participant.CharacterName} a perdu sa cible ({victim.CharacterName}) et se tourne vers {nextTarget.CharacterName}");
                    }
                }
            }
        }
    }


    public bool AreOpponents(Character a, Character b)
    {
        if (a == null || b == null || a == b) return false;
        
        BattleTeam teamA = GetTeamOf(a);
        BattleTeam teamB = GetTeamOf(b);

        return teamA != null && teamB != null && teamA != teamB;
    }

    public void EndBattle()
    {
        if (_isBattleEnded) return;
        _isBattleEnded = true;

        foreach (var character in _allParticipants)
        {
            if (character != null)
            {
                character.OnIncapacitated -= HandleCharacterIncapacitated;
                character.OnDeath -= HandleCharacterIncapacitated;
                character.CharacterCombat.LeaveBattle();
                LeaveCurrentEngagement(character); // Nettoie au cas où
            }
        }
        
        _activeEngagements.Clear();

        Debug.Log("<color=red>[Battle]</color> Le combat est TERMINÉ.");
        Destroy(gameObject);
    }

    public void DrawBattleZoneOutline()
    {
        if (_battleZoneLine == null || _battleZone == null) return;

        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        _battleZoneLine.useWorldSpace = true;
        _battleZoneLine.loop = true;
        _battleZoneLine.positionCount = 4;

        // Calcul des coins basés sur le BoxCollider
        Vector3 center = box.transform.position;
        Vector3 size = box.size;

        float x = size.x / 2f;
        float z = size.z / 2f;
        float y = center.y; 

        Vector3[] corners = new Vector3[4]
        {
            center + new Vector3(-x, 0, -z),
            center + new Vector3(-x, 0, z),
            center + new Vector3(x, 0, z),
            center + new Vector3(x, 0, -z)
        };

        _battleZoneLine.SetPositions(corners);

        Debug.Log("<color=cyan>[Battle]</color> Outline de combat dessinée au sol.");
    }
}
