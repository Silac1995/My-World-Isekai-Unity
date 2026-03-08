---
description: Règles globales du projet, style de code C#, optimisation et bonnes pratiques spécifiques à l'environnement Unity en 2D/3D.
---

# Global Project Skills

Ce skill contient les règles fondamentales et les bonnes pratiques à appliquer systématiquement pour tout développement dans ce projet Unity.

## When to use this skill
- **Toujours** : Lors de l'écriture, la modification ou la revue de n'importe quel script C# dans ce projet.
- Avant de proposer une nouvelle architecture ou fonctionnalité (pour s'assurer qu'elle respecte l'optimisation et la vision multijoueur).
- Lors de la gestion des coroutines, des événements et de la mémoire.

## How to use it
Appliquez strictement les règles suivantes lors de la rédaction de code :

### 1. Style et Architecture C#
- **Attributs privés** : Toujours préfixer les attributs privés par un underscore `_` (ex: `_skeletonAnimation`).
- **Encapsulation** : Privilégier les attributs privés avec des accesseurs (propriétés `get`) ou `[SerializeField] private` pour l'inspecteur Unity. L'utilisation d'attributs publics doit être évitée sauf nécessité absolue.

### 2. Contexte du Jeu
- **Hybride 3D/2D** : Le jeu est développé sous Unity dans un environnement 3D, mais utilise des sprites de personnages en 2D (notamment Spine). Prenez en compte les interactions 3D/2D.
- **Multijoueur** : Le jeu est conçu avec l'objectif d'être multijoueur. Pensez à l'architecture réseau et évitez les singletons ou dépendances qui bloqueraient cette évolution.

### 3. Optimisation et Memory Safety
- **Performances** : L'optimisation est une priorité absolue. Évitez toute allocation inutile dans la boucle `Update` et prévenez les fuites de mémoire.
- **Gestion des Coroutines** :
    - Ne laissez *jamais* une Coroutine s'exécuter sans contrôle.
    - Conservez une référence à vos coroutines. Chaque `StartCoroutine` doit idéalement être accompagné d'un `StopCoroutine` (ou `StopAllCoroutines`) dans le `OnDisable` ou `OnDestroy`.
- **Gestion des Événements** :
    - Toujours se désabonner des événements (actions C#, événements Unity, animations Spine) dans la méthode `OnDisable` ou `OnDestroy` pour éviter les fuites de mémoire (Memory Leaks).
