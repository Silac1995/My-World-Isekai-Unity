---
description: Architecture de code "Future-Proof" pour le multijoueur (Pas de singletons locaux, séparation Inputs/Logique).
---

# Multiplayer Architecture Skill

Ce skill dicte la philosophie architecturale "Network-Ready" (prête pour le réseau) qui **doit être appliquée systématiquement** dans le projet, même s'il n'y a pas encore de framework (comme Mirror ou Netcode) d'installé.
La règle d'or (définie dans `global.md`) est de toujours coder en présumant que le jeu "va être" multijoueur.

## When to use this skill
- À appliquer **systématiquement** lors de la création d'un nouveau système (ex: système de Quête, Inventaire, Combat).
- Lors de l'ajout d'une mécanique impliquant plusieurs personnages.
- Lors de l'écriture de `MonoBehaviours` liés au Temps ou aux Inputs.

## Règles d'Architecture "Future-Proof"

### 1. Bannissement des Singletons pour l'État de Jeu
- **La Règle :** Ne **JAMAIS** utiliser `FindObjectOfType<Player>()` ou un `Player.Instance`.
- **Pourquoi ?** En multijoueur, il y a *plusieurs* joueurs dans une même scène.
- **La Solution :** Utilisez l'Injection de Dépendances, des références explicites par GetComponent, ou des gestionnaires d'instances locaux isolés. (ex: Un `BattleManager` gère une liste de `CharacterCombat` qu'il connaît, plutôt que de deviner qui attaque qui).

### 2. Découplage strict des Inputs et de la Logique
- **La Règle :** Le code qui lit le clavier/la manette (`InputManager.cs`) **ne doit pas** contenir la logique de gameplay (`personnage.Deplacer()`).
- **Pourquoi ?** En réseau, un monstre ne reçoit pas d'inputs claviers locaux. Il reçoit un ordre (RPC) du serveur.
- **La Solution :** Les Inputs ne font qu'emettre des évènements (ex: `OnAttackPressed`). La logique (`Attack()`) écoute cet évènement, mais pourrait tout aussi bien être appelée par un paquet réseau (ou une décision du `BehaviourTree`).

### 3. État (State) vs Visuel
- Ce découplage est déjà amorcé dans le projet : `CharacterStats` possède la donnée et `CharacterVisual` l'affiche.
- **La Règle :** Ne synchronisez jamais un Visuel sur le réseau. Seul l'État (`CharacterStats.Health`, `CharacterCombat.Initiative`) doit un jour être synchronisé par le serveur.

### 4. La Dictature du Temps
- **La Règle :** Ne manipulez **jamais** `Time.timeScale` pour mettre le jeu en pause ou le ralentir dans une logique locale de personnage.
- **Pourquoi ?** Ralentir le temps localement désynchronisera le client de tous les autres joueurs et du serveur physique de façon catastrophique.
- **La Solution :** Confiez la gestion du temps aux Managers Serveurs. Exemple typique : le `BattleManager` utilise son propre "Tick" (`PerformBattleTick()`) indépendant du `Time.time` de Unity, ce qui le rendra facilement synchronisable plus tard.

## Checklist du Code "Network-Ready"
Passez votre nouveau code au crible :
- [ ] Mon code survit-il s'il y a 2 "Objets Joueurs" dans la scène ?
- [ ] Si j'appelle ma méthode de tir ou de déplacement par le code pur depuis n'importe où, marche-t-elle sans dépendre d'un booléen obscur du clavier ?
- [ ] Mes délais (cooldowns) se basent-ils bien sur l'architecture locale plutôt que de modifier le moteur Unity ?
