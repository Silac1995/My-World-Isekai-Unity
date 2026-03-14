using UnityEngine;
using System.Collections;

public class PerformTransportBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private JobTransporter _transporterJob;
    private BuyOrder _currentOrder;

    private bool _isFinished = false;
    private bool _isWaiting = false;
    private Coroutine _transportCoroutine;

    private enum TransportPhase
    {
        MovingToSource,
        PickingUp,
        MovingToDestination,
        DroppingOff
    }

    private TransportPhase _currentPhase = TransportPhase.MovingToSource;

    public bool IsFinished => _isFinished;

    public PerformTransportBehaviour(NPCController npcController, JobTransporter transporterJob)
    {
        _npcController = npcController;
        _transporterJob = transporterJob;
        _currentOrder = transporterJob.CurrentOrder;
    }

    public void Enter(Character selfCharacter) { }
    public void Act(Character selfCharacter)
    {
        if (_isFinished || _isWaiting || _currentOrder == null) return;

        var movement = selfCharacter.CharacterMovement;
        if (movement == null) return;

        switch (_currentPhase)
        {
            case TransportPhase.MovingToSource:
                HandleMovementTo(selfCharacter, movement, _currentOrder.Source, TransportPhase.PickingUp);
                break;

            case TransportPhase.PickingUp:
                _transportCoroutine = _npcController.StartCoroutine(WaitAndProceed(selfCharacter, 2f, TransportPhase.MovingToDestination));
                break;

            case TransportPhase.MovingToDestination:
                HandleMovementTo(selfCharacter, movement, _currentOrder.Destination, TransportPhase.DroppingOff);
                break;

            case TransportPhase.DroppingOff:
                _transportCoroutine = _npcController.StartCoroutine(WaitAndFinish(selfCharacter, 2f));
                break;
        }
    }

    private void HandleMovementTo(Character self, CharacterMovement movement, CommercialBuilding building, TransportPhase nextPhase)
    {
        // Choix du point de rendez-vous : idéalement la DeliveryZone ou DepositZone, sinon le centre du bâtiment.
        Vector3 targetPos = building.transform.position;
        if (building.DeliveryZone != null)
        {
            targetPos = building.GetRandomPointInBuildingZone(self.transform.position.y); // ou GetRandomPointInZone() sur la DeliveryZone
            // Pour être plus précis on utiliserait building.DeliveryZone.GetRandomPointInZone() si dispo
        }
        else if (building.BuildingZone != null)
        {
            targetPos = building.GetRandomPointInBuildingZone(self.transform.position.y);
        }

        if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
            if (Vector3.Distance(self.transform.position, targetPos) > movement.StoppingDistance + 1f)
            {
                movement.SetDestination(targetPos);
            }
            else
            {
                // Arrivé à destination
                movement.ResetPath();
                _currentPhase = nextPhase;
            }
        }
    }

    private IEnumerator WaitAndProceed(Character self, float time, TransportPhase nextPhase)
    {
        _isWaiting = true;
        // Simule le chargement de l'objet
        Debug.Log($"<color=cyan>[Transport]</color> {self.CharacterName} charge la marchandise ({_currentOrder.ItemToTransport.ItemName})...");
        yield return new WaitForSeconds(time);
        
        _currentPhase = nextPhase;
        _isWaiting = false;
        _transportCoroutine = null;
    }

    private IEnumerator WaitAndFinish(Character self, float time)
    {
        _isWaiting = true;
        // Simule le déchargement de l'objet
        Debug.Log($"<color=green>[Transport]</color> {self.CharacterName} décharge la marchandise ({_currentOrder.ItemToTransport.ItemName}) à {_currentOrder.Destination.BuildingName} !");
        yield return new WaitForSeconds(time);

        // On notifie le Job qu'un "lot" a été livré (ex: 1 unité par trajet pour l'instant)
        // C'est ici qu'on pourrait ajuster selon la capacité du sac du PNJ
        _transporterJob.NotifyDeliveryProgress(1);

        _isFinished = true;
        _isWaiting = false;
        _transportCoroutine = null;
    }

    public void Exit(Character selfCharacter)
    {
        if (_npcController != null && _transportCoroutine != null)
        {
            _npcController.StopCoroutine(_transportCoroutine);
            _transportCoroutine = null;
        }

        _isWaiting = false;
        selfCharacter.CharacterMovement?.ResetPath();
    }

    public void Terminate() => _isFinished = true;
}
