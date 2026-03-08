---
description: Règles d'utilisation du GOAP pour dicter la vie de tous les jours et les buts ultimes d'un NPC (ex: fonder une famille).
---

# GOAP (Life Goals) System Skill

Ce skill définit comment utiliser et étendre le système GOAP (Goal-Oriented Action Planning). 
**Important :** Le GOAP de ce projet n'est pas qu'un gestionnaire de file d'attente pour des métiers (ex: Gatherer). Il est conçu pour être le chef d'orchestre de la **vie de tous les jours** du NPC, basé sur des **buts ultimes**.

## Concept Global : Le GOAP comme "Life Manager"
Le GOAP donne une direction globale et organique au NPC. Plutôt que de dire "Va couper du bois", on donne un Goal de vie au NPC. 

Exemples de buts ultimes :
- **Fonder une famille** : Le Planner GOAP va privilégier des actions de vie courante menant à ce but (Discuter avec des NPCs, cibler le sexe opposé, flirter, se marier, faire des enfants).
- **Être le meilleur artiste martial** : Le Planner enchaînera des actions d'entraînement (trouver un dojo, affronter des adversaires, s'améliorer sur un CombatSO).
- **Ambition financière** : Amasser des richesses (ce qui le poussera à trouver un métier comme Gatherer et déposer des ressources).

Le GOAP gère le plan à long/moyen terme, tandis que le Behaviour Tree (BT) s'occupe de la survie à court terme (réagir à une agression, fuir, manger en urgence).

## When to use this skill
- Pour concevoir un nouveau **Life Goal** ou de nouvelles chaînes d'actions quotidiennes (ex: cycle de séduction, cycle d'entraînement martial).
- Lors de la création d'une `GoapAction` qui fait avancer l'histoire personnelle du NPC (ex: `Action_Socialize`, `Action_TrainMartialArts`).
- Pour structurer les préconditions et effets liant la vie sociale, les métiers et les besoins du NPC.

## How to use it

### 1. Créer un But Ultime (`GoapGoal`)
Un Goal GOAP définit l'état absolu que le NPC veut atteindre dans un pan de sa vie.
- `GoalName` : Nom du but à accomplir (ex: "FonderUneFamille").
- `DesiredState` : Dictionnaire d'états booléens décrivant le succès du but.
    - Ex: `new Dictionary<string, bool> { { "hasChildren", true } }`
- `Priority` : Niveau d'importance du but (permet au NPC de choisir entre s'entraîner ou développer son cercle social).

### 2. Définir une Action de Vie (`GoapAction`)
L'action est la brique de base du quotidien.
- `ActionName` : String identifiant l'action.
- `Preconditions` : L'état nécessaire pour lancer l'action.
    - Ex: pour faire un enfant, la précondition est peut-être `{"isMarried", true}`.
- `Effects` : L'état résultant de l'action.
    - Ex: faire un enfant donne la condition `{"hasChildren", true}`.
- `Cost` : La "pénibilité" ou la difficulté du processus (le Planner choisit la route la moins coûteuse). On peut varier ce coût selon les "traits" du NPC !
- `IsValid`, `Execute`, `IsComplete`, `Exit` : Fonctions contrôlant l'action frame par frame.

### 3. Logique du Planner (`GoapPlanner`)
- Il effectue une recherche en marche arrière (backward search) à partir du Life Goal et trouve la suite logique et quotidienne pour l'accomplir.
- **Astuce d'équilibrage** : Mettez en place des chaînes de préconditions logiques qui imposent au NPC de vivre sa vie (pour se marier il faut un haut niveau d'affinité, pour avoir de l'affinité il faut l'action "Socialize", etc).
