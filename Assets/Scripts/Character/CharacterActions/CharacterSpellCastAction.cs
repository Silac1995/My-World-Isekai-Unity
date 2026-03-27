using UnityEngine;

/// <summary>
/// CharacterAction for casting spells (Mana cost, cooldown, Dexterity-scaled cast time).
/// Duration = computed cast time. If instant (0), OnApplyEffect fires immediately.
/// </summary>
public class CharacterSpellCastAction : CharacterCombatAction
{
    private readonly SpellInstance _spell;
    private readonly Character _target;
    private bool _manaConsumed;

    public SpellInstance Spell => _spell;

    public CharacterSpellCastAction(Character character, SpellInstance spell, Character target)
        : base(character, spell.ComputeCastTime())
    {
        _spell = spell;
        _target = target;
        _manaConsumed = false;
    }

    public override bool CanExecute()
    {
        if (_spell == null) return false;
        return _spell.CanUse(_target);
    }

    public override void OnStart()
    {
        base.OnStart();

        // Server: consume mana upfront
        if (character.IsServer && character.Stats?.Mana != null)
        {
            character.Stats.Mana.DecreaseCurrentAmount(_spell.SpellData.ManaCost);
            _manaConsumed = true;
        }

        // TODO: Play cast animation when available
        // var animHandler = character.CharacterVisual?.CharacterAnimator;
        // animHandler?.PlayCastSpell();

        // Spawn cast VFX if available
        if (_spell.Data.VisualPrefab != null && character.CharacterVisual != null)
        {
            Object.Instantiate(_spell.Data.VisualPrefab, character.transform.position, Quaternion.identity);
        }
    }

    public override void OnApplyEffect()
    {
        base.OnApplyEffect();

        if (!character.IsServer) return;

        var spellData = _spell.SpellData;

        // Start cooldown
        _spell.StartCooldown();

        // Apply self status effects
        if (_spell.Data.StatusEffectsOnSelf != null)
        {
            foreach (var effect in _spell.Data.StatusEffectsOnSelf)
            {
                if (effect != null)
                    character.StatusManager?.ApplyEffect(effect, character);
            }
        }

        // Determine spell execution based on type
        if (spellData.IsProjectile)
        {
            SpawnSpellProjectile(spellData);
        }
        else
        {
            ApplyDirectSpellEffect(spellData);
        }
    }

    public override void OnCancel()
    {
        base.OnCancel();

        // Refund mana if spell was interrupted before OnApplyEffect resolved
        if (_manaConsumed && character.IsServer && character.Stats?.Mana != null)
        {
            character.Stats.Mana.IncreaseCurrentAmount(_spell.SpellData.ManaCost);
            _manaConsumed = false;
            Debug.Log($"[SpellCast] {character.CharacterName} spell '{_spell.SpellData.AbilityName}' interrupted — mana refunded.");
        }
    }

    private void SpawnSpellProjectile(SpellSO spellData)
    {
        if (spellData.ProjectilePrefab == null || _target == null || !_target.IsAlive()) return;

        Vector3 direction = (_target.transform.position - character.transform.position).normalized;
        direction.y = 0;

        Vector3 spawnPos = character.transform.position;
        if (character.CharacterVisual != null)
        {
            Vector3 facingDir = character.CharacterVisual.IsFacingRight ? Vector3.right : Vector3.left;
            spawnPos = character.CharacterVisual.GetVisualExtremity(facingDir);
            spawnPos.z = character.transform.position.z;
        }

        float damage = CalculateSpellDamage(spellData);
        DamageType damageType = spellData.SpellDamageType;

        GameObject projectileGo = Object.Instantiate(spellData.ProjectilePrefab, spawnPos, Quaternion.identity);
        var projectile = projectileGo.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.Initialize(character, damage, damageType, 0f, direction, spellData.ProjectileSpeed);
        }
    }

    private void ApplyDirectSpellEffect(SpellSO spellData)
    {
        // Apply damage to target if this is a damage spell
        if (spellData.BaseDamage > 0f && _target != null && _target.IsAlive())
        {
            float damage = CalculateSpellDamage(spellData);

            // AoE: damage all enemies in radius
            if (spellData.AoeRadius > 0f)
            {
                ApplyAoEDamage(spellData, damage);
            }
            else
            {
                _target.CharacterCombat?.TakeDamage(damage, spellData.SpellDamageType, character);
            }
        }

        // Apply status effects on target
        if (_spell.Data.StatusEffectsOnTarget != null)
        {
            Character effectTarget = ResolveTarget();
            if (effectTarget != null)
            {
                foreach (var effect in _spell.Data.StatusEffectsOnTarget)
                {
                    if (effect != null)
                        effectTarget.StatusManager?.ApplyEffect(effect, character);
                }
            }
        }
    }

    private void ApplyAoEDamage(SpellSO spellData, float damage)
    {
        Vector3 center = _target != null ? _target.transform.position : character.transform.position;
        Collider[] hits = Physics.OverlapSphere(center, spellData.AoeRadius);

        foreach (var hit in hits)
        {
            Character hitChar = hit.GetComponent<Character>();
            if (hitChar == null || hitChar == character || !hitChar.IsAlive()) continue;

            // Only damage enemies (characters on the opposite team)
            if (character.CharacterCombat?.CurrentBattleManager != null)
            {
                var battleManager = character.CharacterCombat.CurrentBattleManager;
                if (!battleManager.AreOpponents(character, hitChar)) continue;
            }

            hitChar.CharacterCombat?.TakeDamage(damage, spellData.SpellDamageType, character);
        }
    }

    private float CalculateSpellDamage(SpellSO spellData)
    {
        float baseDamage = spellData.BaseDamage;

        if (character.Stats != null)
        {
            float magicalPower = character.Stats.MagicalPower?.CurrentValue ?? 0f;
            float scalingValue = character.Stats.GetSecondaryStatValue(spellData.ScalingStat);
            baseDamage += magicalPower * 0.3f + scalingValue * spellData.StatMultiplier;
        }

        // Apply variance
        return baseDamage * Random.Range(0.85f, 1.15f);
    }

    private Character ResolveTarget()
    {
        return _spell.Data.TargetType switch
        {
            AbilityTargetType.Self => character,
            AbilityTargetType.SingleAlly => _target ?? character,
            _ => _target
        };
    }
}
