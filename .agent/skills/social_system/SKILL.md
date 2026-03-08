---
description: Système d'interaction entre PNJ/Joueurs (Tours de paroles, ICharacterInteractionAction) et de relations (Modificateurs de Compatibilité, Amitiés/Inimitiés).
---

# Social System Skill

Ce skill détaille comment les personnages interagissent les uns avec les autres de manière dynamique (discussions, échanges) et comment ces événements forgent leurs mémoires à long terme (les relations et la compatibilité de personnalité).
Il regroupe `CharacterInteraction` (l'acte de s'exprimer) et `CharacterRelation` (la mémoire).

## When to use this skill
- Pour ajouter un nouveau type d'interaction (ex: Insulter, Offrir un cadeau, Demander en mariage) via l'interface `ICharacterInteractionAction`.
- Pour comprendre comment la relation de deux personnages évolue (modification d'opinion).
- En cas de personnages se bloquant mutuellement (deadlock) lors d'un dialogue.

## Architecture

Le système social repose sur **deux piliers interconnectés** : L'Actuel (Interaction) et Le Passé/Futur (Relation).

### 1. Actuel : CharacterInteraction
L'interaction est événementielle (ex: engager un PNJ avec E, ou deux PNJs se croisant et déclenchant l'action GOAP `Socialize`).

#### Le cycle de vie d'une Interaction :
1. **Démarrage (`StartInteractionWith`)** : 
   - _Sécurité_ : Le système vérifie que les deux sont libres (`IsFree()`).
   - _Connexion_ : Il fige la cible (`Freeze()`), la force à regarder l'initiateur (`SetLookTarget()`), et ajoute/actualise instantanément la `CharacterRelation` pour dire qu'ils se connaissent (`SetAsMet()`).
   - _Positionnement_ : L'initiateur marche vers la cible (`MoveToInteractionBehaviour`).
2. **Dialogue (`DialogueSequence`)** : C'est une Coroutine simulant de vrais échanges.
   - Les rôles d'Orateur (Speaker) et d'Ecouteur (Listener) s'inversent (jusqu'à 6 échanges maximum).
   - L'algorithme attend la fin visuelle de la "Speech bubble" (`CharacterSpeech.IsSpeaking`) avant de commencer son délai (`WaitForSeconds(1.0f à 2.5f)`) pour la réponse naturelle.
3. **Fin (`EndInteraction`)** : Libère les personnages (`Unfreeze()`, efface les `LookTarget` et nettoie les `MoveToInteractionBehaviour`).

#### Comment ajouter une action ?
Créer une classe qui implémente l'interface `ICharacterInteractionAction` qui contiendra le coeur de la réplique ou de l'acte (ex: `InteractionTalk.cs`).

### 2. Le Souvenir : CharacterRelation
La `CharacterRelation` stocke la liste des liens (`Relationship`) qu'un personnage entretient avec le reste du monde.
- **Principe Bilatéral** : Si A ajoute B (`AddRelationship`), le code garantit que B ajoute A instantanément.

#### Le Système de Compatibilité
L'opinion (`UpdateRelation`) ne monte ou ne descend jamais de manière "brute". Elle est filtrée par le `CharacterProfile` (la Personnalité).
Si A essaie de charmer B (ex: +10 de relation) :
- Si B est **Compatible** avec la personnalité de A : Le gain de +10 est multiplié par 1.5 (Gain = +15). S'il y avait conflit (-10), la perte est amoindrie (-5).
- Si B est **Incompatible** : Le gain de +10 est réduit de moitié (Gain = +5). Si conflit (-10), la catastrophe est amplifiée (-15).

## Tips & Troubleshooting
- **Mon personnage est coincé indéfiniment après avoir parlé** : Vérifiez que l'action GOAP ou l'event d'input appelle bien `EndInteraction()` en cas d'interruption brutale, ou vérifiez qu'aucune Coroutine `DialogueSequence` ne crashe au milieu.
- **Pourquoi le joueur a-t-il moins de points que prévu avec ce PNJ ?** : C'est la compatibilité de personnalité (`CharacterProfile.GetCompatibilityWith()`). L'agent doit toujours regarder ce système si une variation de points étrange lui est rapportée.
