---
description: Le Hub central de l'entité. Dictes les règles sur la disponibilité du personnage (IsFree), son cycle de vie (Death/Unconscious) et son cerveau (Switch Joueur/PNJ).
---

# Character Core Skill

Le script `Character.cs` est la classe la plus importante de l'entité. C'est l'Architecture Centrale (Facade Pattern) par laquelle **tout** transite.

## 1. Pattern Facade (Obligations)
L'Agent **ne doit JAMAIS chercher les composants liés à un personnage via des `GetComponent` isolés**.
S'il possède une référence à `Character`, alors il a déjà accès à la majorité du système de manière sécurisée et performante.
Exemples :
- `character.CharacterJob` -> Gère le travail.
- `character.CharacterCombat` -> Gère la bagarre.
- `character.CharacterInteraction` -> Gère les dialogues.
- `character.CharacterMovement` -> Gère la navigation.
- `character.CharacterEquipment` -> Gère l'inventaire équipable.
- `character.Stats` -> Donne les statistiques vitales.

## 2. Juge de Paix et Disponibilité (`IsFree()`)
C'est la méthode de sécurité par excellence. `Character` scrute tous ses composants enfants pour dire au système global (GOAP, Commandes du joueur, Interactions) si le personnage a le droit d'être interrompu ou s'il est déjà occupé.

`IsFree(out CharacterBusyReason reason)` renverra False et expliquera pourquoi si le personnage est :
- Mort (`Dead`)
- KO (`Unconscious`)
- En train de se battre (`InCombat`)
- En dialogue (`Interacting`)
- En train de forger ou construire un objet complexe (`Crafting`)
- Enseignant de classe (`Teaching`)

## 3. Cycle de Vie et Statuts 
`Character` est responsable des changements majeurs d'état. Il ne faut jamais bricoler les HP ou le collider à la main pour "tuer" quelqu'un.

- **SetUnconscious(true)** :
  - L'entité devient physiquement inerte (Rigidbody passes en Kinematic pour que les chutes soient gérées).
  - Le cerveau d'IA (`Controller`) est éteint et sa pile vidée.
  - Le `NavMeshAgent` est désactivé (indispensable pour Unity).
  - L'Animator passe dans l'état de K.O.
- **Die()** :
  - Effectue la même routine (désactivation cerveau + navmesh).
  - Mais la mort (`_isDead = true`) prime définitivement sur le reste.

## 4. Context Switching (Le Cerveau)
Un personnage de votre jeu peut passer du statut d'IA civile autonome (PNJ) à celui d'Avatar contrôlé par le joueur en un claquement de doigt.

- `SwitchToPlayer()` : Eteint le `NPCController`, allume le `PlayerController`. Désactive le NavMeshAgent car le joueur utilise la physique (Rigidbody non-Kinematic) pour se déplacer.
- `SwitchToNPC()` : Eteint le `PlayerController` et allume le `NPCController`. Réactive le NavMeshAgent et remet le Rigidbody en Kinematic.

> En cas de bug d'input ou de navigation, toujours vérifier d'abord que le bon Controller est allumé via ce système de Switch.
