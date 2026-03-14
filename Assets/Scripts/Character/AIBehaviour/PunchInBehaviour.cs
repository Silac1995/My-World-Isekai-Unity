using UnityEngine;

/// <summary>
/// Behaviour injecté juste au début d'un WorkBehaviour afin que 
/// le personnage se déplace physiquement dans les limites de la BuildingZone
/// de son bâtiment pour y valider son arrivée via Action_PunchIn.
/// </summary>
public class PunchInBehaviour : IAIBehaviour
{
    private CommercialBuilding _workplace;
    private bool _isFinished = false;
    private bool _isMoving = false;
    private bool _isPunchingIn = false;

    public bool IsFinished => _isFinished;

    public PunchInBehaviour(CommercialBuilding workplace)
    {
        _workplace = workplace;
    }

    public void Enter(Character selfCharacter) { }

    public void Act(Character selfCharacter)
    {
        if (_isFinished) return;

        if (_workplace == null || _workplace.BuildingZone == null)
        {
            _isFinished = true;
            return;
        }

        var movement = selfCharacter.CharacterMovement;
        if (movement == null)
        {
            _isFinished = true;
            return;
        }

        // 1. Lancement de l'action si on est arrivé
        if (_isPunchingIn)
        {
            // L'action est en cours, on attend sa fin. 
            // _isFinished sera mis à true dans le callback OnActionFinished.
            return;
        }

        // 2. Définition de la destination si pas encore en mouvement
        if (!_isMoving)
        {
            Vector3 destination = _workplace.GetRandomPointInBuildingZone(selfCharacter.transform.position.y);
            movement.SetDestination(destination);
            _isMoving = true;
            return;
        }

        // 3. Vérification de l'arrivée physique dans la BuildingZone
        if (!movement.PathPending && (!movement.HasPath || _workplace.BuildingZone.bounds.Contains(selfCharacter.transform.position)))
        {
            TryPunchIn(selfCharacter);
        }
        else if (!movement.PathPending && movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
             // Fallback au cas où le pathfinding échoue à nous mettre EXACTEMENT dans la box (ex: collision mur)
            TryPunchIn(selfCharacter);
        }
    }

    private void TryPunchIn(Character selfCharacter)
    {
        selfCharacter.CharacterMovement?.Stop();

        Action_PunchIn punchInAction = new Action_PunchIn(selfCharacter, _workplace);
        
        // Si l'action est valide (le personnage est bien dans la zone)
        if (punchInAction.CanExecute())
        {
            _isPunchingIn = true;

            punchInAction.OnActionFinished += () => 
            {
                _isFinished = true; // Fin du Behaviour, WorkBehaviour peut prendre la main
            };

            selfCharacter.CharacterActions.ExecuteAction(punchInAction);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[Work]</color> {selfCharacter.CharacterName} a tenté de Punch In mais 'CanExecute' a refusé (hors zone). Recalcul du path.");
            _isMoving = false; // Force la boucle à retenter de trouver un point valide
        }
    }

    public void Exit(Character selfCharacter)
    {
        _isMoving = false;
        _isPunchingIn = false;
        selfCharacter.CharacterMovement?.ResetPath();
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
