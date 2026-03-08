---
description: Rendu visuel 2.5D (Billboarding), Presets de Race, et architecture logique des parties du corps (Yeux, Mains) en vue de Spine2D.
---

# Character Visuals System Skill

Ce skill détaille comment les visuels des personnages (sprites 2D dans un monde 3D) sont gérés, ainsi que le système de "Body Parts" (Mains, Yeux, Cheveux, etc.).

> [!WARNING] (Migration Spine2D)
> Le moteur de rendu local **SpriteResolver** et **SpriteLibrary** d'Unity (actuellement utilisé par `CharacterEye`, `CharacterHand`...) est **temporaire**. Il sera entièrement remplacé par des rigs **Spine2D**.
> **RÈGLE D'OR** : Bien que le *rendu* sous-jacent va changer, **l'API logique des Body Parts doit être préservée**. Le code de gameplay (`CharacterActions`, `GoapActions`, `Animator`) **doit** continuer à appeler les méthodes comme `CharacterEye.SetClosed(true)` ou `CharacterHand.SetPose("fist")`. C'est cette surcouche logique qui garantit la modularité du projet, peu importe la technologie d'animation en dessous.

## When to use this skill
- Pour configurer de nouvelles races (`RaceSO`) ou gérer les presets visuels (`CharacterVisualPresetSO`).
- Pour diriger le regard ou orienter un sprite (`Billboarding`, `LookTarget`).
- Lors de l'implémentation d'une fonctionnalité nécessitant le changement d'expression d'un personnage (cligner des yeux, serrer les poings pendant le combat).
- Pour interagir avec `CharacterBodyPartsController`.

## Architecture & How to use it

### 1. Billboarding & Rendering (`CharacterVisual.cs`)
- **Billboarding** : Les sprites 2D des personnages font toujours face à la caméra. Cela est géré via la rotation du `transform` par rapport à la rotation de la caméra principale.
- **Orientation (Flip)** : `IsFacingRight` contrôle l'inversion du visuel. Il contient une sécurité anti-flickering (`FLIP_COOLDOWN = 0.15f`) et bloque le flip si le personnage est en plein Knockback.

### 2. Presets et Initialisation
La méthode `ApplyPresetFromRace(RaceSO)` dans `CharacterVisual` sert de hub d'initialisation.
- Elle délègue l'initialisation des organes au `CharacterBodyPartsController.InitializeAllBodyParts()`.
- Elle applique le `DefaultSkinColor` (ou la catégorie) à travers les divers sous-contrôleurs (Ears, Hands, etc.).

### 3. Logique des Body Parts (L'API Intouchable)
L'architecture utilise un hub, `CharacterBodyPartsController`, qui contient les sous-contrôleurs (`EyesController`, `HandsController`, etc.), eux-mêmes gérant les objets finaux (`CharacterEye`, `CharacterHand`).

**L'API qui doit survivre à Spine2D** :
- **Clignement / Fermeture (Eyes)** : `CharacterEye.SetClosed(bool)` est la source de vérité pour déterminer si un oeil est fermé (pour dormir, cligner, exprimer la douleur).
- **Poses de Mains (Hands)** : `CharacterHand.SetPose(string)` (ex: "fist", "normal") dicte l'état de la main. Il est conçu pour synchroniser toutes les couches (le pouce *et* les doigts sous l'arme).
- **Catégories** : Des méthodes comme `SetCategory(string)` permettent de changer un composant entier (ex: passer d'une oreille Humaine à Elfe).

## Tips & Troubleshooting
- **Un sprite ne s'affiche pas / Bug visuel** : Vérifiez que la logique appelle bien l'API de base (`SetPose()`, `SetClosed()`). Le fait que la technologie sous-jacente soit temporaire (SpriteResolver) ne dispense pas d'utiliser l'architecture modulaire !
- Si vous créez une nouvelle action GOAP/BT (ex: "S'endormir"), n'oubliez pas d'y inclure l'appel visuel de vos actions : `Character.CharacterVisual.BodyPartsController.EyesController.SetClosed(true)`.
