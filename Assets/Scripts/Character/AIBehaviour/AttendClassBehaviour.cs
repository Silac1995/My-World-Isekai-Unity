using UnityEngine;

public class AttendClassBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private bool _isFinished = false;
    private bool _isMoving = false;
    private bool _hasArrived = false;
    
    // The rate at which the AI checks whether the class is still valid
    private float _checkTimer = 0f;
    private float _checkRate = 3f;

    public bool IsFinished => _isFinished;

    public AttendClassBehaviour(NPCController npcController)
    {
        _npcController = npcController;
    }

    public void Enter(Character selfCharacter) { }
    public void Act(Character selfCharacter)
    {
        if (_isFinished) return;

        // 1. Check whether we're still free to attend this class
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
            // The teacher isn't there or has finished, the class is over for now
            _isFinished = true;
            return;
        }

        var classZone = mentorMentorship.SpawnedClassZone;

        // 3. Move to the zone
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

        // Fetch this student's assigned seat
        Vector3 targetPos = classZone.GetStudentSlotPosition(self);

        // Compute distance (on the XZ plane) from their chair
        Vector3 selfPosFlat = new Vector3(self.transform.position.x, 0, self.transform.position.z);
        Vector3 targetPosFlat = new Vector3(targetPos.x, 0, targetPos.z);
        float distance = Vector3.Distance(selfPosFlat, targetPosFlat);

        // If we've reached our seat
        if (distance < 0.5f)
        {
            _hasArrived = true;
            _isMoving = false;
            movement.Stop();

            // Always turn towards the teacher once seated
            Vector3 directionToMentor = classZone.Mentor.transform.position - self.transform.position;
            directionToMentor.y = 0;
            if (directionToMentor.sqrMagnitude > 0.1f)
            {
                movement.transform.rotation = Quaternion.RotateTowards(movement.transform.rotation, Quaternion.LookRotation(directionToMentor), Time.deltaTime * 360f);
            }
            return;
        }

        // If we haven't arrived yet, head to the seat
        if (!_isMoving || _hasArrived || Vector3.Distance(movement.Destination, targetPos) > 1.0f)
        {
            movement.SetDestination(targetPos);
            _isMoving = true;
            _hasArrived = false;
        }
    }

    private bool IsCharacterFreeToAttend(Character self)
    {
        // 1. System check (alive, not in combat, not in forced dialogue)
        if (!self.IsFree()) return false;

        // 2. Job check (if they're at work, the job takes priority)
        if (self.CharacterJob != null && self.CharacterJob.IsWorking) return false;

        // 3. Needs check (if a critical hunger/sleep need system exists)
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
