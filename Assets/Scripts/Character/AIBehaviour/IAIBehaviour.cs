public interface IAIBehaviour
{
    bool IsFinished { get; } // Nouveau : Permet de savoir si le behaviour veut s'arrêter
    void Act(Character character);
    void Exit(Character character);
    void Terminate(); // Nouvelle méthode pour forcer l'arrêt de l'extérieur
}