// Assets/Scripts/Character/CharacterOrders/OrderFactory.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Maps OrderTypeName strings to concrete Order instances. Used by the RPC layer
    /// (server reconstructs the live Order from the payload bytes) and by save-load
    /// (deserialize an IssuedOrderSaveEntry back into a live Order).
    ///
    /// Each new concrete order type must self-register by calling Register() in a static
    /// constructor, OR by being added to the static initializer below.
    /// </summary>
    public static class OrderFactory
    {
        private static readonly Dictionary<string, Func<Order>> _factories = new();

        static OrderFactory()
        {
            Register<Order_Leave>("Order_Leave");
            Register<Order_Kill>("Order_Kill");
            // New concrete order types add a Register call here.
        }

        public static void Register<T>(string typeName) where T : Order, new()
        {
            _factories[typeName] = () => new T { OrderTypeName = typeName };
        }

        public static Order Create(string typeName)
        {
            if (_factories.TryGetValue(typeName, out var factory))
            {
                return factory();
            }
            Debug.LogError($"[OrderFactory] Unknown OrderTypeName: {typeName}");
            return null;
        }

        public static IEnumerable<string> RegisteredTypeNames => _factories.Keys;
    }
}
