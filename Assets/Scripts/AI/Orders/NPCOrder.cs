using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Classe abstraite pour un ordre donné à un NPC.
    /// Un ordre représente une directive venant d'un joueur ou d'un autre NPC.
    /// L'ordre est exécuté frame par frame via Execute() jusqu'à ce qu'il soit complet.
    /// </summary>
    public abstract class NPCOrder
    {
        public abstract NPCOrderType OrderType { get; }
        public bool IsComplete { get; protected set; }

        /// <summary>
        /// Exécute l'ordre pour une frame. Retourne le status du BT.
        /// </summary>
        public abstract BTNodeStatus Execute(Character self);

        /// <summary>
        /// Appelé quand l'ordre est annulé ou remplacé.
        /// </summary>
        public virtual void Cancel(Character self)
        {
            IsComplete = true;
        }

        /// <summary>
        /// L'ordre est-il encore valide ? (ex: la cible est-elle toujours vivante ?)
        /// </summary>
        public virtual bool IsValid()
        {
            return !IsComplete;
        }
    }
}
