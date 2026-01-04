public interface IAIBehaviour
{
    void Act(Character character);
    void Exit(Character character); // Appelé juste avant de changer de comportement
}
