using UnityEngine;

public class FollowTargetBehaviour : IAIBehaviour
{
    private Character _targetCharacter;
    private float _followDistance;
    private bool _isFinished = false;

    public bool IsFinished => _isFinished;

    public FollowTargetBehaviour(Character target, float followDistance = 2.5f)
    {
        _targetCharacter = target;
        _followDistance = followDistance;
    }

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        if (_targetCharacter == null || _isFinished) return;

        var controller = self.GetComponent<CharacterGameController>();
        if (controller == null || controller.CharacterMovement == null) return;

        float distance = Vector3.Distance(self.transform.position, _targetCharacter.transform.position);

        if (distance > _followDistance)
        {
            // RÉVEIL FORCÉ : On s'assure que rien ne bloque
            controller.CharacterMovement.ForceResume();
            controller.CharacterMovement.SetDestination(_targetCharacter.transform.position);
        }
        else
        {
            // On est proche, là on a le droit de stopper
            controller.CharacterMovement.Stop();

            Vector3 direction = _targetCharacter.transform.position - self.transform.position;
            self.CharacterVisual?.UpdateFlip(direction);
        }
    }

    public void Exit(Character self)
    {
        var movement = self.GetComponent<CharacterMovement>();
        movement?.Stop();
    }
}