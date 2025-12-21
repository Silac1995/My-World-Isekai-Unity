using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class BattleManager : MonoBehaviour
{
    [SerializeField] private List<BattleTeam> teams = new List<BattleTeam>();
    public List<BattleTeam> BattleTeams => teams;

    [SerializeField] private Collider battleZone;
    public Collider BattleZone => battleZone;

    [SerializeField] private LineRenderer battleZoneLine;

    private bool isBattleEnded = false;
    public bool IsBattleEnded => isBattleEnded;

    // Liste de tous les personnages (juste pour debug dans l'inspecteur)
    [SerializeField, Tooltip("Liste auto-générée à partir des équipes")]
    private List<Character> allCharactersDebug = new List<Character>();

    private void Update()
    {
        DrawBattleZoneOutline();
    }

    public void Initialize(Character initiator, Character target)
    {
        if (initiator == null || target == null)
        {
            Debug.LogError("Invalid characters for battle initialization.");
            return;
        }

        // Créer zone
        CreateBattleZone(initiator, target);

        // Créer équipes
        var teamA = new BattleTeam();
        var teamB = new BattleTeam();

        teamA.AddCharacter(initiator);
        teamB.AddCharacter(target);

        AddTeam(teamA);
        AddTeam(teamB);

        // Assigner BattleManager à chaque perso
        initiator.JoinBattle(this);
        target.JoinBattle(this);

        // Abonnement aux événements de mort
        SubscribeToDeathEvents();

        RefreshAllCharactersDebug();

        Debug.Log($"Fight started between {initiator.CharacterName} and {target.CharacterName}");
    }


    /// <summary>
    /// Récupère tous les personnages des équipes.
    /// </summary>
    public List<Character> GetAllCharacters()
    {
        var allChars = new List<Character>();
        foreach (var team in teams)
        {
            if (team == null) continue;
            foreach (var character in team.CharacterList)
            {
                if (character != null && !allChars.Contains(character))
                    allChars.Add(character);
            }
        }
        return allChars;
    }

    /// <summary>
    /// Met à jour la liste visible dans l'inspecteur (debug)
    /// </summary>
    private void RefreshAllCharactersDebug()
    {
        allCharactersDebug = GetAllCharacters();
    }

    private void CreateBattleZone(Character charA, Character charB, float padding = 20f)
    {
        if (charA == null || charB == null || charA.Collider == null || charB.Collider == null)
        {
            Debug.LogError("Cannot create battle zone: characters or colliders are missing.");
            return;
        }

        // Bounds combinés des deux personnages
        Bounds combinedBounds = charA.Collider.bounds;
        combinedBounds.Encapsulate(charB.Collider.bounds);
        combinedBounds.Expand(padding * 2f);

        // Création du BoxCollider
        var boxCollider = gameObject.AddComponent<BoxCollider>();
        boxCollider.isTrigger = true;
        transform.position = combinedBounds.center;
        boxCollider.center = Vector3.zero;
        boxCollider.size = combinedBounds.size;

        battleZone = boxCollider;

        // Crée la ligne rouge visible en jeu
        DrawBattleZoneOutline();

        Debug.Log($"Created battle zone (size: {combinedBounds.size}, center: {combinedBounds.center})", this);
    }


    private void SubscribeToDeathEvents()
    {
        foreach (var team in teams)
        {
            foreach (var character in team.CharacterList)
            {
                character.OnDeath += HandleCharacterDeath;
            }
        }
    }

    private void HandleCharacterDeath(Character deadCharacter)
    {
        Debug.Log($"{name} invoked event.");
        if (isBattleEnded) return;

        foreach (var team in teams)
        {
            bool allDead = true;
            foreach (var character in team.CharacterList)
            {
                if (character.IsAlive())
                {
                    allDead = false;
                    break;
                }
            }

            if (allDead)
            {
                isBattleEnded = true;
                EndBattle();
                return;
            }
        }
    }

    [ContextMenu("End battle")]
    public void EndBattle()
    {
        if (isBattleEnded is false) return;

        Debug.Log("Battle ended!");

        foreach (var team in teams)
        {
            foreach (var character in team.CharacterList)
            {
                if (character != null)
                {
                    character.OnDeath -= HandleCharacterDeath;

                    // Réinitialiser le BattleManager du personnage
                    character.LeaveBattle();
                }
            }
        }

        // Optionnel : vider la liste des équipes si la battle manager ne sert plus
        teams.Clear();
        RefreshAllCharactersDebug();

        // Détruire le GameObject à la fin du frame
        Destroy(gameObject);
    }

    [ContextMenu("Force End battle")]
    public void ForceEndBattle()
    {
        isBattleEnded = true;
        EndBattle();
    }


    public void AddTeam(BattleTeam team)
    {
        if (team == null)
        {
            Debug.LogError("Cannot add a null team.");
            return;
        }

        if (!teams.Contains(team))
            teams.Add(team);

        RefreshAllCharactersDebug(); // maj debug list
    }

    public bool RemoveTeam(BattleTeam team)
    {
        if (team == null)
        {
            Debug.LogError("Cannot remove a null team.");
            return false;
        }
        bool removed = teams.Remove(team);

        RefreshAllCharactersDebug(); // maj debug list
        return removed;
    }
    private void DrawBattleZoneOutline()
    {
        if (battleZone == null) return;

        if (battleZone is BoxCollider box)
        {
            Vector3 center = transform.position + box.center;
            Vector3 extents = box.size * 0.5f;

            Vector3[] localCorners = new Vector3[4]
            {
            new Vector3(-extents.x, 0f, -extents.z),
            new Vector3(extents.x, 0f, -extents.z),
            new Vector3(extents.x, 0f, extents.z),
            new Vector3(-extents.x, 0f, extents.z),
            };

            Vector3[] worldCorners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                // Position corners at the collider's horizontal plane (ignore Y here)
                Vector3 localPos = box.center + localCorners[i];
                // But for Y, use something reasonable above expected terrain height, e.g. 10 units
                Vector3 rayOrigin = transform.TransformPoint(new Vector3(localPos.x, 10f, localPos.z));
                worldCorners[i] = rayOrigin;
            }

            battleZone.enabled = false;

            int groundLayerMask = 1 << LayerMask.NameToLayer("Default");

            Vector3[] finalCorners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Ray ray = new Ray(worldCorners[i], Vector3.down);
                if (Physics.Raycast(ray, out RaycastHit hit, 50f, groundLayerMask))
                {
                    finalCorners[i] = hit.point + Vector3.up * 0.05f;
                }
                else
                {
                    // If raycast misses, fallback somewhere reasonable, e.g. 0.5f above original Y=0 level
                    finalCorners[i] = transform.TransformPoint(new Vector3(box.center.x + localCorners[i].x, 0.5f, box.center.z + localCorners[i].z));
                }
            }

            battleZone.enabled = true;

            battleZoneLine.positionCount = finalCorners.Length + 1;
            Vector3[] closedCorners = new Vector3[finalCorners.Length + 1];
            finalCorners.CopyTo(closedCorners, 0);
            closedCorners[finalCorners.Length] = finalCorners[0]; // close the loop by returning to the first corner
            battleZoneLine.SetPositions(closedCorners);
        }
    }



    public Bounds GetBattleZoneBounds()
    {
        if (battleZone is BoxCollider box)
        {
            Bounds bounds = new Bounds();
            bounds.center = transform.TransformPoint(box.center);
            bounds.size = Vector3.Scale(box.size, transform.lossyScale);
            return bounds;
        }

        Debug.LogError("Battle zone is not a BoxCollider.");
        return new Bounds(Vector3.zero, Vector3.zero);
    }

    public Vector3 ClampPositionToBattleZone(Vector3 position)
    {
        Bounds bounds = GetBattleZoneBounds();

        float clampedX = Mathf.Clamp(position.x, bounds.min.x, bounds.max.x);
        float clampedY = position.y; // on ne touche pas la hauteur
        float clampedZ = Mathf.Clamp(position.z, bounds.min.z, bounds.max.z);

        return new Vector3(clampedX, clampedY, clampedZ);
    }



}
