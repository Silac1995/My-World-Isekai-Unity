---
description: Structure, priorités et API de contrôle du système de Behaviour Tree des NPCs.
---

# Behaviour Tree System Skill

Ce skill détaille l'arborescence et le fonctionnement du système de Behaviour Tree (BT) utilisé par les NPCs. 

## When to use this skill
- Pour ajouter un nouveau comportement global (ex: dormir, manger, routines sociales).
- Pour interagir avec un NPC de l'extérieur via le script `NPCBehaviourTree` (ex: donner un ordre).
- Pour debugger pourquoi un NPC "ne fait rien" ou agit de façon inattendue.

## How to use it

### 1. Architecture et Priorités des Noeuds
Le Behaviour Tree utilise un `BTSelector` racine (`_root`) qui évalue ses enfants de haut en bas. **L'ordre définit la priorité**.
L'arbre actuel s'évalue dans cet ordre :
1. **Ordres** (`BTCond_HasOrder`) : Le joueur ou le jeu a donné un ordre explicite (Priorité Max).
2. **Combat** (`BTCond_IsInCombat`) : Le NPC est déjà engagé dans un combat.
3. **Entraide** (`BTCond_FriendInDanger`) : Le NPC voit un allié agressé.
4. **Agression** (`BTCond_DetectedEnemy`) : Le NPC détecte une menace et l'attaque.
5. **Besoins** (`BTCond_HasUrgentNeed`) : Faim, repos d'urgence, vêtements...
6. **Schedule** (`BTCond_HasScheduledActivity`) : Routines quotidiennes (Travail, sommeil régulier).
7. **Social** (`BTCond_WantsToSocialize`) : Discussions et interactions spontanées.
8. **Wander** (`BTAction_Wander`) : Le Fallback, le NPC erre.

*Si vous ajoutez un nouveau comportement, réfléchissez au noeud dans lequel l'insérer ou à la position du nouveau noeud conditionnel dans le `BuildTree()` de `NPCBehaviourTree`.*

### 2. Le Tick (Performances)
- **Staggering** : Le BT ne s'exécute pas à chaque frame. Il s'exécute tous les `_tickInterval` frames (défaut: 5), avec un décalage (`_frameOffset`) unique par PNJ pour étaler la charge CPU.
- **Exceptions de tick** :
    - Le joueur ne tick pas le BT.
    - Un personnage mort ne tick pas.
    - `Controller.IsFrozen` pause le BT (utile pour les cinématiques/dialogues forts).
    - `CharacterInteraction.IsInteracting` pause le BT pour éviter des mouvements ou des annulations impromptues de l'interaction (ex: s'asseoir).
    - L'ancien système (`Controller.CurrentBehaviour != null`) met le BT en pause. *Le but à terme est sûrement de remplacer tous les comportements par le BT ou le GOAP, mais c'est la règle actuelle*.

### 3. API Publique (Interaction Externe)
Pour bypasser l'IA autonome et forcer une action (ex: un sort mind-control, le mode Build du joueur, etc.) :
- `GiveOrder(NPCOrder order)` : Place un ordre dans le `Blackboard`. Il sera prioritaire au prochain tick. Annule tout ordre précédent en cours.
- `CancelOrder()` : Annule l'ordre en cours.
- `ForceNextTick()` : À appeler si le NPC vient d'être décongelé (`IsFrozen = false`) et qu'il faut qu'il réagisse très vite sans attendre son cycle de 5 frames.

## Mettre à jour des Noeuds 
Les actions terminales du BT doivent, dans la mesure du possible, implémenter l'interface `IAIBehaviour` pour pouvoir être proprement gérées (avec `.Act()`, `.Exit()`, `.Terminate()`, `.IsFinished`).
