---
name: combat-system
description: Architecture, flow, and integration of the combat system (BattleManager, CharacterCombat, Initiative, Stats).
---

# Combat System

This skill details the architecture of the combat system in the project and the rules to follow when extending or debugging it. The combat system relies on concepts like **Initiative Ticks**, **Engagement Groups**, and strict role separation between the global Manager and local components.

## When to use this skill
- To add a new combat-related feature (e.g., AoE attack, fleeing, new combat buffs/debuffs).
- To interact with characters' attack delay (`Initiative`).
- In case of bugs where combat doesn't end, or a character is frozen without attacking.
- When adding or modifying combat-related statistics (in `CharacterStats`).

## How to use it

### 1. The BattleManager (Global Management)
The `BattleManager` is the supreme entity of a battle, usually instantiated when a clash begins. It strictly delegates its responsibilities to adhere to SOLID principles:
- **BattleTeams**: It always maintains two teams (Initiator vs Target). We do *not* support 3-team free-for-alls in a single instance.
- **BattleZoneController**: A delegated pure C# class that handles the physical terrain. It dynamically generates the boundary (`BoxCollider` isTrigger), pathfinding deterrent (`NavMeshModifierVolume`), and visual `LineRenderer` to mark the combat zone.
- **CombatEngagementCoordinator**: A delegated pure C# class that manages brawl "subgroups" (`_activeEngagements`) using a **targeting-graph algorithm**. It maintains a `_targetingGraph` (`Dictionary<Character, Character>`) tracking who targets whom. Each battle tick, `EvaluateEngagements()` runs the full algorithm. Key rules:
  - **FORM**: Mutual targeting pairs (A→B and B→A) seed Union-Find components.
  - **JOIN**: One-way targeters join the component of their target (only if the target is already in a component). One-way edges **do not bridge** separate mutual-pair groups — this prevents mega-blob engagements.
  - **SPLIT**: If a mutual subgroup within an engagement re-targets elsewhere, it separates into its own engagement.
  - **FOLLOW**: During splits, characters follow their current target into the new engagement.
  - **RECONCILE**: Compare components against existing engagements — create new, sync members, or merge overlapping.
  - **CLEAN**: Remove characters no longer in any component; prune empty engagements.
  - **Spatial separation**: Engagement anchors repel each other when closer than 12 units apart.
  - Key API: `SetTargeting(attacker, target)` updates the graph, `RemoveFromGraph(character)` cleans up on death/leave (removes both outgoing and incoming edges), `GetEngagementOf(character)` queries current engagement, `GetTargetOf(character)` returns their current graph target, `GetBestTargetFor(attacker)` finds the optimal target (closest in non-full engagement, with **self-targeting prevention**).
- **CombatFormation (Organic Positioning)**: Each `EngagementGroup` owns a `CombatFormation` that calculates dynamic positions based on character role. Melee fighters position at `MELEE_PREFERRED_DISTANCE = 4m` from the opponent center, ranged at `RANGED_MIN_DISTANCE = 8m`. Same-role allies spread along the Z axis with configurable spacing (`MELEE_SPACING = 2.5`, `RANGED_SPACING = 2.0`). There are no fixed ring slots — positions are recomputed each call to `GetOrganicPosition()`. Role detection uses `CombatStyleExpertise.Style is RangedCombatStyleSO`. Deterministic per-character jitter (based on `GetInstanceID()`) prevents stacking. `CombatEngagement.GetAssignedPosition()` delegates to the group's formation with team side sign (-1 for GroupA, +1 for GroupB).
- **Victory Condition**: The manager continuously polls `.IsTeamEliminated()` in its `Update()` loop. This physical guarantee ensures the battle definitively ends if an entire team is wiped out or silently despawned, rather than relying exclusively on volatile event triggers.
- **Incapacitation Handling**: When a character faints/dies, `HandleCharacterIncapacitated` calls `RedirectIncapacitated` which removes the victim from the targeting graph via `RemoveFromGraph` (clears both outgoing and incoming edges, plus leaves engagement) before running `CleanupEngagements`. All characters (including players) are auto-targeted via `SetTargeting` when they join the battle — no `IsPlayer()` guard.
- **Battle Initialization**: `SeedMutualTargeting` runs at battle init to create immediate mutual targeting pairs between opposing teams. Mid-battle joins also seed mutual targeting via `AddParticipantInternal`. This ensures engagements form immediately on battle start.
- **Robust Teardown**: Upon ending, the manager wraps `LeaveBattle` calls in a `try-catch` block to quarantine aggressive UI exceptions (like `PlayerUI` crashing) from aborting the shutdown script. It also unsubscribes all character events explicitly in `OnDestroy()` to prevent zombie memory leaks. `LeaveBattle` and `ForceExitCombatMode` clear `PlannedAction`, `PlannedTarget`, and look target to fully reset combat state.
- **Tick System**: It is the `BattleManager` that sets the pace (`PerformBattleTick()`), and *not the Update method of each character*.

### 2. CharacterCombat (Local Logic)
This is the component every NPC/Player has in order to fight.
- **Combat Mode**: A character switches to "CombatMode" (and draws their weapon) if they intend to attack. There is a `COMBAT_MODE_TIMEOUT` (default 7 seconds).
- **Consumption & Initiative Tick**:
  - The `.IsReadyToAct` method checks if the Initiative (in Stats) is full.
  - The `.ConsumeInitiative()` method resets initiative to 0 after a successful attack.
  - The `.UpdateInitiativeTick(amount)` method is **called by the BattleManager** to fill the bar.
- **Unified Targeting (`SetPlannedTarget` is the SINGLE entry point)**: All target changes route through `SetPlannedTarget(Character)`, which performs: (1) update `CharacterVisual.SetLookTarget`, (2) update `BattleManager.SetTargeting()` in the targeting graph, (3) `EvaluateEngagements()`, (4) issue immediate movement command toward new target. Self-targeting is prevented. All callers route through this:
  - `SetActionIntent(Action, target)` routes through `SetPlannedTarget`
  - `BTAction_Combat` routes through `SetPlannedTarget`
  - `RegisterCharacter` routes through `SetPlannedTarget`
  - `SeedMutualTargeting` uses `SetPlannedTarget`
  - UI click/TAB targeting calls `SetPlannedTarget` to redirect without cancelling queued attacks.
- **Action Intent & Execution (`CombatAILogic.cs`)**: Actions are no longer executed blindly by UI buttons or Behaviour Trees.
  - **`SetActionIntent(Action, target)`** logs what the character *intends* to do. It sets `PlannedAction` and routes the target through `SetPlannedTarget`.
  - **`CombatAILogic.Tick(target)`** is the shared brain for both Players and NPCs. At the top of each tick, it checks `CharacterCombat.PlannedTarget` — if set and alive, it **overrides** the coordinator-assigned `currentTarget` so that player click/TAB selection is respected by all movement and execution logic. It uses `GetEffectiveAttackRange()` which returns `RangedRange` for ranged characters and `MeleeRange` for melee, ensuring ranged characters use their actual weapon range for all approach calculations.
  - **Side-view range check**: This is a side-view game. Melee range uses **X distance only** (`dx <= attackRange && dx >= 1.0`). Z alignment requires `zDist <= 1.5` for melee; ranged bypasses Z alignment entirely. Melee approach sets stagger Z = 0 (match target depth lane for hitbox alignment).
  - **Ranged approach behavior**: Ranged characters (detected via `IsRangedCharacter()` — checks `Style is RangedCombatStyleSO`) that are already within weapon range skip the approach phase entirely and proceed straight to execution. They also bypass the Z-alignment requirement since projectiles handle targeting. Ranged characters **never flee reactively** — they hold ground when melee enemies approach and only reposition after their own attack turn.
  - The attack closure uses **dynamic PlannedTarget evaluation**: `() => Attack(_characterCombat.PlannedTarget)` rather than capturing a fixed target. This ensures retargeting after queuing is respected.
  - **Target change detection**: `ForceImmediateReposition()` is called when the target changes, resetting the tactical pacer throttle and drift timer for instant response.
  - **Stuck reposition**: A 2-second timeout detects when a character is in position but cannot execute (e.g., initiative not ready). After timeout, forces a reposition to prevent standing still indefinitely.
  - While waiting for Initiative, `CombatAILogic` Phase 3 queries `CombatTacticalPacer.GetTacticalDestination(target, attackRange, engagement, isCharging)` for dynamic movement. After a successful action execution in Phase 2, it calls `NotifyAttackCompleted()` and `ResetSwayCenter()` on the pacer to trigger melee step-back.
  - For standard hits, NPCs automatically pull intents (and register them in Phase 1) when ready. Players strictly declare intents via `UI_CombatActionMenu`.
  - **BTAction_Combat integration**: When the blackboard target is null, queries `GetBestTargetFor()` for a fallback target. Routes all targeting through `SetPlannedTarget`.
  - **PlayerCombatCommand exit**: Exits when `IsInBattle` becomes false, using `ResetPath + Resume` (not `Stop`) to allow post-battle movement.

### 3. Weapons & Styles (3-Layer Architecture)

The system is split into three distinct layers to separate static data, runtime state, and fight mechanics.

#### A. Static Data (`WeaponSO` & `CombatStyleSO`)
- **`WeaponSO`**: Defines the item properties.
  - `WeaponCategory` (Melee vs Ranged).
  - `DamageType` (Slashing, Piercing, Blunt). *This is the primary source of damage type.*
  - Max stats (`MaxDurability`, `MaxSharpness`, `MagazineSize`).
- **`CombatStyleSO`**: Defines how the character fights.
  - **Hierarchy**: `MeleeCombatStyleSO` (hitbox-based) vs `RangedCombatStyleSO` (projectile-based).
  - **Ranged Subtypes**: `ChargingRangedCombatStyleSO` (Bow) vs `MagazineRangedCombatStyleSO` (Gun/Crossbow).
  - Defines `MeleeRange`, `ScalingStat`, and `KnockbackForce`.

#### B. Runtime State (`WeaponInstance`)
Every equipped weapon has a specialized instance class to track its wear and tear.
- **`MeleeWeaponInstance`**: Tracks `Sharpness`. High sharpness grants bonuses (impl. pending), low sharpness might require sharpening at a forge.
- **`RangedWeaponInstance`**:
  - `ChargingWeaponInstance`: Tracks `ChargeProgress`. Must be 100% to fire.
  - `MagazineWeaponInstance`: Tracks `CurrentAmmo`. Requires a `Reload()` action when empty.

#### C. Combat Actions (`CharacterAction`)
The actual implementation of the attack.
- **`CharacterMeleeAttackAction`**: Triggers animator, spawns a `CombatStyleAttack` (hitbox) via Animation Event.
- **`CharacterRangedAttackAction`**: Spawns a `Projectile` towards the target.

> [!NOTE]
> All combat actions must override **`ShouldPlayGenericActionAnimation`** to return **`false`**. This prevents the generic "busy" animation from overriding the specific `MeleeAttack` or `RangedAttack` triggers managed by the `CharacterAnimator`.

### 4. Damage Resolution Rules
1. **Damage Type**: Always check `WeaponSO.DamageType` first. If no weapon is equipped, use `CombatStyleSO.DamageType` (fallback for barehands, usually Blunt).
2. **Formula**: `PhysicalPower (from Stats) * Style.PhysicalPowerPercentage + Style.BaseDamage + (ScalingStatValue * Style.StatMultiplier)`.
3. **Projectiles**: Use the `Projectile.cs` script. They are physical objects (`Rigidbody`) that apply damage and knockback on `OnTriggerEnter`.

### 5. CharacterStats (Stat Distribution)
Combat massively relies on `CharacterStats`. It is critical to respect its architecture:
- **Primary Stats**: Dynamic (Health, Stamina, Mana, **Initiative**). 
- **Secondary Stats**: Base characteristics (Strength, Agility, Dexterity, Intelligence, Endurance, Charisma).
- **Tertiary Stats**: Derived from secondary ones (PhysicalPower, MoveSpeed, DodgeChance, CriticalHitChance, etc.).

### 6. Combat Progression (XP & Leveling)
Combat directly drives character progression via the `CharacterCombatLevel` component (a `CharacterSystem`).
- **Centralized XP**: Experience is strictly awarded inside `CharacterCombat.TakeDamage()` to centralize standard hits, DoTs, and spells.
- **Proportional EXP Acquisition**: Instead of flat per-hit XP, a target yields EXP proportionally to the exact amount of HP depleted relative to their MaxHP. Each character has a `BaseExpYield` (e.g., 50). Stripping 10% of their MAX HP instantly rewards 10% of their `BaseExpYield`. The system explicitly calculates `hpBefore - hpAfter` to prevent rewarding excessive EXP when executing an enemy with 1 HP left.
- **Kill Bonus**: A minor +10% yield bonus is awarded for landing the killing blow.
- **Dynamic Balancing (`CalculateCombatExp`)**:
  - **Boost**: Hitting a target with a *higher* level grants up to a **+50%** multiplier (caps at 10 level difference).
  - **Malus**: Hitting a target with a *lower* level implies a penalty up to **-75%** multiplier (caps at 10 level difference).
- **Leveling Up**: Accumulating enough XP (scaling by 50 per level) automatically triggers `LevelUp()`. This logs a `CombatLevelEntry` to history, grants `_statPointsPerLevel` (default 5) as `_unassignedStatPoints` for the player/AI to distribute later, and **instantly heals the character for 30% of their Max HP**.
  - **Player Allocation**: Manual via `SpendStatPoint()` in UI. Only Secondary Stats (Strength, Agility, Dexterity, Intelligence, Endurance, Charisma) are directly upgradeable.
  - **NPC Allocation**: Handled internally via `AutoAllocateStats()`. They randomly reinvest all unspent attribute points evenly across the 6 core Secondary Stats to scale dynamically with the player.

### 7. Targeting & Visual Feedback
- **Unified Click/TAB Targeting**: Both paths converge through `UI_PlayerTargeting.SelectInteractable()`. Click uses `ResolveInteractableFromHit(Collider)` to extract an `InteractableObject` from the raycast hit; TAB cycles through nearby targets. Both call `SelectInteractable` which sets `LookTarget` (always to the **root Character transform**, not the child `CharacterInteractable` transform) and, during battle, calls `CharacterCombat.SetPlannedTarget()`.
- **Battle Target Lock**: During battle, `SelectInteractable` rejects non-battle participants (characters not in the battle and non-character interactables). `ClearSelection` redirects to the current `PlannedTarget` (or `GetBestTargetFor` fallback) instead of clearing the indicator.
- **Dead Target Auto-Retarget**: `UI_CombatActionMenu.OnAttackClicked` validates that `PlannedTarget` is alive and in the battle. If dead or not a participant, falls back to `GetBestTargetFor`. When `PlayerCombatCommand` finishes (target dies), `PlayerController`'s auto-trigger block creates a new command with the next best target and syncs the indicator.
- **CharacterInteractable Access**: Always use the `Character.CharacterInteractable` facade property (never `GetComponent<CharacterInteractable>()`) because CharacterInteractable lives on a child GameObject per the Facade + Child Hierarchy pattern.
- **Shader-Driven Target Indicator**: Active target UI elements (like the crosshair ring) must dynamically lerp their colors (Green -> Yellow -> Red) based on the target's missing health (`Stats.Health.OnAmountChanged`). Obeying the strict **Shader-First** rule, this must be pushed exclusively through `Material.SetFloat("_HealthPercent")` on a custom unlit UI shader to avoid CPU color manipulation and prevent Canvas batching breaks.

### 8. IDamageable Interface & Destructible Objects

The combat system supports damaging non-Character objects (e.g., doors) via the `IDamageable` interface (`Assets/Scripts/Combat/IDamageable.cs`):
- `void TakeDamage(float damage, Character attacker)`
- `bool CanBeDamaged()`

**CombatStyleAttack integration** (`Assets/Scripts/Character/CharacterCombat/CombatStyleAttack.cs`):
- `OnTriggerEnter()` first checks for `Character` (standard combat). If no Character is found, it falls back to checking for `IDamageable` on the collider's GameObject.
- Damage applied to `IDamageable` uses `GetDamage() * Random.Range(0.7f, 1.3f)` for variance, matching Character damage calculations.
- Only objects where `CanBeDamaged()` returns `true` receive damage.

**DoorHealth** (`Assets/Scripts/World/MapSystem/DoorHealth.cs`) is the primary `IDamageable` implementation for doors. See the **door-lock-system** skill for details on breakable doors, repair mechanics, and damage resistance.

### 9. Combat Ability System

Characters can learn and equip active abilities (6 slots) and passive abilities (4 slots). Abilities are managed by the `CharacterAbilities` component (`Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs`).

#### A. Ability Types (ScriptableObject Hierarchy)
All abilities inherit from `AbilitySO` (`Assets/Scripts/Abilities/Data/AbilitySO.cs`):
- **`PhysicalAbilitySO`**: Weapon-bound (requires specific `WeaponType`), costs **Stamina**, no cooldown. If you learn sword abilities from two different CombatStyles, all sword abilities are available when any sword is equipped.
- **`SpellSO`**: Weapon-independent, costs **Mana**, has **cooldown** and **cast time**. Cast time scales with `SpellCasting` (Dexterity-linked) via division formula: `baseCastTime / (1 + spellCastingValue)`. Per-ability `_instantCastThreshold` (default 5%) determines when the spell becomes instant.
- **`PhysicalAbilitySO`** also supports optional cast time via `_baseCastTime`, reduced by `CombatCasting` (Agility-linked) using the same formula. Default threshold is 10%.
- **`PassiveAbilitySO`**: Event-triggered reactions with 9 trigger conditions: `OnDamageTaken`, `OnCriticalHitDealt`, `OnKill`, `OnDodge`, `OnBattleStart`, `OnInitiativeFull`, `OnAllyDamaged`, `OnLowHPThreshold`, `OnStatusEffectApplied`. Each passive has a trigger chance, internal cooldown, and reaction effects.

#### B. Runtime Instances (`Assets/Scripts/Abilities/Runtime/`)
- **`PhysicalAbilityInstance`**: `CanUse()` checks stamina + weapon type match.
- **`SpellInstance`**: `CanUse()` checks mana + cooldown. Tracks `_remainingCooldown`.
- **`PassiveAbilityInstance`**: `TryTrigger()` checks condition match + cooldown + chance roll.

#### C. Execution Flow
1. `CharacterCombat.UseAbility(slotIndex, target)` is the entry point.
2. Follows the same Owner-predict -> Server-validate -> Broadcast RPC pattern as `Attack()`.
3. Creates either `CharacterPhysicalAbilityAction` or `CharacterSpellCastAction` (both extend `CharacterCombatAction`).
4. Physical abilities: consume stamina on `OnStart()`, spawn hitbox via animation events.
5. Spells: consume mana on `OnStart()`, apply effect on `OnApplyEffect()` (after cast time). If interrupted, `OnCancel()` refunds mana.
6. Passive triggers: hooked into `TakeDamage()`, `JoinBattle()`, `UpdateInitiativeTick()`, and `CharacterStatusManager.ApplyEffect()`. Server-only evaluation.

#### D. Stamina Cost for Basic Attacks
- All basic melee/ranged attacks now consume Stamina (added in `ExecuteAttackLocally()`).
- Melee cost: `BASE_COST (3) + PhysicalPower * 0.1`
- Ranged cost: flat `5`
- When stamina fully depletes: **Out of Breath** status effect applied (initiative fills slower, -70% physical damage). Removed automatically when stamina fully recovers.

#### E. DamageType Expansion
The `DamageType` enum now includes: `Blunt, Slashing, Piercing, Fire, Ice, Lightning, Holy, Dark`.

#### F. Support Abilities
Both `PhysicalAbilitySO` and `SpellSO` implement `IStatRestoreAbility`, allowing any ability to restore/drain stats on cast:
- `AbilityPurpose` enum (Offensive/Support) on the base `AbilitySO`.
- `StatRestoreEntry` struct: `stat`, `value`, `isPercentage`. Processed by `StatRestoreProcessor.ApplyRestores()`.
- `IStatRestoreAbility` interface: `StatRestoresOnTarget` and `StatRestoresOnSelf`.
- Applied server-side in `OnApplyEffect()` of both `CharacterPhysicalAbilityAction` and `CharacterSpellCastAction`.

#### G. AI Integration
`CombatAILogic.DecideAbilityOrAttack()` uses resource-scanning heuristics:
1. Scans HP/Stamina/Mana pools for the most urgent need.
2. If a resource is critical (< 20%): 80% chance to use a matching Support ability. If low (< 40%): 30% chance.
3. Uses `FindSlotForStat()` to locate Support abilities that restore the needed stat, and `FindSlotWithSelfRestore()` for offensive abilities with self-heal.
4. Falls back to offensive abilities (30% chance) or basic attack.
5. Server-only: `_self.IsServer` guard prevents client-side desync.

#### H. Learning
Abilities are learned via the mentorship system (`CharacterMentorship.ReceiveLessonTick()` has an `AbilitySO` branch). All known abilities are teachable. Books can also teach abilities via `IAbilitySource` interface — see the item-system SKILL.md for the book system.

### 10. Damage Type Categories
Physical damage types: **Blunt**, **Slashing**, **Piercing**.
Magical damage types: **Fire**, **Ice**, **Lightning**, **Holy**, **Dark**.
Weapons use physical types. Spells typically use magical types.

### 11. Knockback & Combat Movement Interaction
- **`ApplyKnockback`** (`CharacterMovement.cs`) disables the NavMeshAgent, sets the Rigidbody to non-kinematic, and applies an impulse force for a 0.4s window (`_knockbackTimer`).
- All movement methods (`SetDestination`, `Stop`, `Resume`, `SetDesiredDirection`) guard against `_knockbackTimer > 0` and return early, so `CombatAILogic.Tick()` cannot override knockback through normal movement calls.
- **Critical pitfall**: `PlayerController.Move()` has a safety check that re-enables the NavMeshAgent when it detects it was "externally disabled" during combat. This check **must** respect `IsKnockedBack` — otherwise it immediately calls `ConfigureNavMesh(true)` which zeros velocity and sets the Rigidbody to kinematic, completely nullifying the knockback. The guard is: `!_character.CharacterMovement.IsKnockedBack`.
- After the knockback window ends, `FixedUpdate` re-enables the NavMeshAgent (if in combat) and sets the Rigidbody back to kinematic.

### 12. Facing System (Single Source of Truth)
During combat, a character's facing direction is driven exclusively by their `PlannedTarget`. The implementation chain:
- **`CharacterVisual.LateUpdate`** reads the character's own `PlannedTarget` via the look target reference and flips the sprite accordingly. This is the single evaluation point — no competing `UpdateFlip`/`FaceTarget` calls exist in `CombatAILogic`.
- **`SetPlannedTarget()`** (the unified entry point) calls `CharacterVisual.SetLookTarget(target)` to set the look target reference.
- **`ClearActionIntent()`** only clears the look target when the character is **NOT in battle**. This prevents the character from turning away during step-back or other tactical movement while still in combat.
- **`LeaveBattle()` and `ForceExitCombatMode()`** clear `PlannedAction`, `PlannedTarget`, and look target to fully reset facing control.
- No other system (movement, animation, AI) overrides facing during battle. This eliminates sprite-flip jitter from competing flip sources. When leaving combat, `ClearLookTarget()` returns control to the normal movement-based facing.

### 13. CombatTacticalPacer (Dynamic Movement)

`CombatTacticalPacer` (`Assets/Scripts/Character/CharacterCombat/CombatTacticalPacer.cs`) determines where a character should move between action executions. It is a pure C# class instantiated by `CombatAILogic` and called in Phase 3 (no action queued).

**Signature**: `GetTacticalDestination(Character target, float attackRange, CombatEngagement engagement, bool isCharging)`

**Movement states (priority order):**
1. **Post-attack step-back** (melee only): After `NotifyAttackCompleted()` is called, the character steps `MELEE_STEPBACK_DISTANCE = 2m` away from the target. Fires **immediately** (no throttle). Fires once per attack, then resets.
2. **Unengaged follow**: If no `CombatEngagement` exists, trails the target at `attackRange` (ranged) or `UNENGAGED_FOLLOW_MELEE_DISTANCE = 5m` (melee). Uses **1-second throttle** for re-pathing. Falls back to idle drift if within range.
3. **Tactical circling**: When outnumbering opponents 2:1+ (via `engagement.GetOutnumberRatio()`), melee fighters orbit the opponent center at `MELEE_PREFERRED_DISTANCE + CIRCLE_RADIUS_OFFSET = 6m`. **Angular slot spreading**: allies on the same side get evenly-spaced angles across a 180-degree semicircle to prevent stacking.
4. **Idle standoff/drift**: Random angle + distance from focal point every **5-7 seconds**, up to **5 units**. Constrained within a standoff distance band: `min = 1`, `max = attackRange * 1.5 + 6` from opponent center. Uses **5-second throttle** for re-pathing.

**ForceImmediateReposition**: Resets both the path update throttle and the drift timer, enabling instant response to target changes or other urgent repositioning needs.

**Leash system**: All engaged destinations are soft-constrained to the engagement's `LeashRadius` (15m from `AnchorPoint`). Overshoot is pulled back at `LEASH_PULL_STRENGTH = 0.3` — a gentle pull, not a hard clamp.

**Ranged behavior**: Ranged characters (detected via `CombatStyleExpertise.Style is RangedCombatStyleSO`) skip step-back and circling — they hold position and idle drift only.

### 14. Battle Ground Circle Indicators
Client-side visual. Colored circles beneath characters: **Blue** (ally), **Green** (party member), **Red** (enemy). Outside combat, party members always show green. Colors relative to local player.

**Components:**
- **`BattleGroundCircle.cs`**: Flat Quad prefab with `MeshRenderer` + `Custom/BattleGroundCircle` shader. Per-instance `_FadeFactor`, `_InitProgress`, `_InitFlash` via `MaterialPropertyBlock` (outside `CBUFFER_START`). `SetInitiativeProgress(float)` fills clockwise arc ring; flash burst when initiative hits 100%.
- **`BattleCircleManager.cs`**: `CharacterSystem` on Character prefab child. **Uses `_character.IsLocalPlayer`** (not `IsOwner`). Guard via `ShouldManageCircles()` at handler time. Subscribes to battle AND party events.

**Color priority** (`PickMaterial`): Enemy (red) → Party member (green, same `PartyData.PartyId`) → Ally (blue).

**Party circles:** Green on party members (excluding self) outside combat. On battle end: self + enemy removed, party circles kept and swapped to green.

**Initiative arc ring:** Fills clockwise from 12 o'clock. `Update()` reads `Initiative.CurrentAmount / MaxValue`. Configurable: `_InitInner`, `_InitOuter`, `_InitColor`.

**Initiative sync:** `ConsumeInitiative()` fires `SyncInitiativeResetClientRpc`. Called from `ExecuteAction(Func<bool>)` AND `ExecuteAttackLocally`.

**Rendering:** URP Unlit transparent shader on flat Quad, `Euler(-90,0,0)`. Three materials (ally, party, enemy). Dynamic sizing via `GetCharacterGroundRadius` + 10 units.

### 15. Battle Zone Visual Border
Translucent gold border + ambient particles on zone perimeter.

- **LineRenderer**: `M_BattleZoneBorder.mat` (Additive HDR gold), width 0.15, rounded corners.
- **ParticleSystem** (`BattleZoneParticles`): Box edge emission, 25/sec, max 80. Settings exposed on `BattleManager` (`ZoneParticleSettings`).
- **Smooth resize:** `BattleZoneController.Tick()` lerps `_visualSize` at `RESIZE_SPEED = 3f`. Collider snaps; visuals smooth. Starts tiny on creation.

## Tips & Troubleshooting
- **A character never attacks**:
  - Verify that the `BattleManager` is properly calling `.UpdateInitiativeTick()`.
  - Check `WeaponInstance.CanFire()`. A magazine-based weapon won't fire if empty.
  - Check if Stamina is depleted — basic attacks now require Stamina.
- **Ranged attack accuracy**: Projectiles travel in a straight line towards the target's position at the moment of firing. They do not "home in" on the target.
- **Ability won't fire**:
  - Check `AbilityInstance.CanUse()` — verifies resource cost, cooldown, and weapon match.
  - Physical abilities require the correct `WeaponType` equipped.
  - Spells require cooldown to be 0 and enough Mana.
- **Passive never triggers**:
  - Verify the passive is equipped in one of the 4 passive slots (not just known).
  - Check internal cooldown isn't blocking repeated triggers.
  - Passives are server-only — check `IsServer` context.
- **Out of Breath not applying**:
  - Verify `_outOfBreathEffect` is assigned on the `CharacterStatusManager` component.
  - It's a permanent-duration effect removed when `Stamina.CurrentAmount >= Stamina.MaxValue`.
