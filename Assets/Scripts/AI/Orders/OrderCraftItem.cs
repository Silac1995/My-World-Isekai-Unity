using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Ordre : Trouver une station de craft valide et fabriquer l'objet demandé.
    /// Sera intercepté et transformé en intention physique par l'IA.
    /// </summary>
    public class OrderCraftItem : NPCOrder
    {
        private ItemSO _itemToCraft;
        private CraftingStation _reservedStation;
        private bool _actionInitiated = false;

        public override NPCOrderType OrderType => NPCOrderType.CraftItem;
        public ItemSO ItemToCraft => _itemToCraft;

        public OrderCraftItem(ItemSO itemToCraft)
        {
            _itemToCraft = itemToCraft;
        }

        public override BTNodeStatus Execute(Character self)
        {
            if (_itemToCraft == null)
            {
                IsComplete = true;
                return BTNodeStatus.Failure;
            }

            // 1. Initialisation : Trouver la station et lancer l'approche
            if (!_actionInitiated)
            {
                _reservedStation = FindAvailableStation();

                if (_reservedStation == null)
                {
                    Debug.LogWarning($"<color=orange>[Order]</color> {self.CharacterName} n'a pas pu crafter {_itemToCraft.ItemName} car aucune station libre ou compatible n'a été trouvée.");
                    IsComplete = true;
                    return BTNodeStatus.Failure;
                }

                // Réserver la station pour que personne d'autre ne la prenne pendant le trajet
                _reservedStation.Use(self);

                // Lancer l'action physique
                NPCController npc = self.Controller as NPCController;
                if (npc != null)
                {
                    npc.PushBehaviour(new CraftItemBehaviour(_reservedStation, _itemToCraft));
                }

                _actionInitiated = true;
                Debug.Log($"<color=cyan>[Order]</color> {self.CharacterName} a reçu l'ordre de crafter {_itemToCraft.ItemName}. En route vers {_reservedStation.FurnitureName}.");
            }

            // 2. Vérification pendant que le comportement s'exécute
            // Si la station n'est plus occupée par nous, ou si le Behaviour global est vide, c'est fini
            if (_reservedStation.Occupant != self)
            {
                // L'action est terminée (le CraftItemBehaviour ou le CharacterCraftAction a libéré la station)
                IsComplete = true;
                return BTNodeStatus.Success;
            }

            return BTNodeStatus.Running;
        }

        public override void Cancel(Character self)
        {
            base.Cancel(self);
            if (_reservedStation != null && _reservedStation.Occupant == self)
            {
                _reservedStation.Release();
            }
            Debug.Log($"<color=yellow>[Order]</color> Ordre de craft annulé pour {self.CharacterName}.");
        }

        /// <summary>
        /// Cherche à travers tous les bâtiments locaux la première station qui peut crafter cet objet et qui est libre.
        /// (Si la méthode GetAllCraftingStations existait, on l'utiliserait, sinon on scanne les buildings).
        /// </summary>
        private CraftingStation FindAvailableStation()
        {
            if (BuildingManager.Instance == null) return null;

            foreach (var building in BuildingManager.Instance.allBuildings)
            {
                var stations = building.GetFurnitureOfType<CraftingStation>();
                foreach (var st in stations)
                {
                    if (st.CanCraft(_itemToCraft) && !st.IsOccupied)
                    {
                        return st;
                    }
                }
            }

            return null; // Pas de station trouvée
        }
    }
}
