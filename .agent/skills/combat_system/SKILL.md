---
description: Architecture, déroulement et intégration du système de combat (BattleManager, CharacterCombat, Initiative, Stats).
---

# Combat System Skill

Ce skill détaille l'architecture du système de combat dans le projet et les règles à suivre lors de son extension ou de son débogage. Le système de combat est basé sur des notions de **Tick d'Initiative**, des **Groupes d'Engagements**, et la séparation stricte des rôles entre le Manager global et les composants locaux.

## When to use this skill
- Pour ajouter une nouvelle fonctionnalité liée aux combats (ex: attaque de zone, fuite, nouveaux buffs/debuffs liés au combat).
- Pour interagir avec le délai d'attaque des personnages (`Initiative`).
- En cas de bugs où le combat ne se termine pas, ou un personnage est figé sans attaquer.
- Lors de l'ajout ou de la modification de statistiques liées au combat (dans `CharacterStats`).

## Architecture & How to use it

### 1. Le BattleManager (Gestion Global)
Le `BattleManager` est l'entité suprême d'un combat, souvent instantiée au déclenchement d'un affrontement.
- **BattleTeams** : Il maintient toujours deux équipes (Initiateur vs Cible). On ne supporte *pas* de mêlée générale à 3 équipes dans une seule instance.
- **CombatEngagement** : NOUVEAU SYSTÈME. Le BattleManager gère des "sous-groupes" de bagarre (ex: le Guerrier tape le Mage, pendant que l'Archer tape le Voleur au sein du même combat). Géré par la liste interne `_activeEngagements`.
- **BattleZone** : Une zone physique (`BoxCollider` isTrigger) et de pathfinding (`NavMeshModifierVolume`) est générée dynamiquement au centre de l'affrontement initial pour marquer le terrain.
- **Tick System** : C'est le `BattleManager` qui donne le rythme (`PerformBattleTick()`), et *non pas l'Update de chaque personnage*.

### 2. CharacterCombat (Logique Locale)
C'est le composant que chaque PNJ/Joueur possède pour pouvoir se battre.
- **Mode Combat** : Un personnage bascule en "CombatMode" (et tire son arme) s'il compte attaquer. Il y a un `COMBAT_MODE_TIMEOUT` (7 secondes par défaut).
- **Consommation & Tick d'Initiative** :
  - La méthode `.IsReadyToAct` vérifie si l'Initiative (dans les Stats) est pleine.
  - La méthode `.ConsumeInitiative()` remet l'initiative à 0 après une attaque réussie.
  - La méthode `.UpdateInitiativeTick(amount)` est **appelée par le BattleManager** pour faire grimper la barre.
- **Attack()** : Choix dynamique. Si la cible est portée d'une arme à distance (selon le `RangedCombatStyleSO.MeleeRange`), il fera un `RangedAttack()`. Sinon, il choisit `MeleeAttack()`. Ces actions sont envoyées au système global `CharacterActions`.

### 3. Combat Styles (`CombatStyleSO`)
Le pont entre l'État (`CharacterStats`) et la Logique Spatiale (`CharacterCombat`).
- **Data Statistique** : Il définit sur quelle statistique l'attaque devient plus forte (`ScalingStat`, `StatMultiplier`). Ex: La dague scale sur la Dextérité plutôt que la Force.
- **Portée et Animations** : Il contient la portée de l'arme (`MeleeRange`) et surtout **le contrôleur d'animation dynamique** assigné selon le niveau de maîtrise (`StyleLevelData.CombatController`).
- Ces variables sont interrogées à la volée par le `CharacterCombat` au moment d'attaquer.

### 4. CharacterStats (Répartition des Stats)
Le combat utilise massivement `CharacterStats`. Il est primordial de respecter son architecture :
- **Primary Stats** : Dynamique (Health, Stamina, Mana, **Initiative**). 
  - *Note : L'initiative a une base de "0" par défaut.*
- **Secondary Stats** : Les caractéristiques de base (Strength, Agility, Dexterity, Intelligence, Endurance, Charisma).
- **Tertiary Stats** : Dérivées des secondaires (PhysicalPower, MoveSpeed, DodgeChance, CriticalHitChance, etc.). Ces stats sont celles vérifiées lors des calculs de dégâts purs.

## Tips & Troubleshooting
- **Un personnage ne tape jamais** : Vérifiez que le `BattleManager` appelle bien le `.UpdateInitiativeTick()` sur ce personnage. Si la zone de combat l'a "oublié" dans `_allParticipants`, l'Initiative restera à 0 indéfiniment.
- **Le combat ne s'arrête pas** : Le drapeau `_isBattleEnded` dépend souvent de la survie des équipes entières. Assurez-vous des callbacks lors de la mort d'un participant.
