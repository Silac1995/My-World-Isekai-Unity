using System.Collections.Generic;
using UnityEngine;

public class BattleManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private List<BattleTeam> _teams = new List<BattleTeam>();
    [SerializeField] private Collider _battleZone;
    [SerializeField] private LineRenderer _battleZoneLine;
    [SerializeField] private float _padding = 10f;

    private bool _isBattleEnded = false;

    // Liste pour le debug (plus besoin de GetAllCharacters à chaque fois)
    [SerializeField] private List<Character> _allParticipants = new List<Character>();

    public List<BattleTeam> BattleTeams => _teams;
    public bool IsBattleEnded => _isBattleEnded;

    public void Initialize(Character initiator, Character target)
    {
        if (initiator == null || target == null) return;

        // 1. Setup des équipes
        BattleTeam teamA = new BattleTeam();
        BattleTeam teamB = new BattleTeam();
        teamA.AddCharacter(initiator);
        teamB.AddCharacter(target);

        _teams.Add(teamA);
        _teams.Add(teamB);

        // 2. Création physique de la zone
        CreateBattleZone(initiator, target);

        // 3. Inscription des participants
        RegisterParticipants();

        // 4. Rendu visuel UNIQUE (pas dans Update)
        DrawBattleZoneOutline();

        Debug.Log($"<color=orange>[Battle]</color> Combat lancé : {initiator.name} vs {target.name}");
    }

    private void CreateBattleZone(Character a, Character b)
    {
        Bounds combinedBounds = a.Collider.bounds;
        combinedBounds.Encapsulate(b.Collider.bounds);
        combinedBounds.Expand(_padding);

        // On utilise un BoxCollider pour les limites
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(combinedBounds.size.x, 20f, combinedBounds.size.z);
        transform.position = new Vector3(combinedBounds.center.x, a.transform.position.y, combinedBounds.center.z);

        _battleZone = box;
    }

    private void RegisterParticipants()
    {
        _allParticipants.Clear();

        for (int i = 0; i < _teams.Count; i++)
        {
            BattleTeam currentTeam = _teams[i];
            // L'adversaire est l'autre équipe (0 -> 1, 1 -> 0)
            BattleTeam opponentTeam = _teams[(i + 1) % _teams.Count];

            foreach (var character in currentTeam.CharacterList)
            {
                if (character == null) continue;

                _allParticipants.Add(character);

                if (character.CharacterCombat != null)
                {
                    character.CharacterCombat.JoinBattle(this);

                    // On ne force pas le comportement de combat si c'est le Joueur
                    if (!character.IsPlayer())
                    {
                        Character randomEnemy = opponentTeam.GetRandomMember();
                        if (randomEnemy != null)
                        {
                            character.Controller.PushBehaviour(new CombatBehaviour(this, randomEnemy));
                        }
                    }

                    Debug.Log($"<color=white>[Battle]</color> {character.CharacterName} a rejoint le combat.");
                }

                // Gestion de la mort pour vérifier la fin du combat
                character.OnDeath -= HandleCharacterDeath;
                character.OnDeath += HandleCharacterDeath;
            }
        }
    }

    private void HandleCharacterDeath(Character deadCharacter)
    {
        if (_isBattleEnded) return;

        // Vérification de victoire
        foreach (var team in _teams)
        {
            if (team.IsTeamEliminated())
            {
                _isBattleEnded = true;
                EndBattle();
                return;
            }
        }
    }

    public void EndBattle()
    {
        foreach (var character in _allParticipants)
        {
            if (character != null)
            {
                character.OnDeath -= HandleCharacterDeath; // UNSUBSCRIBE CRITIQUE
                character.CharacterCombat.LeaveBattle();
            }
        }

        Debug.Log("<color=red>[Battle]</color> Fin du combat.");
        Destroy(gameObject);
    }

    // Garde ton DrawBattleZoneOutline mais enlève-le de l'Update !
    public void DrawBattleZoneOutline()
    {
        if (_battleZoneLine == null || _battleZone == null) return;

        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        // Configuration de base du LineRenderer
        _battleZoneLine.useWorldSpace = true;
        _battleZoneLine.loop = true;
        _battleZoneLine.positionCount = 4;

        // Calcul des coins basés sur le BoxCollider
        Vector3 center = box.transform.position;
        Vector3 size = box.size;

        // On trace un rectangle au niveau du sol (y)
        float x = size.x / 2f;
        float z = size.z / 2f;
        float y = center.y; // Ajuste légèrement (+0.05f) si la ligne clignote avec le sol

        Vector3[] corners = new Vector3[4]
        {
        center + new Vector3(-x, 0, -z),
        center + new Vector3(x, 0, -z),
        center + new Vector3(x, 0, z),
        center + new Vector3(-x, 0, z)
        };

        _battleZoneLine.SetPositions(corners);

        Debug.Log("<color=cyan>[Battle]</color> Outline de combat dessinée au sol.");
    }
}