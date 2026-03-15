using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_LocateItem : GoapAction
    {
        private JobTransporter _job;
        protected bool _isComplete = false;

        public override string ActionName => "Locate Delivery Item";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "itemLocated", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "itemLocated", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_LocateItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.CurrentOrder.Source != null;
        }

        public override void Execute(Character worker)
        {
            if (_job.CurrentOrder == null)
            {
                _isComplete = true;
                return;
            }

            CommercialBuilding source = _job.CurrentOrder.Source;
            Zone zone = source.StorageZone ?? source.MainRoom.GetComponent<Zone>();
            ItemSO wantedSO = _job.CurrentOrder.ItemToTransport;
            
            WorldItem targetWorldItem = null;

            Collider searchZone = zone != null ? zone.GetComponent<Collider>() : source.GetComponent<Collider>();
            
            if (searchZone != null)
            {
                Collider[] colliders = Physics.OverlapBox(searchZone.bounds.center, searchZone.bounds.extents, Quaternion.identity);
                foreach (var col in colliders)
                {
                    var wi = col.GetComponentInParent<WorldItem>();
                    if (wi != null && wi.ItemInstance != null && wi.ItemInstance.ItemSO == wantedSO && !wi.IsBeingCarried)
                    {
                        // On s'assure que ce n'est pas ciblé par un collègue
                        bool alreadyTargeted = false;
                        foreach (var otherJob in source.GetJobsOfType<JobTransporter>())
                        {
                            if (otherJob != _job && otherJob.TargetWorldItem == wi)
                            {
                                alreadyTargeted = true;
                                break;
                            }
                        }

                        // On s'assure que cet objet est bien DANS l'inventaire logique de la source
                        if (!alreadyTargeted && source.GetItemCount(wantedSO) > 0)
                        {
                            targetWorldItem = wi;
                            break;
                        }
                    }
                }
            }

            // Fallback de secours (si pas trouvé dans bounds strict)
            if (targetWorldItem == null)
            {
                WorldItem[] allItems = Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None);
                foreach (var wi in allItems)
                {
                    if (wi.ItemInstance != null && wi.ItemInstance.ItemSO == wantedSO && !wi.IsBeingCarried && Vector3.Distance(wi.transform.position, source.transform.position) < 25f)
                    {
                        bool alreadyTargeted = false;
                        foreach (var otherJob in source.GetJobsOfType<JobTransporter>())
                        {
                            if (otherJob != _job && otherJob.TargetWorldItem == wi)
                            {
                                alreadyTargeted = true;
                                break;
                            }
                        }

                        if (!alreadyTargeted)
                        {
                            targetWorldItem = wi;
                            break;
                        }
                    }
                }
            }

            if (targetWorldItem == null)
            {
                if (_job.CarriedItems.Count > 0)
                {
                    Debug.LogWarning($"<color=orange>[LocateItem]</color> Plus de {wantedSO.ItemName} disponible. {_job.Worker.CharacterName} lance la livraison de son batch partiel ({_job.CarriedItems.Count} items).");
                    // Force GOAP to transition to deliver by short circuiting the locate flag
                    _job.ForceDeliverPartialBatch = true;
                    _isComplete = true;
                    return;
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[LocateItem]</color> Plus de {wantedSO.ItemName} physiquement disponible chez {source.BuildingName}. Annulation de l'ordre.");
                    _job.WaitCooldown = 2f;
                    _job.CancelCurrentOrder();
                    _isComplete = true;
                    return;
                }
            }

            _job.TargetWorldItem = targetWorldItem;
            _isComplete = true;
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
        }
    }
}
