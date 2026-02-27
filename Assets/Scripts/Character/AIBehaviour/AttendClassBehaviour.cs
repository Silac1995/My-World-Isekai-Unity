using UnityEngine;

public class AttendClassBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private bool _isFinished = false;
    private bool _isMoving = false;
    private bool _hasArrived = false;
    
    // Le taux auquel l'IA vérifie si la classe est toujours valide
    private float _checkTimer = 0f;
    private float _checkRate = 3f;

    public bool IsFinished => _isFinished;

    public AttendClassBehaviour(NPCController npcController)
    {
        _npcController = npcController;
    }

    public void Act(Character selfCharacter)
    {
        if (_isFinished) return;

        // 1. Vérifier si on est toujours libre de suivre ce cours
        if (!IsCharacterFreeToAttend(selfCharacter))
        {
            _isFinished = true;
            return;
        }

        var mentorship = selfCharacter.CharacterMentorship;
        if (mentorship == null || mentorship.CurrentMentor == null)
        {
            _isFinished = true;
            return;
        }

        // 2. Le mentor donne-t-il son cours actuellement ?
        var mentorMentorship = mentorship.CurrentMentor.CharacterMentorship;
        if (mentorMentorship == null || !mentorMentorship.IsCurrentlyTeaching || mentorMentorship.SpawnedClassZone == null)
        {
            // Le prof n'est pas là ou a fini, la classe est terminée pour l'instant
            _isFinished = true;
            return;
        }

        var classZone = mentorMentorship.SpawnedClassZone;

        // 3. Déplacement vers la zone
        _checkTimer += Time.deltaTime;
        if (!_hasArrived || _checkTimer >= _checkRate)
        {
            _checkTimer = 0f;
            MoveToClass(selfCharacter, classZone);
        }
    }

    private void MoveToClass(Character self, MentorClassZone classZone)
    {
        var movement = self.CharacterMovement;
        if (movement == null) return;

        // Récupérer la place attitrée de cet étudiant
        Vector3 targetPos = classZone.GetStudentSlotPosition(self);
        
        // On calcule la distance (sur le plan XZ) par rapport à sa chaise
        Vector3 selfPosFlat = new Vector3(self.transform.position.x, 0, self.transform.position.z);
        Vector3 targetPosFlat = new Vector3(targetPos.x, 0, targetPos.z);
        float distance = Vector3.Distance(selfPosFlat, targetPosFlat);

        // Si on est arrivé à notre place
        if (distance < 0.5f)
        {
            _hasArrived = true;
            _isMoving = false;
            movement.Stop();
            
            // Toujours se tourner vers le professeur quand on est assis
            Vector3 directionToMentor = classZone.Mentor.transform.position - self.transform.position;
            directionToMentor.y = 0;
            if (directionToMentor.sqrMagnitude > 0.1f)
            {
                movement.transform.rotation = Quaternion.RotateTowards(movement.transform.rotation, Quaternion.LookRotation(directionToMentor), Time.deltaTime * 360f);
            }
            return;
        }

        // Si on n'est pas encore arrivé, on se dirige vers la place
        if (!_isMoving || _hasArrived || Vector3.Distance(movement.Destination, targetPos) > 1.0f)
        {
            movement.SetDestination(targetPos);
            _isMoving = true;
            _hasArrived = false;
        }
    }

    private bool IsCharacterFreeToAttend(Character self)
    {
        // 1. Vérification système (vivant, pas en combat, pas en dialogue forcé)
        if (!self.IsFree()) return false;

        // 2. Vérification Job (S'il est en train de travailler, le job prime)
        if (self.CharacterJob != null && self.CharacterJob.IsWorking) return false;

        // 3. Vérification des Besoins (Si un système de besoins de type faim/sommeil critique existe)
        // if (self.CharacterNeeds != null && self.CharacterNeeds.IsCritical) return false;

        return true;
    }

    public void Exit(Character selfCharacter)
    {
        _isFinished = true;
        _hasArrived = false;
        _isMoving = false;
        selfCharacter.CharacterMovement?.Stop();
    }

    public void Terminate() => _isFinished = true;
}
