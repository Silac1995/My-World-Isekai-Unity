using UnityEngine;

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

        // Application du multiplicateur de stat si disponible
        if (_character != null && _character.Stats != null && _combatStyleSO != null)
        {
            float statValue = _character.Stats.GetSecondaryStatValue(_combatStyleSO.ScalingStat);
            _damage *= (statValue * _combatStyleSO.StatMultiplier);
            
            Debug.Log($"<color=red>[Combat]</color> Dégâts ajustés pour {_character.CharacterName} : {_damage} (Stat: {_combatStyleSO.ScalingStat}={statValue}, Mult: {_combatStyleSO.StatMultiplier})");
        }
    }

    private void Update()
    {
        // Si on n'a plus de place pour des cibles ou personne en vue, on sort
        if (_hitTargets.Count >= _finalMaxTargets || _potentialTargets.Count == 0) return;

        // On trie les cibles potentielles par distance par rapport au lanceur de l'attaque
        _potentialTargets.Sort((a, b) => 
            Vector3.Distance(_character.transform.position, a.transform.position)
            .CompareTo(Vector3.Distance(_character.transform.position, b.transform.position))
        );

        // On traite les cibles dans l'ordre de proximité
        for (int i = 0; i < _potentialTargets.Count; i++)
        {
            Character target = _potentialTargets[i];

            if (target == null || _hitTargets.Contains(target)) continue;

            // Application des dégâts: Physical Power + (StatMultiplier * ScalingStat)
            float damage = GetDamage();
            _hitTargets.Add(target);
            target.CharacterCombat.TakeDamage(damage);

            // --- AUTO-COMBAT ---
            // Si le lanceur n'est pas déjà en combat, on initie le combat automatiquement avec la première cible frappée
            if (_character.CharacterCombat != null && !_character.CharacterCombat.IsInBattle)
            {
                _character.CharacterCombat.StartFight(target);
            }

            Debug.Log($"<color=red>[Combat]</color> {_character.CharacterName} a frappé {target.CharacterName} (PROXIMITÉ) pour {damage} dégâts.");

            // Si on a atteint la limite après cet ajout, on arrête tout
            if (_hitTargets.Count >= _finalMaxTargets) break;
        }

        // On vide la liste des potentiels pour ne pas les retraiter
        _potentialTargets.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        // On récupère le Character sur l'objet touché
        Character target = other.GetComponentInParent<Character>();
        
        // Validations de base
        if (target == null) return;
        if (target == _character) return;
        if (!target.IsAlive()) return;
        if (_hitTargets.Contains(target) || _potentialTargets.Contains(target)) return;

        // On l'ajoute aux potentiels pour tri par distance dans l'Update
        _potentialTargets.Add(target);
    }
}

