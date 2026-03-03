using UnityEngine;

/// <summary>
/// Action d'attaque à distance. Gère le chargement (arc) ou la vérification
/// des munitions (chargeur), puis tire un projectile.
/// </summary>
public class CharacterRangedAttackAction : CharacterCombatAction
{
    private readonly RangedCombatStyleSO _rangedStyle;
    private readonly Character _target;

    public CharacterRangedAttackAction(Character character, Character target, RangedCombatStyleSO rangedStyle) 
        : base(character, 0.8f)
    {
        _rangedStyle = rangedStyle;
        _target = target;

        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            // TODO: Ajouter une méthode GetRangedAttackDuration quand l'animation sera prête
            this.Duration = 1.0f;
        }
    }

    public override void OnStart()
    {
        base.OnStart();

        // TODO: Jouer l'animation de tir quand elle sera prête
        // animHandler.PlayRangedAttack();

        character.CharacterSpeech?.Say("Take this!");
    }

    public override void OnApplyEffect()
    {
        base.OnApplyEffect();
        SpawnProjectile();
    }

    private void SpawnProjectile()
    {
        if (_rangedStyle == null || _rangedStyle.ProjectilePrefab == null) return;
        if (_target == null || !_target.IsAlive()) return;

        // Direction vers la cible
        Vector3 direction = (_target.transform.position - character.transform.position).normalized;
        direction.y = 0;

        // Position de spawn
        Vector3 spawnPos = character.transform.position;
        if (character.CharacterVisual != null)
        {
            Vector3 facingDir = character.CharacterVisual.IsFacingRight ? Vector3.right : Vector3.left;
            spawnPos = character.CharacterVisual.GetVisualExtremity(facingDir);
            spawnPos.z = character.transform.position.z;
        }

        // Calcul des dégâts
        float damage = _rangedStyle.BaseDamage;
        if (character.Stats != null)
        {
            float physicalDamage = character.Stats.PhysicalPower.Value * _rangedStyle.PhysicalPowerPercentage;
            float scalingStatValue = character.Stats.GetSecondaryStatValue(_rangedStyle.ScalingStat);
            damage = physicalDamage + _rangedStyle.BaseDamage + (_rangedStyle.StatMultiplier * scalingStatValue);
        }

        // DamageType : arme > style (fallback)
        DamageType damageType = _rangedStyle.DamageType;
        var weapon = character.CharacterEquipment?.CurrentWeapon;
        if (weapon != null && weapon.ItemSO is WeaponSO weaponSO)
        {
            damageType = weaponSO.DamageType;
        }

        // Spawn du projectile
        GameObject projectileGo = Object.Instantiate(_rangedStyle.ProjectilePrefab, spawnPos, Quaternion.identity);
        var projectile = projectileGo.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.Initialize(character, damage, damageType, _rangedStyle.KnockbackForce, direction, _rangedStyle.ProjectileSpeed);
        }
    }
}
