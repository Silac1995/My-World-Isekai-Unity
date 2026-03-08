---
description: L'écosystème du travail reliant l'employé (CharacterJob), la data pure (Job) et le lieu (CommercialBuilding).
---

# Job System Skill

L'économie et le travail régissent le monde du jeu. Ce skill détaille l'architecture en triangle qui permet à un personnageNPC ou Joueur d'occuper un poste dans un bâtiment.

## La Sainte Trinité du Travail

L'architecture repose strictement sur trois concepts pour garantir que personne ne travaille "dans le vide" :

1. L'Employé -> `CharacterJob`
2. Le Concept/Data -> `Job`
3. Le Lieu Physique -> `CommercialBuilding`

### 1. L'Employé (`CharacterJob`)
C'est le composant (MonoBehaviour) attaché au personnage.
- **Dictionnaires des affectations (`JobAssignment`)** : Permet à un personnage d'avoir de multiples emplois.
- **Le Garde-fou Temporel (`DoesScheduleOverlap`)** : Lorsqu'il tente de prendre un job (`TakeJob`), cet algorithme vérifie qu'aucun de ses postes actuels n'entre en conflit avec les nouvelles heures de ce job. 
- **Injection dans l'AI (`InjectWorkSchedule`)** : En cas de succès, `CharacterJob` va forcer les tranches horaires (ex: 8h-17h de Travail) dans le planificateur de routine (`CharacterSchedule`) du personnage, ce qui l'emmènera physiquement au travail.
- **La Propriété** : Stocke si le personnage est le Boss/Owner du Building.

### 2. Le Rôle (`Job`)
Classe C# Pure abstraite. C'est l'essence du poste (par exemple, "Barman").
- **Stateless/Data** : Contient le `JobTitle`, la `Category` et spécifie les heures de la journée dédiées à ce rôle (`GetWorkSchedule()`).
- **Conteneur** : Stocke les références de `Worker` (qui fait le job) et `Workplace` (où cela se passe). Un job ne peut avoir qu'un seul worker. `IsAssigned` vérifie sa disponibilité.
- **L'Action (`Execute()`)** : Méthode appelée chaque Tick durant les heures de bureau. C'est ici que l'agent développera la logique métier (Servir des bières, Forger une épée, etc.).

### 3. Le Lieu (`CommercialBuilding`)
L'ancrage physique dans la scène.
- **L'Administration** : C'est le building qui instancie tous ses propres Jobs dans le tableau abstrait (via `InitializeJobs()`).
- **Le Recrutement (`AskForJob`)** : Pour qu'un personnage obtienne un poste ici, il faut que le Building ait un Boss (`HasOwner`), que le poste existe localement et qu'il soit libre.
- **Le Pointage (Punch In / Punch Out)** : Lorsqu'un NPC arrive physiquement dans le bâtiment pour bosser, il s'annonce (`WorkerStartingShift`). Le building sait instatanément qui est dans les murs.

### 4. L'Artisanat (CraftingBuilding & JobCrafter)
L'artisanat suit une sur-couche de ce système.
- **CraftingBuilding** : Un `CommercialBuilding` spécialisé. Il scanne ses `ComplexRoom` pour trouver des `CraftingStation` et compile une liste de ce qui peut y être fabriqué via `GetCraftableItems()`. 
- **JobCrafter** : Le job d'artisan (ex: Forgeron).
   - **Exigences** : Il demande au NPC d'avoir une compétence spécifique (`SkillSO`) et d'un niveau minimum (`SkillTier` défini dans `CharacterSkills`). Sans ça, le building refuse l'embauche.
   - **Logique pilotée par la demande** : L'artisan ne produit pas dans le vide. Son Behaviour Tree vérifie que le `JobLogisticsManager` du bâtiment possède une **`CraftingOrder`** active (qui suit la même logique temporelle et de pénalité de réputation que `BuyOrder`). S'il y a une commande, il trouve la bonne station, joue son animation, et produit l'objet.

## Comment Créer un Nouveau Job ?
A l'avenir, si l'Agent doit créer un "Forgeron" :
1. Taper le code `JobCrafter` abstrait, puis `JobBlacksmith` héritant de `JobCrafter`. Définir ses horaires, ses prérequis de `SkillSO`/`SkillTier`, et son node BT `BTAction_PerformCraft`.
2. Créer ou modifier le `ForgeBuilding` héritant de `CraftingBuilding` (et non plus juste `CommercialBuilding`) pour que sa fonction `InitializeJobs()` ajoute un `JobBlacksmith` + un `JobLogisticsManager` (pour les commandes).
3. Terminé ! Le joueur pourra aller demander le poste (s'il a le bon niveau de skill), et passer des commandes de fabrication au Logistics Manager.
