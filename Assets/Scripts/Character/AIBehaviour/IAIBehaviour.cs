public interface IAIBehaviour
{
    bool IsFinished { get; }
    void Enter(Character character);
    void Act(Character character);
    void Exit(Character character);
    void Terminate();
}
