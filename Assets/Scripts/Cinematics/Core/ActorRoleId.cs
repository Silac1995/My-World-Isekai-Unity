using System;

namespace MWI.Cinematics
{
    /// <summary>
    /// Typed wrapper around a string role identifier. Prevents stringly-typed bugs
    /// when wiring step actor fields to RoleSlot.RoleId.
    /// </summary>
    [Serializable]
    public readonly struct ActorRoleId : IEquatable<ActorRoleId>
    {
        public readonly string Value;

        public ActorRoleId(string value) { Value = value; }

        public static readonly ActorRoleId Empty = new ActorRoleId(string.Empty);

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public bool Equals(ActorRoleId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ActorRoleId o && Equals(o);

        public override int GetHashCode() =>
            Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ActorRoleId a, ActorRoleId b) => a.Equals(b);
        public static bool operator !=(ActorRoleId a, ActorRoleId b) => !a.Equals(b);
    }
}
