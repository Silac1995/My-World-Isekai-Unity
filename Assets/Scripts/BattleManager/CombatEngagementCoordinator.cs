using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CombatEngagementCoordinator
{
    private BattleManager _manager;
    private List<CombatEngagement> _activeEngagements = new List<CombatEngagement>();
    private Dictionary<Character, Character> _targetingGraph = new Dictionary<Character, Character>();

    public IReadOnlyList<CombatEngagement> ActiveEngagements => _activeEngagements;

    public CombatEngagementCoordinator(BattleManager manager)
    {
        _manager = manager;
    }

    // ───────────────────────────────────────────────
    //  Targeting Graph API
    // ───────────────────────────────────────────────

    /// <summary>
    /// Updates the targeting graph when a character changes targets.
    /// Called by BattleManager.SetTargeting, which is invoked from CharacterCombat
    /// and CombatAILogic when intent changes.
    /// </summary>
    public void SetTargeting(Character attacker, Character target)
    {
        if (attacker == null) return;

        if (target == null)
        {
            _targetingGraph.Remove(attacker);
        }
        else
        {
            _targetingGraph[attacker] = target;
        }
    }

    /// <summary>
    /// Removes a character from the targeting graph entirely (both as attacker and as target).
    /// Called when a character dies or leaves battle.
    /// </summary>
    public void RemoveFromGraph(Character character)
    {
        if (character == null) return;

        _targetingGraph.Remove(character);

        // Remove all edges pointing TO this character
        var attackersToUpdate = _targetingGraph
            .Where(kvp => kvp.Value == character)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var attacker in attackersToUpdate)
        {
            _targetingGraph.Remove(attacker);
        }

        LeaveCurrentEngagement(character);
    }

    // ───────────────────────────────────────────────
    //  Core Algorithm — called once per battle tick
    // ───────────────────────────────────────────────

    /// <summary>
    /// Evaluates the targeting graph and reconciles engagements.
    /// Uses Union-Find to build connected components from mutual targeting pairs,
    /// then joins one-way targeters into existing components.
    /// </summary>
    public void EvaluateEngagements()
    {
        // Step 1: Find all mutual pairs (A targets B AND B targets A)
        var mutualPairs = new HashSet<(Character, Character)>();
        foreach (var kvp in _targetingGraph)
        {
            Character a = kvp.Key;
            Character b = kvp.Value;
            if (b != null && _targetingGraph.TryGetValue(b, out Character bTarget) && bTarget == a)
            {
                // Canonical ordering to avoid duplicate pairs
                var pair = a.GetInstanceID() < b.GetInstanceID() ? (a, b) : (b, a);
                mutualPairs.Add(pair);
            }
        }

        // Step 2: Build connected components using Union-Find
        var unionFind = new Dictionary<Character, Character>();

        // Seed with mutual pairs
        foreach (var (a, b) in mutualPairs)
        {
            EnsureInUnionFind(unionFind, a);
            EnsureInUnionFind(unionFind, b);
            Union(unionFind, a, b);
        }

        // Join edges: if X targets someone already in union-find, X joins that component
        foreach (var kvp in _targetingGraph)
        {
            Character attacker = kvp.Key;
            Character target = kvp.Value;
            if (target != null && unionFind.ContainsKey(target))
            {
                EnsureInUnionFind(unionFind, attacker);
                Union(unionFind, attacker, target);
            }
        }

        // Step 3: Collect components by root
        var components = new Dictionary<Character, List<Character>>();
        foreach (var kvp in unionFind)
        {
            Character root = Find(unionFind, kvp.Key);
            if (!components.ContainsKey(root))
                components[root] = new List<Character>();
            components[root].Add(kvp.Key);
        }

        // Step 4: Reconcile computed components against existing engagements
        ReconcileEngagements(components);

        // Step 5: Clean up empty or finished engagements
        _activeEngagements.RemoveAll(e => e.IsFinished());
    }

    // ───────────────────────────────────────────────
    //  Union-Find Helpers
    // ───────────────────────────────────────────────

    private void EnsureInUnionFind(Dictionary<Character, Character> uf, Character c)
    {
        if (!uf.ContainsKey(c))
            uf[c] = c;
    }

    private Character Find(Dictionary<Character, Character> uf, Character c)
    {
        // Path compression
        while (uf[c] != c)
        {
            uf[c] = uf[uf[c]];
            c = uf[c];
        }
        return c;
    }

    private void Union(Dictionary<Character, Character> uf, Character a, Character b)
    {
        Character rootA = Find(uf, a);
        Character rootB = Find(uf, b);
        if (rootA != rootB)
        {
            uf[rootA] = rootB;
        }
    }

    // ───────────────────────────────────────────────
    //  Reconciliation
    // ───────────────────────────────────────────────

    /// <summary>
    /// Compares computed connected components against existing engagements
    /// and creates, merges, or syncs as needed.
    /// </summary>
    private void ReconcileEngagements(Dictionary<Character, List<Character>> components)
    {
        foreach (var kvp in components)
        {
            List<Character> component = kvp.Value;
            if (component.Count == 0) continue;

            // Find all existing engagements that contain at least one member of this component
            var overlapping = new List<CombatEngagement>();
            foreach (var engagement in _activeEngagements)
            {
                bool hasOverlap = component.Any(c =>
                    engagement.GroupA.Members.Contains(c) ||
                    engagement.GroupB.Members.Contains(c));

                if (hasOverlap)
                    overlapping.Add(engagement);
            }

            if (overlapping.Count == 0)
            {
                // No existing engagement — create a new one
                CreateEngagementForComponent(component);
            }
            else if (overlapping.Count == 1)
            {
                // Exactly one existing engagement — sync its members
                SyncEngagementMembers(overlapping[0], component);
            }
            else
            {
                // Multiple existing engagements overlap this component — merge into the largest
                CombatEngagement largest = overlapping.OrderByDescending(e =>
                    e.GroupA.Members.Count + e.GroupB.Members.Count).First();

                foreach (var other in overlapping)
                {
                    if (other == largest) continue;

                    // Move all members from the smaller engagement to the largest
                    var allMembers = other.GroupA.Members.Concat(other.GroupB.Members).ToList();
                    foreach (var member in allMembers)
                    {
                        other.LeaveEngagement(member);
                        largest.JoinEngagement(member);
                    }

                    // Remove the emptied engagement
                    _activeEngagements.Remove(other);
                }

                // Now sync the merged engagement with the full component
                SyncEngagementMembers(largest, component);
            }
        }
    }

    /// <summary>
    /// Creates a new engagement for a connected component of characters.
    /// The component must contain characters from at least two opposing teams.
    /// </summary>
    private void CreateEngagementForComponent(List<Character> component)
    {
        // Determine the two teams present in this component
        BattleTeam teamA = null;
        BattleTeam teamB = null;

        foreach (var character in component)
        {
            BattleTeam team = _manager.GetTeamOf(character);
            if (team == null) continue;

            if (teamA == null)
            {
                teamA = team;
            }
            else if (teamB == null && team != teamA)
            {
                teamB = team;
                break;
            }
        }

        // Need at least two opposing teams to form an engagement
        if (teamA == null || teamB == null) return;

        var engagement = new CombatEngagement(_manager, teamA, teamB);
        foreach (var character in component)
        {
            engagement.JoinEngagement(character);
        }

        // Set anchor point at the midpoint of all characters in the component
        Vector3 midpoint = CalculateMidpoint(component);
        engagement.SetAnchorPoint(midpoint);

        _activeEngagements.Add(engagement);

        Debug.Log($"<color=cyan>[Engagement]</color> New engagement created with {component.Count} characters.");
    }

    /// <summary>
    /// Syncs an existing engagement's members with the computed component.
    /// Adds missing characters and removes characters that no longer belong.
    /// </summary>
    private void SyncEngagementMembers(CombatEngagement engagement, List<Character> component)
    {
        // Add characters that should be in this engagement but aren't
        foreach (var character in component)
        {
            bool inGroupA = engagement.GroupA.Members.Contains(character);
            bool inGroupB = engagement.GroupB.Members.Contains(character);

            if (!inGroupA && !inGroupB)
            {
                engagement.JoinEngagement(character);
            }
        }

        // Remove characters that are in the engagement but not in the component
        var currentMembers = engagement.GroupA.Members
            .Concat(engagement.GroupB.Members)
            .ToList();

        foreach (var member in currentMembers)
        {
            if (!component.Contains(member))
            {
                engagement.LeaveEngagement(member);
            }
        }
    }

    // ───────────────────────────────────────────────
    //  Queries
    // ───────────────────────────────────────────────

    /// <summary>
    /// Returns the engagement a character currently belongs to, or null.
    /// Used by CombatAILogic and CombatTacticalPacer.
    /// </summary>
    public CombatEngagement GetEngagementOf(Character character)
    {
        if (character == null) return null;

        foreach (var engagement in _activeEngagements)
        {
            if (engagement.GroupA.Members.Contains(character) ||
                engagement.GroupB.Members.Contains(character))
            {
                return engagement;
            }
        }

        return null;
    }

    public Character GetBestTargetFor(Character attacker)
    {
        if (attacker == null) return null;

        BattleTeam myTeam = _manager.GetTeamOf(attacker);
        BattleTeam opponentTeam = _manager.GetOpponentTeamOf(attacker);
        if (opponentTeam == null) return null;

        List<Character> aliveEnemies = opponentTeam.CharacterList.Where(e => e != null && e.IsAlive()).ToList();
        if (aliveEnemies.Count == 0) return null;

        List<Character> availableTargets = new List<Character>();
        List<Character> fullTargets = new List<Character>();

        foreach (var enemy in aliveEnemies)
        {
            CombatEngagement enemyEngagement = GetEngagementOf(enemy);

            if (enemyEngagement == null || !enemyEngagement.IsFullFor(myTeam))
            {
                availableTargets.Add(enemy);
            }
            else
            {
                fullTargets.Add(enemy);
            }
        }

        if (availableTargets.Count > 0)
        {
            return GetClosestFromList(attacker.transform.position, availableTargets);
        }

        if (fullTargets.Count > 0)
        {
            return fullTargets[Random.Range(0, fullTargets.Count)];
        }

        return null;
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

    // ───────────────────────────────────────────────
    //  Lifecycle
    // ───────────────────────────────────────────────

    public void LeaveCurrentEngagement(Character attacker)
    {
        foreach (var engagement in _activeEngagements)
        {
            engagement.LeaveEngagement(attacker);
        }
    }

    public void CleanupEngagements()
    {
        _activeEngagements.RemoveAll(e => e.IsFinished());
    }

    public void ClearAll()
    {
        _activeEngagements.Clear();
        _targetingGraph.Clear();
    }

    // ───────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────

    private Vector3 CalculateMidpoint(List<Character> characters)
    {
        if (characters == null || characters.Count == 0) return Vector3.zero;

        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (var character in characters)
        {
            if (character != null)
            {
                sum += character.transform.position;
                count++;
            }
        }

        return count > 0 ? sum / count : Vector3.zero;
    }
}
