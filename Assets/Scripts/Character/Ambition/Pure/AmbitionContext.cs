using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Typed bag of context values shared across an ambition's quest chain. Each step
    /// quest reads/writes via the bag (e.g. Quest_FindLover writes context["Lover"];
    /// Quest_HaveChildWithLover reads it). Set-time validation rejects non-serializable
    /// values so authors hit the wall in editor, not at production save time.
    /// </summary>
    [Serializable]
    public sealed class AmbitionContext
    {
        private readonly Dictionary<string, object> _values = new();

        public T Get<T>(string key)
        {
            if (!_values.TryGetValue(key, out var v))
                throw new KeyNotFoundException($"Ambition context has no key '{key}'.");
            return (T)v;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out var v) && v is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public void Set<T>(string key, T value)
        {
            // Check the runtime type of the value, not the generic parameter T,
            // so ctx.Set<object>("k", someCharacter) classifies as Character.
            var runtimeType = value?.GetType() ?? typeof(T);
            if (!IsSerializableValueKind(runtimeType))
                throw new InvalidOperationException(
                    $"Ambition context value of type {runtimeType.Name} is not serializable. "
                    + "Allowed: primitives, enums, Character, ScriptableObject subclasses, IWorldZone.");
            _values[key] = value;
        }

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public IReadOnlyDictionary<string, object> AsReadOnly() => _values;

        /// <summary>
        /// Returns true if values of the given type are allowed in the ambition context.
        /// The runtime save layer relies on this set; expanding it requires extending
        /// the ContextEntryDTO serialization switch in AmbitionSaveData.
        /// <para>
        /// NOTE: Character and IWorldZone both live in Assembly-CSharp (no separate
        /// asmdef). To avoid a circular dependency from this Pure assembly back to
        /// Assembly-CSharp, we identify them by type FullName rather than via typeof().
        /// The save layer performs the authoritative check after deserialization.
        /// </para>
        /// </summary>
        public static bool IsSerializableValueKind(Type t)
        {
            if (t == null) return false;
            if (t.IsPrimitive) return true;                              // int, float, bool, …
            if (t == typeof(string)) return true;
            if (t.IsEnum) return true;
            if (typeof(ScriptableObject).IsAssignableFrom(t)) return true;
            // Character lives in Assembly-CSharp; identify by name to avoid dependency loop.
            if (t.FullName == "Character") return true;
            // IWorldZone also lives in Assembly-CSharp; walk the interface list by name.
            foreach (var iface in t.GetInterfaces())
            {
                if (iface.FullName == "MWI.WorldSystem.IWorldZone") return true;
            }
            return false;
        }
    }
}
