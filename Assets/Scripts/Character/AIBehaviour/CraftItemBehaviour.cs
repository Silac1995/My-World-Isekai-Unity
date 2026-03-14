using UnityEngine;

public class CraftItemBehaviour : IAIBehaviour
{
    private CraftingStation _station;
    private ItemSO _itemToCraft;
    private bool _isFinished = false;

    // --- TIMEOUT ---
    private float _timeoutTimer = 0f;
    private const float TIMEOUT_DURATION = 15f; // Un peu plus long au cas où il vient de loin

    public bool IsFinished => _isFinished;

    public void Terminate() => _isFinished = true;

    public CraftItemBehaviour(CraftingStation station, ItemSO itemToCraft)
    {
        _station = station;
        _itemToCraft = itemToCraft;
    }

    public void Enter(Character selfCharacter) { }
    public void Act(Character self)
    {
        if (_station == null || _itemToCraft == null)
        {
            _isFinished = true;
            return;
        }

        // Sécurité : au cas où un autre PNJ a volé la station (ne devrait pas arriver avec Use())
        if (_station.IsOccupied && _station.Occupant != self)
        {
            Debug.LogWarning($"<color=orange>[AI]</color> {self.CharacterName} annule son déplacement vers {_station.FurnitureName} qui est occupé.");
            _isFinished = true;
            return;
        }

        // Gestion du Timeout
        _timeoutTimer += Time.deltaTime;
        if (_timeoutTimer > TIMEOUT_DURATION)
        {
            Debug.LogWarning($"<color=orange>[AI]</color> Timeout de déplacement pour {self.CharacterName} vers {_station.FurnitureName}. ABORT.");
            _station.Release();
            _isFinished = true;
            return;
        }

        var movement = self.CharacterMovement;
        if (movement == null) return;

        Vector3 targetPos = _station.GetInteractionPosition();
        
        // Calculer la distance (ignorons le Y pour éviter les bugs de hauteur)
        float distDelta = Vector2.Distance(new Vector2(self.transform.position.x, self.transform.position.z), 
                                           new Vector2(targetPos.x, targetPos.z));

        if (distDelta < 0.25f)
        {
            // Arrivé ! On s'arrête
            movement.Stop();
            
            // On se tourne vers la table (on peut ajuster si besoin)
            if (self.CharacterVisual != null)
            {
                float directionX = _station.transform.position.x > self.transform.position.x ? 1f : -1f;
                // Note: La fonction exacte pour Flip / FaceTarget peut varier, mais beaucoup de scripts 
                // utilisent UpdateLookDirection ou dépendent du Scale directement.
                // On laisse le mouvement s'en charger s'il reste une inertie, ou on le force :
                self.transform.localScale = new Vector3(Mathf.Abs(self.transform.localScale.x) * directionX, self.transform.localScale.y, self.transform.localScale.z);
            }

            // On installe le NPC sur la station avant de lancer l'action
            if (!_station.IsOccupied || _station.Occupant == self)
            {
                _station.Use(self);
                
                // On lance l'action de craft (qui trouvera la station via OccupyingFurniture)
                if (self.CharacterActions != null)
                {
                    self.CharacterActions.ExecuteAction(new CharacterCraftAction(self, _itemToCraft));
                }
            }
            else
            {
                Debug.LogWarning($"<color=orange>[AI]</color> {_station.name} est occupee quand {self.CharacterName} est arrive.");
            }

            _isFinished = true; 
            return; 
        }

        // On continue de bouger
        movement.Resume();
        movement.SetDestination(targetPos);
    }

    public void Exit(Character self)
    {
        self.CharacterMovement?.ResetPath();
        
        // Si le comportement est annulé (préempté) avant d'avoir pu lancer l'action,
        // l'ordre s'en rendra compte car il checke IsOccupied.
    }
}
