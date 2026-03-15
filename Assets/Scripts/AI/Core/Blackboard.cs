using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Mémoire partagée du NPC. Stocke les données que les nodes du BT lisent et écrivent.
    /// Utilise un dictionnaire typé avec des clés string pour la flexibilité.
    /// Les clés courantes sont définies comme constantes pour éviter les typos.
    /// </summary>
    public class Blackboard
    {
        // --- Clés prédéfinies ---
        public const string KEY_SELF = "Self";
        public const string KEY_CURRENT_ORDER = "CurrentOrder";
        public const string KEY_DETECTED_CHARACTER = "DetectedCharacter";
        public const string KEY_URGENT_NEED = "UrgentNeed";
        public const string KEY_SCHEDULE_ACTIVITY = "ScheduleActivity";
        public const string KEY_BATTLE_MANAGER = "BattleManager";
        public const string KEY_FLEE_BATTLE_MANAGER = "FleeBattleManager";
        public const string KEY_COMBAT_TARGET = "CombatTarget";
        public const string KEY_SOCIAL_TARGET = "SocialTarget";

        private Dictionary<string, object> _data = new Dictionary<string, object>();

        /// <summary>
        /// Initialise le blackboard avec la référence au personnage.
        /// </summary>
        public Blackboard(Character self)
        {
            Set(KEY_SELF, self);
        }

        /// <summary>
        /// Écrit une valeur dans le blackboard.
        /// </summary>
        public void Set<T>(string key, T value)
        {
            _data[key] = value;
        }

        /// <summary>
        /// Lit une valeur du blackboard. Retourne default(T) si la clé n'existe pas.
        /// </summary>
        public T Get<T>(string key)
        {
            if (_data.TryGetValue(key, out object value) && value is T typedValue)
            {
                return typedValue;
            }
            return default;
        }

        /// <summary>
        /// Vérifie si une clé existe et n'est pas null.
        /// </summary>
        public bool Has(string key)
        {
            return _data.TryGetValue(key, out object value) && value != null;
        }

        /// <summary>
        /// Supprime une clé du blackboard.
        /// </summary>
        public void Remove(string key)
        {
            _data.Remove(key);
        }

        /// <summary>
        /// Vide tout le blackboard sauf Self.
        /// </summary>
        public void Clear()
        {
            Character self = Get<Character>(KEY_SELF);
            _data.Clear();
            if (self != null) Set(KEY_SELF, self);
        }

        // --- Raccourcis pour les clés courantes ---

        public Character Self => Get<Character>(KEY_SELF);
    }
}
