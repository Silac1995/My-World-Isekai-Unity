using System;

namespace MWI.Economy
{
    /// <summary>
    /// Thin handle for a currency. Placeholder for the future Kingdom system —
    /// today only <see cref="Default"/> exists; later, kingdoms mint additional ids.
    /// </summary>
    [Serializable]
    public struct CurrencyId : IEquatable<CurrencyId>
    {
        public int Id;

        public CurrencyId(int id) { Id = id; }

        public static readonly CurrencyId Default = new CurrencyId(0);

        public bool Equals(CurrencyId other) => Id == other.Id;
        public override bool Equals(object obj) => obj is CurrencyId c && Equals(c);
        public override int GetHashCode() => Id.GetHashCode();
        public static bool operator ==(CurrencyId a, CurrencyId b) => a.Id == b.Id;
        public static bool operator !=(CurrencyId a, CurrencyId b) => a.Id != b.Id;
        public override string ToString() => $"Currency#{Id}";
    }
}
