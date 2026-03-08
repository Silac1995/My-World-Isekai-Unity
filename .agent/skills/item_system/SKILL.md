---
description: Le cycle de vie d'un objet (ItemSO statique, ItemInstance dynamique en mémoire, CharacterEquipment pour l'usage et WorldItem pour le drop au sol).
---

# Item System Skill

Ce skill détaille l'architecture complète des objets dans le jeu. Le système repose sur une séparation drastique entre la définition abstraite et universelle de l'objet, et son existence concrète, colorisée et instanciée dans la scène.

## Architecture

Le système est construit autour de 4 piliers principaux :

### 1. La Data Universelle (`ItemSO`)
Un *ScriptableObject* contenant la donnée immuable de l'objet.
- Contient le nom de base, la description, l'icône, le `ItemPrefab` visuel.
- **Crafting** : Contient les ingrédients nécessaires via la variable `CraftingRecipe`.
- Contient le SpriteResolver "parent" (`SpriteLibraryAsset`) de l'objet.
- > Ne stockez **JAMAIS** d'état changeant (comme la couleur ou la durabilité d'une épée) dans un `ItemSO`. C'est une erreur d'architecture grave qui se répercutera sur l'entièreté des épées du royaume.

### 2. La Donnée en Mémoire (`ItemInstance`)
C'est l'incarnation d'un item dans l'inventaire. Cette classe C# "Pure" (non MonoBehaviour) englobe le `ItemSO` pour lui donner des caractéristiques uniques.
- **Couleurs dynamiques** : `Color_Primary` et `Color_Secondary`.
- Nom customisé potentiel (`_customizedName`).
- **Initialisation visuelle** : Elle utilise `InitializePrefab()` (et `InitializeWorldPrefab()`) pour rechercher les noeuds enfants de type `Color_Primary` dans un GameObject vierge généré par l'Engine, afin d'y injecter ses couleurs uniques.

### 3. La Présence Physique au sol (`WorldItem`)
Quand un `ItemInstance` est laché hors de l'inventaire, il matérialise un GameObject local `WorldItem` avec un `SortingGroup`.
- Le script assigne le `ItemPrefab` enfant du SO dans le Node `_visualRoot`.
- Le script fait le pont entre la donnée mémoire de l'`ItemInstance` et les couleurs physiques via `WearableHandlerBase` (pour les vêtements complexes) ou l'appel direct à `InitializeWorldPrefab` (pour un objet simple comme une pomme).

### 4. L'Usage sur le Personnage (`CharacterEquipment`)
C'est le composant Hub responsable d'attacher un `ItemInstance` sur le personnage.
- **Système de Couches (`Layer`)** : Le personnage possède plusieurs couches de vêtements (`UnderwearLayer`, `ClothingLayer`, `ArmorLayer`). Equiper un item nécessite de trouver le bon `TargetLayer` et de lui injecter l'instance.
- **Le Cas Spécial du Sac (`BagInstance`)** : Un sac est un `WearableType.Bag`. Lorsqu'il est équipé, `CharacterEquipment` réveille les sockets accrochés physiquement au dos du personnage (`_bagSockets`), puis instancie visuellement les armes qui se trouvent **à l'intérieur** de cet `Inventory` lié au sac.
- > **Equip vs Unequip** : L'utilisation de `character.DropItem` est la fonction charnière. Elle gérera simultanément l'arrachage du visuel local du joueur et l'apparition du `WorldItem` à l'endroit où le drop a eu lieu.
