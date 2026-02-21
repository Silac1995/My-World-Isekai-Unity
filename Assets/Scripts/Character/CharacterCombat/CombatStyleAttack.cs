using UnityEngine;
using System.Linq;

[RequireComponent(typeof(Collider))]
public class CombatStyleAttack : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private int _maxTargets = 1;
    [SerializeField] private float _damage = 1f;

    [Header("Components")]
    [SerializeField] private Character _character;
    [SerializeField] private CombatStyleSO _combatStyleSO;
    [SerializeField] private Collider _hitCollider;

    private System.Collections.Generic.HashSet<Character> _hitTargets = new System.Collections.Generic.HashSet<Character>();
    private System.Collections.Generic.List<Character> _potentialTargets = new System.Collections.Generic.List<Character>();
    private int _finalMaxTargets;

    public Character Character => _character;
    public CombatStyleSO CombatStyleSO => _combatStyleSO;
    public Collider HitCollider => _hitCollider;

    private float GetDamage()
    {
        if (_character == null || _character.Stats == null || _combatStyleSO == null)
            return _damage;

        float physicalDamage = _character.Stats.PhysicalPower.Value;
        float scalingStatValue = _character.Stats.GetSecondaryStatValue(_combatStyleSO.ScalingStat);
        return physicalDamage + (_combatStyleSO.StatMultiplier * scalingStatValue);
    }

    public void Initialize(Character character, int additionalTargets)
    {
        _character = character;
        _finalMaxTargets = _maxTargets + additionalTargets;
        _hitTargets.Clear();
        _potentialTargets.Clear();

        if (_character != null && _character.Stats != null && _combatStyleSO != null)
        {
            float statValue = _character.Stats.GetSecondaryStatValue(_combatStyleSO.ScalingStat);
            _damage *= (statValue * _combatStyleSO.StatMultiplier);
            
            Debug.Log($"<color=red>[Combat]</color> Dégâts ajustés pour {_character.CharacterName} : {_damage} (Stat: {_combatStyleSO.ScalingStat}={statValue}, Mult: {_combatStyleSO.StatMultiplier})");
        }
    }

    private void Update()
    {
        if (_hitTargets.Count >= _finalMaxTargets || _potentialTargets.Count == 0) return;

        // --- TRI PAR PRIORITÉ ET DISTANCE ---
        _potentialTargets.Sort((a, b) => 
        {
            bool aIsOpponent = false;
            bool bIsOpponent = false;

            if (_character != null && _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle)
            {
                var bm = _character.CharacterCombat.CurrentBattleManager;
                aIsOpponent = bm.AreOpponents(_character, a);
                bIsOpponent = bm.AreOpponents(_character, b);
            }

            // Priorité aux opposants r?els dans la bataille
            if (aIsOpponent && !bIsOpponent) return -1;
            if (!aIsOpponent && bIsOpponent) return 1;

            // Secondaire : Distance
            float distA = Vector3.Distance(_character.transform.position, a.transform.position);
            float distB = Vector3.Distance(_character.transform.position, b.transform.position);
            return distA.CompareTo(distB);
        });

        for (int i = 0; i < _potentialTargets.Count; i++)
        {
            Character target = _potentialTargets[i];

            if (target == null || _hitTargets.Contains(target)) continue;

            float damage = GetDamage() * Random.Range(0.7f, 1.3f);

            if (_character.CharacterCombat != null && !_character.CharacterCombat.IsInBattle)
            {
                damage *= 0.2f;
            }

            _hitTargets.Add(target);
            target.CharacterCombat.TakeDamage(damage, _combatStyleSO != null ? _combatStyleSO.DamageType : MeleeDamageType.Blunt);

            if (_character.CharacterCombat != null && !_character.CharacterCombat.IsInBattle)
            {
                _character.CharacterCombat.StartFight(target);
            }

            Debug.Log($"<color=red>[Combat]</color> {_character.CharacterName} a frappé {target.CharacterName} (Priorité: {(_character.CharacterCombat.IsInBattle ? _character.CharacterCombat.CurrentBattleManager.AreOpponents(_character, target) : "N/A")}) pour {damage} dégâts.");

            if (_hitTargets.Count >= _finalMaxTargets) break;
        }

        _potentialTargets.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        Character target = other.GetComponentInParent<Character>();
        
        if (target == null) return;
        if (target == _character) return;
        if (!target.IsAlive()) return;
        if (_hitTargets.Contains(target) || _potentialTargets.Contains(target)) return;

        _potentialTargets.Add(target);
    }
}
