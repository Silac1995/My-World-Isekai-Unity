using UnityEngine;

public class IdleBehaviour : IAIBehaviour
{
    private bool _isStopped = false;
    private bool _isFinished = false;
    public bool IsFinished => _isFinished;

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        if (!_isStopped)
        {
            self.CharacterMovement?.Stop();
            _isStopped = true;
        }
    }

    public void Exit(Character self)
    {
        _isStopped = false;
    }
}
