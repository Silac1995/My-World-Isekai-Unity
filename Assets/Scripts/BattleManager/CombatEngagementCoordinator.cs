using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CombatEngagementCoordinator
{
    private BattleManager _manager;
    private List<CombatEngagement> _activeEngagements = new List<CombatEngagement>();

    public IReadOnlyList<CombatEngagement> ActiveEngagements => _activeEngagements;

    public CombatEngagementCoordinator(BattleManager manager)
    {
        _manager = manager;
    }

    public void ClearAll()
    {
        _activeEngagements.Clear();
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

        if (availableTargets.Count > 0)
        {
            return GetClosestFromList(attacker.transform.position, availableTargets);
        }

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

    public CombatEngagement RequestEngagement(Character attacker, Character target)
    {
        if (target == null || attacker == null) return null;

        LeaveCurrentEngagement(attacker);

        CombatEngagement engagement = _activeEngagements.Find(e =>
            e.GroupA.Members.Contains(target) || e.GroupB.Members.Contains(target)
        );

        BattleTeam attackerTeam = _manager.GetTeamOf(attacker);
        BattleTeam targetTeam = _manager.GetTeamOf(target);

        if (engagement == null)
        {
            float mergeDistance = 10f;
            float bestDist = float.MaxValue;

            foreach (var existing in _activeEngagements)
            {
                if (existing.TeamA != targetTeam && existing.TeamB != targetTeam) continue;
                if (existing.IsFullFor(attackerTeam)) continue;

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

        if (engagement == null)
        {
            // Only create NEW engagements between opponents — allies can join existing ones above
            if (!_manager.AreOpponents(attacker, target)) return null;

            if (targetTeam != null && attackerTeam != null)
            {
                engagement = new CombatEngagement(_manager, targetTeam, attackerTeam);
                engagement.JoinEngagement(target);
                _activeEngagements.Add(engagement);
            }
        }

        if (engagement != null)
        {
            engagement.JoinEngagement(attacker);
            engagement.JoinEngagement(target);

            if (engagement.NeedsSplit())
            {
                SplitEngagement(engagement);
            }
        }

        return engagement;
    }

    private void SplitEngagement(CombatEngagement originalEngagement)
    {
        if (originalEngagement == null) return;

        CombatEngagement newEngagement = new CombatEngagement(_manager, originalEngagement.TeamA, originalEngagement.TeamB);
        _activeEngagements.Add(newEngagement);

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

        ForceRetarget(newEngagement);
        ForceRetarget(originalEngagement);
    }

    private void ForceRetarget(CombatEngagement engagement) { }

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
}
