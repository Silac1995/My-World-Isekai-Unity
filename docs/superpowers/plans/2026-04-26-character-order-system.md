# Character Order System — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [docs/superpowers/specs/2026-04-26-character-order-system-design.md](../specs/2026-04-26-character-order-system-design.md)

**Goal:** Build a generic `Order` primitive — a server-authoritative directive one character (or anonymous source) issues to another, with timeout, designer-composable consequences/rewards, multiplayer correctness, and integration with the existing Quest, GOAP, Relation, Combat, and Save systems. Ship two concrete v1 orders (`Order_Kill`, `Order_Leave`) to validate both abstract subclasses (`OrderQuest`, `OrderImmediate`).

**Architecture:** New `CharacterOrders` subsystem on every Character (rule #9 child component). Live `Order` instances exist server-only; clients see `NetworkList<OrderSyncData>`. `OrderQuest` implements `IQuest` and reuses `CharacterQuestLog` snapshot sync. `OrderImmediate` polls a compliance predicate. Authority derived on the fly via `AuthorityResolver` from existing systems (`CharacterJob`, `CharacterParty`, `CharacterRelation`). NPC AI consumes orders via a new `Goal_FollowOrder` whose utility = top active order's priority — drops cleanly into the existing GOAP utility system. Persistence: issuer-side ledger persists in `OrdersSaveData`; receiver-side `OrderQuest`s persist via existing `CharacterQuestLog`; receiver-side `OrderImmediate`s are intentionally transient.

**Tech Stack:** Unity 2D-in-3D, C#, NGO (Netcode for GameObjects), NUnit EditMode tests, GOAP backward-search planner, ScriptableObject-based content.

> **MCP availability note:** All "Run EditMode tests" / "Smoke-test in Play mode" / "Create asset" / "Edit prefab" steps assume Unity MCP is connected. If MCP is offline, the executing worker must pause and hand control back to the user to perform those steps in the Unity Editor manually.

---

## Pre-flight

- [ ] **P1: Read the spec end-to-end** before touching any code.
  Read: [docs/superpowers/specs/2026-04-26-character-order-system-design.md](../specs/2026-04-26-character-order-system-design.md).

- [ ] **P2: Confirm clean working tree.**
  Run: `git status`
  Expected: only the in-flight modifications listed in the brief. No untracked files relevant to orders.

- [ ] **P3: Verify the EditMode test runner works.**
  In Unity Editor → Window → General → Test Runner → EditMode → Run All. Expected: existing tests (`WageCalculatorTests`, `HarvesterCreditCalculatorTests`, `NeedHungerMathTests`) pass. If any fail, stop and investigate before adding new tests on top of broken infra.

- [ ] **P4: Skim the existing files this plan depends on so the implementer knows the surfaces being extended.**

  Read (skim, don't memorize):
  - `Assets/Scripts/Character/Character.cs` (rows 27–83 = the subsystem field block; rows 86–150 = capability registry).
  - `Assets/Scripts/Character/CharacterSystem.cs` (whole file — base class).
  - `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs` (lines 41–135: NetworkList sync + dormant pattern — this is the canonical reference for `CharacterOrders` to mirror).
  - `Assets/Scripts/Character/CharacterInvitation/CharacterInvitation.cs` (whole file — reference for receive-evaluation coroutine, Owner RPC popup, NPC eval formula).
  - `Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs` (whole file — for Loyalty/Aggressivity getters used in NPC eval).
  - `Assets/Scripts/Character/CharacterActions/CharacterAction.cs` (whole file — base for `CharacterAction_IssueOrder`).
  - `Assets/Scripts/Character/Quest/IQuest.cs` (or whatever the actual file is — find it via `Glob: **/IQuest.cs`. Note its signature, target wrappers, and how `CharacterQuestLog` registers/removes quests).
  - `Assets/Scripts/Character/Quest/CharacterQuestLog.cs` (note the public `RegisterQuest` / `RemoveQuest` API used by `OrderQuest`).
  - `Assets/Scripts/AI/GOAP/` (find `GoapGoal.cs` or equivalent; note how a goal exposes `Utility(Character)` and `DesiredWorldState(Character)`. The `Goal_FollowOrder` task adapts to whatever the actual base class is).

  If any of those files don't exist with the assumed names, log the discrepancy in your handoff before starting Task 1.

---

## Phase 1 — Foundation

No gameplay yet — pure plumbing. After this phase the project compiles, NetworkLists serialize, and `AuthorityResolver` returns the right context for known issuer/receiver pairs.

---

## Task 1: `OrderState` + `OrderUrgency` enums

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/OrderEnums.cs`

- [ ] **Step 1.1: Write the file.**

```csharp
// Assets/Scripts/Character/CharacterOrders/OrderEnums.cs
namespace MWI.Orders
{
    /// <summary>
    /// Lifecycle state of an Order. See Order state machine in spec §6.
    /// </summary>
    public enum OrderState : byte
    {
        Pending   = 0, // In _pendingOrdersSync, evaluation coroutine running
        Accepted  = 1, // Transient — immediately becomes Active
        Active    = 2, // In _activeOrdersSync, OnTick + IsComplied polling
        Complied  = 3, // Resolved successfully — rewards fired
        Disobeyed = 4, // Resolved by refusal or timeout — consequences fired
        Cancelled = 5, // Cancelled by issuer — no consequences, no rewards
    }

    /// <summary>
    /// Urgency modifier added to AuthorityContext.BasePriority to compute final Priority.
    /// </summary>
    public enum OrderUrgency : byte
    {
        Routine   = 0,
        Important = 15,
        Urgent    = 25,
        Critical  = 35,
    }
}
```

- [ ] **Step 1.2: Switch to Unity, wait for recompile, verify zero errors.**

- [ ] **Step 1.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/OrderEnums.cs" "Assets/Scripts/Character/CharacterOrders/OrderEnums.cs.meta"
git commit -m "feat(orders): add OrderState + OrderUrgency enums"
```

---

## Task 2: `IOrderIssuer` interface + `Character` implementation

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/IOrderIssuer.cs`
- Modify: `Assets/Scripts/Character/Character.cs` (add interface implementation)

- [ ] **Step 2.1: Create the interface.**

```csharp
// Assets/Scripts/Character/CharacterOrders/IOrderIssuer.cs
namespace MWI.Orders
{
    /// <summary>
    /// Anything that can issue an Order. v1 implementation: Character.
    /// Future: Faction, BuildingOwnerProxy, environmental rules.
    /// Nullable in callers — Order accepts a null issuer for anonymous/system orders.
    /// </summary>
    public interface IOrderIssuer
    {
        /// <summary>Returns the Character behind this issuer, or null for non-character issuers.</summary>
        Character AsCharacter { get; }

        /// <summary>Display name shown in UI / logs.</summary>
        string DisplayName { get; }

        /// <summary>Stable network identifier (NetworkObjectId for characters, 0 for anonymous).</summary>
        ulong IssuerNetId { get; }
    }
}
```

- [ ] **Step 2.2: Make `Character` implement `IOrderIssuer`.**

Open `Assets/Scripts/Character/Character.cs`. Find the class declaration (search: `public class Character : NetworkBehaviour`). Add the interface to the inheritance list:

```csharp
public class Character : NetworkBehaviour, MWI.Orders.IOrderIssuer
```

Then add this region to the class body. Place it after the `Capability Registry` region — search for `#endregion` following `_allCapabilities.Remove(system)` and add after it:

```csharp
#region IOrderIssuer
// ── IOrderIssuer ─────────────────────────────────────────────────
Character MWI.Orders.IOrderIssuer.AsCharacter => this;
string    MWI.Orders.IOrderIssuer.DisplayName => CharacterName;
ulong     MWI.Orders.IOrderIssuer.IssuerNetId => NetworkObject != null ? NetworkObject.NetworkObjectId : 0;
#endregion
```

If `CharacterName` is not the property name on `Character`, search for the public name accessor in `Character.cs` and substitute it (the convention in this project is usually `CharacterName`).

- [ ] **Step 2.3: Compile.**
  Switch to Unity. Console expected: zero errors.

- [ ] **Step 2.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/IOrderIssuer.cs" "Assets/Scripts/Character/CharacterOrders/IOrderIssuer.cs.meta" "Assets/Scripts/Character/Character.cs"
git commit -m "feat(orders): add IOrderIssuer interface + Character impl"
```

---

## Task 3: `AuthorityContextSO` ScriptableObject

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/AuthorityContextSO.cs`

- [ ] **Step 3.1: Write the SO.**

```csharp
// Assets/Scripts/Character/CharacterOrders/AuthorityContextSO.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Defines a relationship "kind" (Stranger, Friend, Employer, Captain, …) with the
    /// base priority an Order from this kind of issuer carries. Resolved at issue time
    /// by AuthorityResolver from the receiver's existing systems (CharacterJob,
    /// CharacterParty, CharacterRelation, future Family/Faction).
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Authority Context", fileName = "Authority_New")]
    public class AuthorityContextSO : ScriptableObject
    {
        [Tooltip("Stable name used as the network identifier. Should match the asset filename suffix (e.g., 'Captain' for Authority_Captain).")]
        [SerializeField] private string _contextName;

        [Tooltip("Base priority (0–100) that orders carrying this context start with. Urgency adds on top.")]
        [Range(0, 100)] [SerializeField] private int _basePriority;

        [Tooltip("If true, this context can issue orders without proximity. v1: always false. Future feature.")]
        [SerializeField] private bool _bypassProximity;

        public string ContextName     => _contextName;
        public int    BasePriority    => _basePriority;
        public bool   BypassProximity => _bypassProximity;
    }
}
```

- [ ] **Step 3.2: Compile.**

- [ ] **Step 3.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/AuthorityContextSO.cs" "Assets/Scripts/Character/CharacterOrders/AuthorityContextSO.cs.meta"
git commit -m "feat(orders): add AuthorityContextSO scriptable object"
```

---

## Task 4: Create the 7 v1 AuthorityContext SO assets

**Files:**
- Create: `Assets/Resources/Data/AuthorityContexts/Authority_Stranger.asset`
- Create: `Assets/Resources/Data/AuthorityContexts/Authority_Friend.asset`
- Create: `Assets/Resources/Data/AuthorityContexts/Authority_Parent.asset`
- Create: `Assets/Resources/Data/AuthorityContexts/Authority_PartyLeader.asset`
- Create: `Assets/Resources/Data/AuthorityContexts/Authority_Employer.asset`
- Create: `Assets/Resources/Data/AuthorityContexts/Authority_Captain.asset`
- Create: `Assets/Resources/Data/AuthorityContexts/Authority_Lord.asset`

- [ ] **Step 4.1: Verify the parent folder exists, create it if not.**

Run: `ls -la "Assets/Resources/Data/AuthorityContexts/" 2>/dev/null || echo "MISSING"`.
If MISSING: in Unity Editor (or via MCP `assets-create-folder`), create folder `Assets/Resources/Data/AuthorityContexts`.

- [ ] **Step 4.2: Create the 7 SO assets via MCP `script-execute` (or hand-create in Unity if MCP unavailable).**

For each context, run a `script-execute` block like the following (adapt name + values per row of the table). Or create them via right-click → Create → MWI → Orders → Authority Context in the Unity Editor and set the fields manually.

| Filename                             | ContextName     | BasePriority | BypassProximity |
|--------------------------------------|-----------------|--------------|-----------------|
| Authority_Stranger.asset             | Stranger        | 20           | false           |
| Authority_Friend.asset               | Friend          | 35           | false           |
| Authority_Parent.asset               | Parent          | 45           | false           |
| Authority_PartyLeader.asset          | PartyLeader     | 50           | false           |
| Authority_Employer.asset             | Employer        | 55           | false           |
| Authority_Captain.asset              | Captain         | 70           | false           |
| Authority_Lord.asset                 | Lord            | 85           | false           |

Sample script-execute body (run once per row, edit the values):

```csharp
using UnityEditor;
using UnityEngine;
using MWI.Orders;

public static class CreateAuthorityAsset
{
    public static void Run()
    {
        var so = ScriptableObject.CreateInstance<AuthorityContextSO>();
        // Set private fields via SerializedObject
        var serialized = new SerializedObject(so);
        serialized.FindProperty("_contextName").stringValue   = "Captain";
        serialized.FindProperty("_basePriority").intValue     = 70;
        serialized.FindProperty("_bypassProximity").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(so, "Assets/Resources/Data/AuthorityContexts/Authority_Captain.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
```

Repeat for the other 6 rows.

- [ ] **Step 4.3: Verify all 7 assets exist.**

Run: `ls "Assets/Resources/Data/AuthorityContexts/"*.asset | wc -l`. Expected: `7`.

- [ ] **Step 4.4: Commit.**

```bash
git add "Assets/Resources/Data/AuthorityContexts/"
git commit -m "feat(orders): add 7 v1 AuthorityContext assets"
```

---

## Task 5: `AuthorityResolver` static helper + tests

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/AuthorityResolver.cs`
- Create: `Assets/Tests/EditMode/Orders/AuthorityResolverTests.cs`
- Modify: `Assets/Tests/EditMode/Orders.asmdef` (create if missing — see step 5.0)

- [ ] **Step 5.0: Ensure the Orders test assembly exists.**

If `Assets/Tests/EditMode/Orders/` doesn't exist, create the folder. The test assembly definition can be the existing `Assets/Tests/EditMode/...asmdef` (find it via `Glob: Assets/Tests/EditMode/*.asmdef` and check whether it includes the new folder by default — if it's a single asmdef at the root of EditMode, new subfolders are auto-included).

- [ ] **Step 5.1: Write `AuthorityResolver`.**

```csharp
// Assets/Scripts/Character/CharacterOrders/AuthorityResolver.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Server-only, stateless static helper. Given an issuer + receiver, derives the
    /// highest-applying AuthorityContextSO from the receiver's existing systems.
    ///
    /// Resolution order (highest BasePriority wins on tie — but in practice each
    /// context kind is mutually exclusive for a given (issuer, receiver) pair):
    ///   1. Lord       — TODO future Faction integration
    ///   2. Captain    — TODO future Faction integration
    ///   3. Employer   — receiver.CharacterJob.Employer == issuer
    ///   4. PartyLeader — receiver.CharacterParty.Leader == issuer
    ///   5. Parent     — TODO future Family integration
    ///   6. Friend     — receiver.CharacterRelation.IsFriend(issuer)
    ///   7. Stranger   — fallback
    ///
    /// Anonymous issuer (null) always resolves to Stranger.
    /// </summary>
    public static class AuthorityResolver
    {
        // Cached SO refs; loaded lazily from Resources/Data/AuthorityContexts.
        private static AuthorityContextSO _stranger;
        private static AuthorityContextSO _friend;
        private static AuthorityContextSO _parent;
        private static AuthorityContextSO _partyLeader;
        private static AuthorityContextSO _employer;
        private static AuthorityContextSO _captain;
        private static AuthorityContextSO _lord;

        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _stranger    = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Stranger");
            _friend      = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Friend");
            _parent      = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Parent");
            _partyLeader = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_PartyLeader");
            _employer    = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Employer");
            _captain     = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Captain");
            _lord        = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Lord");
            _loaded = true;

            if (_stranger == null)
            {
                Debug.LogError("<color=red>[AuthorityResolver]</color> Authority_Stranger asset missing in Resources/Data/AuthorityContexts/. Resolution will return null and break Order issuance.");
            }
        }

        /// <summary>
        /// Resolve the AuthorityContext to apply when 'issuer' issues an order to 'receiver'.
        /// Pure server-side function. Returns the Stranger SO if no other context matches.
        /// Returns null only if the Stranger asset itself is missing.
        /// </summary>
        public static AuthorityContextSO Resolve(IOrderIssuer issuer, Character receiver)
        {
            EnsureLoaded();

            if (issuer == null || issuer.AsCharacter == null || receiver == null)
            {
                return _stranger;
            }

            Character issuerCharacter = issuer.AsCharacter;

            // 1. Lord — future Faction integration; no v1 wiring.
            // 2. Captain — future Faction integration; no v1 wiring.

            // 3. Employer
            if (receiver.CharacterJob != null
                && receiver.CharacterJob.Employer == issuerCharacter
                && _employer != null)
            {
                return _employer;
            }

            // 4. PartyLeader
            if (receiver.CharacterParty != null
                && receiver.CharacterParty.Leader == issuerCharacter
                && _partyLeader != null)
            {
                return _partyLeader;
            }

            // 5. Parent — future Family integration; no v1 wiring.

            // 6. Friend
            if (receiver.CharacterRelation != null
                && receiver.CharacterRelation.IsFriend(issuerCharacter)
                && _friend != null)
            {
                return _friend;
            }

            // 7. Stranger fallback
            return _stranger;
        }

        /// <summary>Test-only reset hook so unit tests can inject mock SOs.</summary>
        internal static void ResetForTests()
        {
            _loaded = false;
            _stranger = _friend = _parent = _partyLeader = _employer = _captain = _lord = null;
        }
    }
}
```

> **Note:** if `CharacterJob` doesn't expose an `Employer` property, look for `EmployerCharacter`, `BossCharacter`, or `CommercialBuilding.Owner` and substitute. Same for `CharacterParty.Leader` (could be `LeaderCharacter`, `PartyLeader`). Find the actual member names via `Glob` + `Read` of `CharacterJob.cs` and `CharacterParty.cs`. Document the substitution in the commit message.

- [ ] **Step 5.2: Write the unit tests.**

```csharp
// Assets/Tests/EditMode/Orders/AuthorityResolverTests.cs
using NUnit.Framework;
using UnityEngine;
using MWI.Orders;

namespace MWI.Tests.Orders
{
    public class AuthorityResolverTests
    {
        [Test]
        public void NullIssuer_ResolvesToStranger()
        {
            // Resolver loads SOs from Resources; this test will only pass once the 7 assets exist.
            var receiver = new GameObject("TestReceiver").AddComponent<Character>();
            try
            {
                var ctx = AuthorityResolver.Resolve(null, receiver);
                Assert.IsNotNull(ctx, "Stranger asset must exist in Resources/Data/AuthorityContexts/");
                Assert.AreEqual("Stranger", ctx.ContextName);
            }
            finally
            {
                Object.DestroyImmediate(receiver.gameObject);
            }
        }

        [Test]
        public void AssetsLoadedFromResources_AllSevenPresent()
        {
            // Sanity: every v1 context asset must load.
            var contexts = new[] { "Stranger", "Friend", "Parent", "PartyLeader", "Employer", "Captain", "Lord" };
            foreach (var name in contexts)
            {
                var so = Resources.Load<AuthorityContextSO>($"Data/AuthorityContexts/Authority_{name}");
                Assert.IsNotNull(so, $"Missing asset: Authority_{name}.asset");
                Assert.AreEqual(name, so.ContextName, $"ContextName field on Authority_{name}.asset must equal '{name}'");
            }
        }

        [Test]
        public void StrangerHasBasePriority20()
        {
            var so = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Stranger");
            Assert.IsNotNull(so);
            Assert.AreEqual(20, so.BasePriority);
        }

        [Test]
        public void LordHasBasePriority85()
        {
            var so = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Lord");
            Assert.IsNotNull(so);
            Assert.AreEqual(85, so.BasePriority);
        }
    }
}
```

> **Note:** the Friend / Employer / PartyLeader resolution branches need a fully-instantiated Character with sub-components, which requires either a play-mode test or a mock harness — both out of scope for this minimal edit-mode suite. The end-to-end resolution paths are validated manually in Task 22 + Task 27.

- [ ] **Step 5.3: Run the tests.**

Unity → Test Runner → EditMode → Run All. Expected: all four `AuthorityResolverTests` pass.
If `NullIssuer_ResolvesToStranger` throws because `Character` requires components on `AddComponent`: replace the test body with one that only validates resource loading (drop the `new GameObject` + `AddComponent<Character>()` entirely and call `Resolve(null, null)` instead — the resolver's null-receiver branch returns the Stranger context).

- [ ] **Step 5.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/AuthorityResolver.cs" "Assets/Scripts/Character/CharacterOrders/AuthorityResolver.cs.meta" "Assets/Tests/EditMode/Orders/"
git commit -m "feat(orders): AuthorityResolver static helper + edit-mode tests"
```

---

## Task 6: `OrderSyncData` + `PendingOrderSyncData` (NetworkList element types)

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/OrderSyncData.cs`
- Create: `Assets/Scripts/Character/CharacterOrders/PendingOrderSyncData.cs`

- [ ] **Step 6.1: Write `OrderSyncData`.**

```csharp
// Assets/Scripts/Character/CharacterOrders/OrderSyncData.cs
using System;
using Unity.Collections;
using Unity.Netcode;

namespace MWI.Orders
{
    /// <summary>
    /// Network-friendly snapshot of an active Order. Single fixed schema; type-specific
    /// payload bytes live in OrderPayload to keep the NetworkList polymorphic-safe.
    /// </summary>
    [Serializable]
    public struct OrderSyncData : INetworkSerializable, IEquatable<OrderSyncData>
    {
        public ulong              OrderId;
        public ulong              IssuerNetId;            // 0 = anonymous
        public ulong              ReceiverNetId;
        public FixedString64Bytes OrderTypeName;          // e.g., "Order_Kill"
        public FixedString32Bytes AuthorityContextName;   // e.g., "Captain"
        public byte               Priority;
        public byte               Urgency;
        public byte               State;
        public float              TimeoutSeconds;
        public float              ElapsedSeconds;
        public bool               IsQuestBacked;
        public ulong              LinkedQuestId;
        public byte[]             OrderPayload;           // Type-specific (target id, zone center, etc.)

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref OrderId);
            serializer.SerializeValue(ref IssuerNetId);
            serializer.SerializeValue(ref ReceiverNetId);
            serializer.SerializeValue(ref OrderTypeName);
            serializer.SerializeValue(ref AuthorityContextName);
            serializer.SerializeValue(ref Priority);
            serializer.SerializeValue(ref Urgency);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref TimeoutSeconds);
            serializer.SerializeValue(ref ElapsedSeconds);
            serializer.SerializeValue(ref IsQuestBacked);
            serializer.SerializeValue(ref LinkedQuestId);

            // byte[] payload — write length then bytes
            int length = OrderPayload?.Length ?? 0;
            serializer.SerializeValue(ref length);
            if (serializer.IsReader && length > 0)
            {
                OrderPayload = new byte[length];
            }
            for (int i = 0; i < length; i++)
            {
                byte b = OrderPayload[i];
                serializer.SerializeValue(ref b);
                if (serializer.IsReader) OrderPayload[i] = b;
            }
        }

        public bool Equals(OrderSyncData other)
        {
            return OrderId == other.OrderId
                && IssuerNetId == other.IssuerNetId
                && ReceiverNetId == other.ReceiverNetId
                && OrderTypeName.Equals(other.OrderTypeName)
                && AuthorityContextName.Equals(other.AuthorityContextName)
                && Priority == other.Priority
                && Urgency == other.Urgency
                && State == other.State
                && TimeoutSeconds == other.TimeoutSeconds
                && ElapsedSeconds == other.ElapsedSeconds
                && IsQuestBacked == other.IsQuestBacked
                && LinkedQuestId == other.LinkedQuestId
                && PayloadsEqual(OrderPayload, other.OrderPayload);
        }

        private static bool PayloadsEqual(byte[] a, byte[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length)   return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
```

- [ ] **Step 6.2: Write `PendingOrderSyncData`.**

```csharp
// Assets/Scripts/Character/CharacterOrders/PendingOrderSyncData.cs
using System;
using Unity.Collections;
using Unity.Netcode;

namespace MWI.Orders
{
    /// <summary>
    /// Slimmer snapshot for in-evaluation orders. No payload, no LinkedQuestId — just
    /// enough to drive the player popup countdown.
    /// </summary>
    [Serializable]
    public struct PendingOrderSyncData : INetworkSerializable, IEquatable<PendingOrderSyncData>
    {
        public ulong              OrderId;
        public ulong              IssuerNetId;
        public ulong              ReceiverNetId;
        public FixedString64Bytes OrderTypeName;
        public byte               Priority;
        public byte               Urgency;
        public float              TimeoutSeconds;
        public float              ElapsedSeconds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref OrderId);
            serializer.SerializeValue(ref IssuerNetId);
            serializer.SerializeValue(ref ReceiverNetId);
            serializer.SerializeValue(ref OrderTypeName);
            serializer.SerializeValue(ref Priority);
            serializer.SerializeValue(ref Urgency);
            serializer.SerializeValue(ref TimeoutSeconds);
            serializer.SerializeValue(ref ElapsedSeconds);
        }

        public bool Equals(PendingOrderSyncData other)
        {
            return OrderId == other.OrderId
                && IssuerNetId == other.IssuerNetId
                && ReceiverNetId == other.ReceiverNetId
                && OrderTypeName.Equals(other.OrderTypeName)
                && Priority == other.Priority
                && Urgency == other.Urgency
                && TimeoutSeconds == other.TimeoutSeconds
                && ElapsedSeconds == other.ElapsedSeconds;
        }
    }
}
```

- [ ] **Step 6.3: Compile.**

- [ ] **Step 6.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/OrderSyncData.cs" "Assets/Scripts/Character/CharacterOrders/OrderSyncData.cs.meta" "Assets/Scripts/Character/CharacterOrders/PendingOrderSyncData.cs" "Assets/Scripts/Character/CharacterOrders/PendingOrderSyncData.cs.meta"
git commit -m "feat(orders): OrderSyncData + PendingOrderSyncData NetworkList elements"
```

---

## Task 7: `OrdersSaveData` + `IssuedOrderSaveEntry`

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/OrdersSaveData.cs`

- [ ] **Step 7.1: Write the save model.**

```csharp
// Assets/Scripts/Character/CharacterOrders/OrdersSaveData.cs
using System;
using System.Collections.Generic;

namespace MWI.Orders
{
    /// <summary>
    /// Persistence root for CharacterOrders. Saves only the issuer-side ledger.
    ///   - Receiver-side OrderQuests are persisted by CharacterQuestLog (they implement IQuest).
    ///   - Receiver-side OrderImmediates are intentionally transient.
    /// See spec §7.
    /// </summary>
    [Serializable]
    public class OrdersSaveData
    {
        /// <summary>Outstanding orders this character has issued and is waiting on.</summary>
        public List<IssuedOrderSaveEntry> issuedOrders = new();
    }

    [Serializable]
    public class IssuedOrderSaveEntry
    {
        public ulong        receiverCharacterId;       // Character.CharacterId, NOT NetworkObjectId
        public string       orderTypeName;
        public string       authorityContextName;
        public byte         urgency;
        public float        timeoutRemaining;
        public byte[]       orderPayload;
        public bool         isQuestBacked;
        public ulong        linkedQuestId;
        public List<string> consequenceSoNames = new();
        public List<string> rewardSoNames      = new();
    }
}
```

- [ ] **Step 7.2: Compile.**

- [ ] **Step 7.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/OrdersSaveData.cs" "Assets/Scripts/Character/CharacterOrders/OrdersSaveData.cs.meta"
git commit -m "feat(orders): OrdersSaveData + IssuedOrderSaveEntry persistence model"
```

---

## Task 8: `Order` abstract base class

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Order.cs`

- [ ] **Step 8.1: Write the base class. (Implementations of OnAccepted/OnResolved come in Tasks 14+15 via subclasses.)**

```csharp
// Assets/Scripts/Character/CharacterOrders/Order.cs
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Abstract server-side runtime object representing an issued directive.
    /// Lives only on the server; clients see OrderSyncData snapshots.
    /// See spec §5 for the full data model.
    /// </summary>
    public abstract class Order
    {
        // Identity
        public ulong  OrderId;
        public string OrderTypeName;            // Stable name, e.g. "Order_Kill"

        // Parties
        public IOrderIssuer Issuer;             // Nullable
        public Character    Receiver;           // Always non-null

        // Authority + priority
        public AuthorityContextSO AuthorityContext;
        public OrderUrgency       Urgency;
        public int                Priority;

        // Lifetime
        public float      TimeoutSeconds;
        public float      ElapsedSeconds;
        public OrderState State;

        // Composition
        public List<IOrderConsequence> Consequences = new();
        public List<IOrderReward>      Rewards      = new();

        // ── Lifecycle hooks (overridden by concrete subclasses) ──

        /// <summary>Pre-flight validity check. Called on the server before issuance.</summary>
        public abstract bool CanIssueAgainst(Character receiver);

        /// <summary>Called immediately after the receiver accepts. Used by OrderQuest to register with the quest log.</summary>
        public abstract void OnAccepted();

        /// <summary>Server-side polled check: did the receiver actually do the thing?</summary>
        public abstract bool IsComplied();

        /// <summary>Called every server frame while State == Active.</summary>
        public virtual void OnTick(float dt)
        {
            ElapsedSeconds += dt;
        }

        /// <summary>Called when the order resolves (Complied / Disobeyed / Cancelled). Used by OrderQuest to clean up the quest log.</summary>
        public abstract void OnResolved(OrderState finalState);

        /// <summary>Type-specific payload (target id, zone center, etc.) → bytes for sync + save.</summary>
        public abstract byte[] SerializeOrderPayload();

        /// <summary>Type-specific payload bytes → fields. Called on save reload.</summary>
        public abstract void DeserializeOrderPayload(byte[] data);

        /// <summary>GOAP world-state precondition this order needs satisfied. e.g. { "TargetIsDead_42": true }.</summary>
        public abstract Dictionary<string, object> GetGoapPrecondition();
    }
}
```

- [ ] **Step 8.2: Compile.**
  Compilation will fail because `IOrderConsequence` and `IOrderReward` don't exist yet — that's expected. Add temporary stubs at the bottom of the file (we'll move them to their own files in Task 12):

```csharp
namespace MWI.Orders
{
    /// <summary>TEMPORARY stub — replaced in Task 12.</summary>
    public interface IOrderConsequence { string SoName { get; } void Apply(Order order, Character receiver, IOrderIssuer issuer); }
    /// <summary>TEMPORARY stub — replaced in Task 12.</summary>
    public interface IOrderReward      { string SoName { get; } void Apply(Order order, Character receiver, IOrderIssuer issuer); }
}
```

Compile again. Expected: zero errors.

- [ ] **Step 8.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Order.cs" "Assets/Scripts/Character/CharacterOrders/Order.cs.meta"
git commit -m "feat(orders): Order abstract base class with stubs for consequences/rewards"
```

---

## Task 9: `CharacterOrders` subsystem skeleton (NetworkLists, Awake, OnNetworkSpawn)

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs`

- [ ] **Step 9.1: Write the skeleton.**

```csharp
// Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Server-authoritative subsystem managing all orders for a single Character.
    /// Both issuer-side (orders this character has issued) and receiver-side (orders
    /// this character has been given). See spec §5 for the public surface and §6 for
    /// the lifecycle.
    /// </summary>
    public class CharacterOrders : CharacterSystem, ICharacterSaveData<OrdersSaveData>
    {
        // ── Inspector ─────────────────────────────────────────────
        [Header("Settings")]
        [Tooltip("Time the receiver 'thinks' before responding to an order (NPC only). Mirrors CharacterInvitation._responseDelay.")]
        [SerializeField] private float _responseDelay = 1.0f;

        [Tooltip("How often (seconds) the server polls IsComplied() on each active order.")]
        [SerializeField] private float _compliancePollInterval = 0.5f;

        // ── Server-side runtime state ─────────────────────────────
        // Live Order instances exist only on the server; clients see *Sync* lists.
        private readonly List<Order> _activeOrdersServer  = new();   // Receiver-side
        private readonly List<Order> _issuedOrdersServer  = new();   // Issuer-side ledger
        private readonly Dictionary<ulong, Order> _ordersByIdServer = new();

        // ── Networked state ───────────────────────────────────────
        private NetworkList<OrderSyncData>        _activeOrdersSync;
        private NetworkList<PendingOrderSyncData> _pendingOrdersSync;

        // ── Server-side bookkeeping ───────────────────────────────
        private ulong _nextOrderIdServer = 1;
        private float _pollAccumulator;

        // ── Events ────────────────────────────────────────────────
        public event Action<Order>             OnOrderReceived;
        public event Action<Order>             OnOrderAccepted;
        public event Action<Order, OrderState> OnOrderResolved;

        // ── Public read-only accessors (server) ───────────────────
        public IReadOnlyList<Order> ActiveOrders => _activeOrdersServer;
        public IReadOnlyList<Order> IssuedOrders => _issuedOrdersServer;

        // ── Public read-only accessors (client) ───────────────────
        /// <summary>Snapshot list visible to all clients.</summary>
        public IReadOnlyList<OrderSyncData> ActiveOrdersSync   => GetSyncList(_activeOrdersSync);
        public IReadOnlyList<PendingOrderSyncData> PendingOrdersSync => GetSyncList(_pendingOrdersSync);

        private static IReadOnlyList<T> GetSyncList<T>(NetworkList<T> list) where T : unmanaged, INetworkSerializable, IEquatable<T>
        {
            if (list == null) return Array.Empty<T>();
            var copy = new T[list.Count];
            for (int i = 0; i < list.Count; i++) copy[i] = list[i];
            return copy;
        }

        // ── Lifecycle ─────────────────────────────────────────────
        protected override void Awake()
        {
            base.Awake();
            _activeOrdersSync  = new NetworkList<OrderSyncData>();
            _pendingOrdersSync = new NetworkList<PendingOrderSyncData>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // Subscribe to list changes for client-side rendering hooks (Task 18 wires UI).
            _activeOrdersSync.OnListChanged  += OnActiveOrdersSyncChanged;
            _pendingOrdersSync.OnListChanged += OnPendingOrdersSyncChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (_activeOrdersSync != null)  _activeOrdersSync.OnListChanged  -= OnActiveOrdersSyncChanged;
            if (_pendingOrdersSync != null) _pendingOrdersSync.OnListChanged -= OnPendingOrdersSyncChanged;
        }

        private void OnActiveOrdersSyncChanged(NetworkListEvent<OrderSyncData> evt)
        {
            // Client-side render hook. Wired in Task 18 (UI) and Task 26 (quest log entry).
        }

        private void OnPendingOrdersSyncChanged(NetworkListEvent<PendingOrderSyncData> evt)
        {
            // Client-side render hook for the pending popup. Wired in Task 18.
        }

        private void Update()
        {
            if (!IsServer) return;
            // Server-side ticking implemented in Task 14.
        }

        // ── ICharacterSaveData<OrdersSaveData> ────────────────────
        // Full implementations come in Task 28. Stubs to satisfy the interface for now.
        public string SaveKey      => "CharacterOrders";
        public int    LoadPriority => 60;

        public OrdersSaveData Serialize() => new OrdersSaveData();
        public void           Deserialize(OrdersSaveData data) { /* Task 28 */ }

        string ICharacterSaveData.SerializeToJson()                 => CharacterSaveDataHelper.SerializeToJson(this);
        void   ICharacterSaveData.DeserializeFromJson(string json)  => CharacterSaveDataHelper.DeserializeFromJson(this, json);

        // ── Public surface that callers need from day one (impls in later tasks) ──
        /// <summary>Server-side helper to issue an Order. Returns the new OrderId. Implemented in Task 14.</summary>
        public ulong IssueOrder(Order order)
        {
            Debug.LogWarning("[CharacterOrders] IssueOrder called but not yet implemented (Task 14).");
            return 0;
        }
    }
}
```

- [ ] **Step 9.2: Compile.**
  Expected: errors only on `ICharacterSaveData` / `CharacterSaveDataHelper` references if those don't exist with those exact names. Find the actual interface via `Glob: **/ICharacterSaveData*.cs` and the helper class. Substitute the actual names. Recompile until zero errors.

- [ ] **Step 9.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs" "Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs.meta"
git commit -m "feat(orders): CharacterOrders subsystem skeleton with NetworkLists"
```

---

## Task 10: Wire `CharacterOrders` into `Character` facade + prefab

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs` (add field, getter, auto-assign)
- Modify: All Character prefabs that need the new subsystem (player + every NPC archetype prefab)

- [ ] **Step 10.1: Add the serialized field and getter to `Character.cs`.**

In the `Sub-Systems` `[Header]` block (around line 45–82), add:

```csharp
    [SerializeField] private MWI.Orders.CharacterOrders _characterOrders;
```

Find the public accessors section (search for `public CharacterRelation CharacterRelation`). Add a matching one:

```csharp
    public MWI.Orders.CharacterOrders CharacterOrders => _characterOrders;
```

Find `Awake()` (search: `protected virtual void Awake()` or `private void Awake()`). Add a fallback resolution at the end:

```csharp
    if (_characterOrders == null) _characterOrders = GetComponentInChildren<MWI.Orders.CharacterOrders>();
```

- [ ] **Step 10.2: Add `CharacterOrders` to each Character prefab as a child GameObject.**

For every Character prefab (player prefab + every NPC archetype prefab — find them via `Glob: Assets/Prefabs/**/Character*.prefab` and `Glob: Assets/Resources/**/Character*.prefab`):

1. Open the prefab via MCP `assets-prefab-open`.
2. Create a new child GameObject named `CharacterOrders` under the root.
3. Add the `MWI.Orders.CharacterOrders` component.
4. The Inspector should auto-assign `_character` via `CharacterSystem.Awake`. Verify the `Character` reference points to the prefab root in Play mode.
5. Drag the new child into the root's `_characterOrders` serialized slot.
6. Save with `assets-prefab-save`. Close with `assets-prefab-close`.

Repeat for every Character prefab.

- [ ] **Step 10.3: Smoke-test in Play mode.**

Open the main scene. Enter Play mode. Spawn a Character. In the Hierarchy, expand it; confirm a `CharacterOrders` child exists with the component attached and the `_character` field populated. Check the Console for any errors.

- [ ] **Step 10.4: Commit.**

```bash
git add "Assets/Scripts/Character/Character.cs"
# Plus every modified prefab
git add "Assets/Prefabs/" "Assets/Resources/"
git commit -m "feat(orders): wire CharacterOrders into Character facade + prefabs"
```

---

## Phase 2 — Consequences & Rewards

After this phase, the Order primitive can apply state changes to the receiver and issuer, with all changes replicating to clients via existing channels (CharacterRelation, CharacterCombat, CharacterStatusManager).

---

## Task 11: Replace `IOrderConsequence` / `IOrderReward` stubs with proper interfaces

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Consequences/IOrderConsequence.cs`
- Create: `Assets/Scripts/Character/CharacterOrders/Rewards/IOrderReward.cs`
- Modify: `Assets/Scripts/Character/CharacterOrders/Order.cs` (remove stubs)

- [ ] **Step 11.1: Create `IOrderConsequence`.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Consequences/IOrderConsequence.cs
namespace MWI.Orders
{
    /// <summary>
    /// Server-side strategy fired when an order resolves as Disobeyed (refused or timed out).
    /// Each implementation is a ScriptableObject; SoName is the stable filename used as the
    /// network identifier in OrdersSaveData and OrderSyncData payload metadata.
    ///
    /// Implementations MUST handle null issuer gracefully (no-op for issuer-dependent effects
    /// like RelationDrop or IssuerAttacks).
    /// </summary>
    public interface IOrderConsequence
    {
        /// <summary>Stable identifier; should equal the SO asset filename without extension.</summary>
        string SoName { get; }

        /// <summary>Server-only. Called once when the order resolves as Disobeyed.</summary>
        void Apply(Order order, Character receiver, IOrderIssuer issuer);
    }
}
```

- [ ] **Step 11.2: Create `IOrderReward`.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Rewards/IOrderReward.cs
namespace MWI.Orders
{
    /// <summary>
    /// Server-side strategy fired when an order resolves as Complied. Symmetric to
    /// IOrderConsequence. Each implementation is a ScriptableObject.
    /// Implementations MUST handle null issuer gracefully.
    /// </summary>
    public interface IOrderReward
    {
        string SoName { get; }
        void Apply(Order order, Character receiver, IOrderIssuer issuer);
    }
}
```

- [ ] **Step 11.3: Remove the temporary stubs from `Order.cs`.**

Open `Assets/Scripts/Character/CharacterOrders/Order.cs`. Delete the `TEMPORARY stub` block at the bottom. Compile. Expected: zero errors.

- [ ] **Step 11.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Consequences/" "Assets/Scripts/Character/CharacterOrders/Rewards/" "Assets/Scripts/Character/CharacterOrders/Order.cs"
git commit -m "feat(orders): IOrderConsequence + IOrderReward interfaces"
```

---

## Task 12: `Consequence_RelationDrop` SO + tuned assets

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_RelationDrop.cs`
- Create: `Assets/Resources/Data/OrderConsequences/Consequence_RelationDrop_Light.asset`
- Create: `Assets/Resources/Data/OrderConsequences/Consequence_RelationDrop_Heavy.asset`

- [ ] **Step 12.1: Write the SO.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_RelationDrop.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Reduces the issuer's relation toward the receiver by a fixed amount. No-ops if
    /// issuer is null (anonymous order).
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Consequences/Relation Drop", fileName = "Consequence_RelationDrop_New")]
    public class Consequence_RelationDrop : ScriptableObject, IOrderConsequence
    {
        [Tooltip("How much the issuer's opinion of the receiver drops. Positive number.")]
        [SerializeField] private int _amount = 10;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (issuer == null || issuer.AsCharacter == null) return;
            if (receiver == null) return;

            var issuerCharacter = issuer.AsCharacter;
            if (issuerCharacter.CharacterRelation == null)
            {
                Debug.LogWarning($"<color=yellow>[Consequence_RelationDrop]</color> Issuer {issuerCharacter.CharacterName} has no CharacterRelation; skipping.");
                return;
            }

            issuerCharacter.CharacterRelation.UpdateRelation(receiver, -_amount);
            Debug.Log($"<color=red>[Order]</color> {receiver.CharacterName} disobeyed {issuerCharacter.CharacterName}: relation -{_amount}");
        }
    }
}
```

- [ ] **Step 12.2: Compile.**

- [ ] **Step 12.3: Create the two tuned assets via MCP `script-execute` (or hand-create in Unity).**

For Light (-10):

```csharp
using UnityEditor;
using UnityEngine;
using MWI.Orders;

public static class CreateRelationDropLight
{
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Data/OrderConsequences"))
        {
            AssetDatabase.CreateFolder("Assets/Resources/Data", "OrderConsequences");
        }
        var so = ScriptableObject.CreateInstance<Consequence_RelationDrop>();
        var serialized = new SerializedObject(so);
        serialized.FindProperty("_amount").intValue = 10;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(so, "Assets/Resources/Data/OrderConsequences/Consequence_RelationDrop_Light.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
```

For Heavy (-30): same script, change the path suffix to `_Heavy` and `_amount` to `30`.

- [ ] **Step 12.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_RelationDrop.cs" "Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_RelationDrop.cs.meta" "Assets/Resources/Data/OrderConsequences/"
git commit -m "feat(orders): Consequence_RelationDrop SO + Light/Heavy assets"
```

---

## Task 13: `Consequence_IssuerAttacks` SO + asset

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_IssuerAttacks.cs`
- Create: `Assets/Resources/Data/OrderConsequences/Consequence_IssuerAttacks.asset`

- [ ] **Step 13.1: Write the SO.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_IssuerAttacks.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// The issuer initiates combat against the receiver. No-ops if issuer is null,
    /// dead, or has no CharacterCombat.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Consequences/Issuer Attacks", fileName = "Consequence_IssuerAttacks")]
    public class Consequence_IssuerAttacks : ScriptableObject, IOrderConsequence
    {
        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (issuer == null || issuer.AsCharacter == null) return;

            var issuerCharacter = issuer.AsCharacter;
            if (!issuerCharacter.IsAlive())
            {
                Debug.Log($"<color=orange>[Consequence_IssuerAttacks]</color> Issuer {issuerCharacter.CharacterName} is dead; cannot retaliate.");
                return;
            }

            if (issuerCharacter.CharacterCombat == null)
            {
                Debug.LogWarning($"<color=yellow>[Consequence_IssuerAttacks]</color> Issuer {issuerCharacter.CharacterName} has no CharacterCombat; skipping.");
                return;
            }

            // The actual API may be SetTarget(receiver), EngageTarget(receiver), or similar.
            // Find the public method on CharacterCombat and substitute. This call is the
            // canonical "I now want to attack X" entry point used elsewhere in the codebase.
            issuerCharacter.CharacterCombat.SetTarget(receiver);

            Debug.Log($"<color=red>[Order]</color> {issuerCharacter.CharacterName} now attacks {receiver.CharacterName} for disobedience.");
        }
    }
}
```

> **Note:** if `CharacterCombat.SetTarget(Character)` doesn't exist with that exact name, find the actual "begin attacking X" method via `Grep: pattern="SetTarget" path="Assets/Scripts/Character/CharacterCombat.cs"`. Common alternates: `EngageTarget`, `AttackTarget`, `BeginCombatWith`. Substitute and document in the commit message.

- [ ] **Step 13.2: Compile.**

- [ ] **Step 13.3: Create the asset.**

```csharp
using UnityEditor;
using UnityEngine;
using MWI.Orders;

public static class CreateIssuerAttacksAsset
{
    public static void Run()
    {
        var so = ScriptableObject.CreateInstance<Consequence_IssuerAttacks>();
        AssetDatabase.CreateAsset(so, "Assets/Resources/Data/OrderConsequences/Consequence_IssuerAttacks.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
```

- [ ] **Step 13.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_IssuerAttacks.cs" "Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_IssuerAttacks.cs.meta" "Assets/Resources/Data/OrderConsequences/Consequence_IssuerAttacks.asset" "Assets/Resources/Data/OrderConsequences/Consequence_IssuerAttacks.asset.meta"
git commit -m "feat(orders): Consequence_IssuerAttacks SO + asset"
```

---

## Task 14: `Consequence_StatusEffect` SO + asset

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_StatusEffect.cs`
- Create: `Assets/Resources/Data/OrderConsequences/Consequence_StatusEffect_Wanted.asset`

- [ ] **Step 14.1: Write the SO.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_StatusEffect.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Applies a configurable StatusEffectSO to the receiver. Works with null issuer
    /// (anonymous orders, e.g., a "no trespassing" sign applying a Wanted status).
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Consequences/Status Effect", fileName = "Consequence_StatusEffect_New")]
    public class Consequence_StatusEffect : ScriptableObject, IOrderConsequence
    {
        [Tooltip("Status effect to apply to the receiver on disobedience.")]
        [SerializeField] private StatusEffectSO _statusEffect;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (receiver == null || _statusEffect == null) return;
            if (receiver.CharacterStatusManager == null)
            {
                Debug.LogWarning($"<color=yellow>[Consequence_StatusEffect]</color> Receiver {receiver.CharacterName} has no CharacterStatusManager; skipping.");
                return;
            }

            // Replace 'AddEffect' / 'ApplyEffect' with the actual API of CharacterStatusManager.
            // Find it via Grep: pattern="public.*Effect" path="Assets/Scripts/Character/CharacterStatusManager.cs"
            receiver.CharacterStatusManager.AddEffect(_statusEffect, issuer?.AsCharacter);

            Debug.Log($"<color=red>[Order]</color> {receiver.CharacterName} gains status '{_statusEffect.name}' for disobedience.");
        }
    }
}
```

> **Note:** if `StatusEffectSO` is in a namespace, add the appropriate `using`. If `CharacterStatusManager.AddEffect(...)` doesn't exist with that signature, find the actual entry point.

- [ ] **Step 14.2: Compile.**

- [ ] **Step 14.3: Create the Wanted-status asset via the Editor.**

This one requires picking a `StatusEffectSO` reference, so doing it in the Editor is faster than `script-execute`:

1. Right-click in `Assets/Resources/Data/OrderConsequences/` → Create → MWI → Orders → Consequences → Status Effect.
2. Rename to `Consequence_StatusEffect_Wanted.asset`.
3. In Inspector, drag a "Wanted" or equivalent existing `StatusEffectSO` into the `_statusEffect` field. If no Wanted status exists yet, leave the field empty for now and create the asset; that's fine — the consequence simply no-ops until populated.

- [ ] **Step 14.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_StatusEffect.cs" "Assets/Scripts/Character/CharacterOrders/Consequences/Consequence_StatusEffect.cs.meta" "Assets/Resources/Data/OrderConsequences/Consequence_StatusEffect_Wanted.asset" "Assets/Resources/Data/OrderConsequences/Consequence_StatusEffect_Wanted.asset.meta"
git commit -m "feat(orders): Consequence_StatusEffect SO + Wanted asset"
```

---

## Task 15: `Reward_RelationGain` SO + Light/Heavy assets

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Rewards/Reward_RelationGain.cs`
- Create: `Assets/Resources/Data/OrderRewards/Reward_RelationGain_Light.asset`
- Create: `Assets/Resources/Data/OrderRewards/Reward_RelationGain_Heavy.asset`

- [ ] **Step 15.1: Write the SO.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Rewards/Reward_RelationGain.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Increases the issuer's opinion of the receiver by a fixed amount. No-ops if
    /// issuer is null.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Rewards/Relation Gain", fileName = "Reward_RelationGain_New")]
    public class Reward_RelationGain : ScriptableObject, IOrderReward
    {
        [Tooltip("How much the issuer's opinion of the receiver gains. Positive number.")]
        [SerializeField] private int _amount = 10;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (issuer == null || issuer.AsCharacter == null) return;
            if (receiver == null) return;

            var issuerCharacter = issuer.AsCharacter;
            if (issuerCharacter.CharacterRelation == null) return;

            issuerCharacter.CharacterRelation.UpdateRelation(receiver, +_amount);
            Debug.Log($"<color=green>[Order]</color> {receiver.CharacterName} obeyed {issuerCharacter.CharacterName}: relation +{_amount}");
        }
    }
}
```

- [ ] **Step 15.2: Compile.**

- [ ] **Step 15.3: Create both assets** (same `script-execute` approach as Task 12, with `_amount = 10` for Light and `30` for Heavy, and paths in `Assets/Resources/Data/OrderRewards/`). Ensure the parent folder exists first.

- [ ] **Step 15.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Rewards/Reward_RelationGain.cs" "Assets/Scripts/Character/CharacterOrders/Rewards/Reward_RelationGain.cs.meta" "Assets/Resources/Data/OrderRewards/"
git commit -m "feat(orders): Reward_RelationGain SO + Light/Heavy assets"
```

---

## Task 16: `Reward_GiveItem` SO + sample asset

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Rewards/Reward_GiveItem.cs`
- Create: `Assets/Resources/Data/OrderRewards/Reward_GiveItem_Sample.asset`

- [ ] **Step 16.1: Write the SO.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Rewards/Reward_GiveItem.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Grants the receiver an item via their inventory. Server-side; relies on existing
    /// inventory replication.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Rewards/Give Item", fileName = "Reward_GiveItem_New")]
    public class Reward_GiveItem : ScriptableObject, IOrderReward
    {
        [Tooltip("Item template to grant to the receiver.")]
        [SerializeField] private ItemSO _item;

        [Tooltip("How many to grant.")]
        [Min(1)] [SerializeField] private int _count = 1;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (receiver == null || _item == null) return;

            // Use the project's standard inventory grant API. Common candidates:
            //   receiver.Inventory.AddItem(_item, _count);
            //   receiver.CharacterEquipment.AddToInventory(_item, _count);
            // Find it via Grep across CharacterEquipment / CharacterInventory / Bag — substitute.
            receiver.Inventory?.AddItem(_item.CreateInstance(), _count);

            Debug.Log($"<color=green>[Order]</color> {receiver.CharacterName} received {_count}x {_item.name}");
        }
    }
}
```

> **Note:** the inventory access path on `Character` may be `Character.Inventory`, `Character.CharacterEquipment.Backpack`, or similar. Find the canonical "give item to character" call used elsewhere (e.g., in loot or quest rewards) and substitute. If the API doesn't accept an `ItemInstance`, adapt accordingly.

- [ ] **Step 16.2: Compile.**

- [ ] **Step 16.3: Create the sample asset in the Editor.**

In Unity: right-click in `Assets/Resources/Data/OrderRewards/` → Create → MWI → Orders → Rewards → Give Item. Rename to `Reward_GiveItem_Sample.asset`. Drop any existing `ItemSO` (e.g., a basic food item) into `_item`. Set count to 1. This asset is a template; production order designers will create their own per-order variants.

- [ ] **Step 16.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Rewards/Reward_GiveItem.cs" "Assets/Scripts/Character/CharacterOrders/Rewards/Reward_GiveItem.cs.meta" "Assets/Resources/Data/OrderRewards/Reward_GiveItem_Sample.asset" "Assets/Resources/Data/OrderRewards/Reward_GiveItem_Sample.asset.meta"
git commit -m "feat(orders): Reward_GiveItem SO + sample asset"
```

---

## Task 17: `Reward_StatusEffect` SO + sample asset

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Rewards/Reward_StatusEffect.cs`
- Create: `Assets/Resources/Data/OrderRewards/Reward_StatusEffect_Sample.asset`

- [ ] **Step 17.1: Write the SO.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Rewards/Reward_StatusEffect.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Applies a positive StatusEffectSO to the receiver on compliance.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Rewards/Status Effect", fileName = "Reward_StatusEffect_New")]
    public class Reward_StatusEffect : ScriptableObject, IOrderReward
    {
        [SerializeField] private StatusEffectSO _statusEffect;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (receiver == null || _statusEffect == null) return;
            if (receiver.CharacterStatusManager == null) return;
            receiver.CharacterStatusManager.AddEffect(_statusEffect, issuer?.AsCharacter);
            Debug.Log($"<color=green>[Order]</color> {receiver.CharacterName} gains reward status '{_statusEffect.name}'");
        }
    }
}
```

- [ ] **Step 17.2: Compile.**

- [ ] **Step 17.3: Create the sample asset in the Editor** (same procedure as Task 14.3, in `Assets/Resources/Data/OrderRewards/`).

- [ ] **Step 17.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Rewards/Reward_StatusEffect.cs" "Assets/Scripts/Character/CharacterOrders/Rewards/Reward_StatusEffect.cs.meta" "Assets/Resources/Data/OrderRewards/Reward_StatusEffect_Sample.asset" "Assets/Resources/Data/OrderRewards/Reward_StatusEffect_Sample.asset.meta"
git commit -m "feat(orders): Reward_StatusEffect SO + sample asset"
```

---

## Phase 3 — `OrderImmediate` + `Order_Leave` end-to-end

After this phase, an NPC or player can issue `Order_Leave` to another character; the receiver evaluates, accepts/refuses, and the consequences fire. Validates Host↔Client and Client↔NPC scenarios.

---

## Task 18: `OrderImmediate` abstract subclass

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/OrderImmediate.cs`

- [ ] **Step 18.1: Write the subclass.**

```csharp
// Assets/Scripts/Character/CharacterOrders/OrderImmediate.cs
namespace MWI.Orders
{
    /// <summary>
    /// Order with no objective tracked in the quest log; the receiver's behavior is
    /// polled directly via IsComplied(). Examples: "Leave this area", "Drop your weapon",
    /// "Halt", "Stop attacking".
    /// </summary>
    public abstract class OrderImmediate : Order
    {
        // No quest log integration. Resolution = whether IsComplied() returns true
        // before the timeout expires.
        public override void OnAccepted() { /* no-op */ }
        public override void OnResolved(OrderState finalState) { /* no log cleanup */ }
    }
}
```

- [ ] **Step 18.2: Compile.**

- [ ] **Step 18.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/OrderImmediate.cs" "Assets/Scripts/Character/CharacterOrders/OrderImmediate.cs.meta"
git commit -m "feat(orders): OrderImmediate abstract subclass"
```

---

## Task 19: `Order_Leave` concrete order

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Concrete/Order_Leave.cs`

- [ ] **Step 19.1: Write the concrete order.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Concrete/Order_Leave.cs
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// "Leave this area within N seconds." Compliance = receiver outside the sphere
    /// defined by (zoneCenter, zoneRadius). Optional zoneEntityId references an
    /// IWorldZone for richer integration; if 0, the order is a free-floating sphere.
    /// </summary>
    public class Order_Leave : OrderImmediate
    {
        public Vector3 ZoneCenter;
        public float   ZoneRadius;
        public ulong   ZoneEntityId;          // 0 if not tied to a specific IWorldZone

        public override bool CanIssueAgainst(Character receiver)
        {
            if (receiver == null) return false;
            return Vector3.Distance(receiver.transform.position, ZoneCenter) <= ZoneRadius;
        }

        public override bool IsComplied()
        {
            if (Receiver == null) return true; // dead/despawned receivers are "compliant"
            return Vector3.Distance(Receiver.transform.position, ZoneCenter) > ZoneRadius;
        }

        public override Dictionary<string, object> GetGoapPrecondition()
        {
            // GOAP key the planner looks at to know "agent must be outside zone X".
            // Goap_MoveToPosition (or equivalent) should satisfy this when the world-state
            // evaluator computes distance-from-center against the receiver's position.
            return new Dictionary<string, object>
            {
                { $"OutsideZone_{ZoneEntityId}", true }
            };
        }

        public override byte[] SerializeOrderPayload()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(ZoneCenter.x);
            bw.Write(ZoneCenter.y);
            bw.Write(ZoneCenter.z);
            bw.Write(ZoneRadius);
            bw.Write(ZoneEntityId);
            return ms.ToArray();
        }

        public override void DeserializeOrderPayload(byte[] data)
        {
            if (data == null || data.Length < 24) return;
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            float x = br.ReadSingle();
            float y = br.ReadSingle();
            float z = br.ReadSingle();
            ZoneCenter   = new Vector3(x, y, z);
            ZoneRadius   = br.ReadSingle();
            ZoneEntityId = br.ReadUInt64();
        }
    }
}
```

- [ ] **Step 19.2: Compile.**

- [ ] **Step 19.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Concrete/Order_Leave.cs" "Assets/Scripts/Character/CharacterOrders/Concrete/Order_Leave.cs.meta"
git commit -m "feat(orders): Order_Leave concrete OrderImmediate"
```

---

## Task 20: `CharacterOrders.IssueOrder` server-side path + state machine

**Files:**
- Modify: `Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs`

- [ ] **Step 20.1: Replace the stub `IssueOrder` with the real implementation. Add a server-side `Update` ticker, an evaluation coroutine, a `ResolveOrder` helper, and a `CancelIssuedOrder` method.**

Open `Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs`. Replace the stub `IssueOrder` (`Debug.LogWarning` placeholder) and the empty `Update` body with the following block. Keep all existing fields and lifecycle methods.

```csharp
        // ── Issuance (server-side) ───────────────────────────────────
        /// <summary>Server-side helper to issue an Order. Returns the new OrderId, or 0 on rejection.</summary>
        public ulong IssueOrder(Order order)
        {
            if (!IsServer)
            {
                Debug.LogError("[CharacterOrders] IssueOrder called on non-server. This subsystem is server-authoritative.");
                return 0;
            }
            if (order == null || order.Receiver == null)
            {
                Debug.LogError("[CharacterOrders] IssueOrder called with null order or receiver.");
                return 0;
            }

            try
            {
                // Pre-flight
                if (!order.CanIssueAgainst(order.Receiver))
                {
                    Debug.Log($"<color=orange>[Order]</color> {order.OrderTypeName} rejected: CanIssueAgainst returned false (issuer={order.Issuer?.DisplayName ?? "anonymous"}, receiver={order.Receiver.CharacterName}).");
                    return 0;
                }

                // Proximity check (rule #33-adjacent: long-range orders deferred to v2)
                bool requiresProximity = !(order.AuthorityContext != null && order.AuthorityContext.BypassProximity);
                if (requiresProximity && order.Issuer != null && order.Issuer.AsCharacter != null)
                {
                    var issuerChar = order.Issuer.AsCharacter;
                    if (order.Receiver.CharacterInteractable == null
                        || !order.Receiver.CharacterInteractable.IsCharacterInInteractionZone(issuerChar))
                    {
                        Debug.Log($"<color=orange>[Order]</color> {order.OrderTypeName} rejected: issuer {issuerChar.CharacterName} not in receiver's interaction zone.");
                        return 0;
                    }
                }

                order.OrderId = _nextOrderIdServer++;
                order.State = OrderState.Pending;

                // Dispatch to the receiver's CharacterOrders for evaluation
                var receiverOrders = order.Receiver.CharacterOrders;
                if (receiverOrders == null)
                {
                    Debug.LogError($"[Order] Receiver {order.Receiver.CharacterName} has no CharacterOrders subsystem.");
                    return 0;
                }
                receiverOrders.ReceiveOrder(order);

                // Track on the issuer side (this CharacterOrders instance might be the receiver itself if NPC self-issues)
                if (order.Issuer != null && order.Issuer.AsCharacter != null)
                {
                    var issuerOrders = order.Issuer.AsCharacter.CharacterOrders;
                    if (issuerOrders != null)
                    {
                        issuerOrders._issuedOrdersServer.Add(order);
                        issuerOrders._ordersByIdServer[order.OrderId] = order;
                    }
                }

                return order.OrderId;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return 0;
            }
        }

        /// <summary>Server-side: receive an order and start evaluation.</summary>
        internal void ReceiveOrder(Order order)
        {
            if (!IsServer) return;
            try
            {
                _ordersByIdServer[order.OrderId] = order;

                _pendingOrdersSync.Add(new PendingOrderSyncData
                {
                    OrderId        = order.OrderId,
                    IssuerNetId    = order.Issuer?.IssuerNetId ?? 0,
                    ReceiverNetId  = order.Receiver.NetworkObject != null ? order.Receiver.NetworkObject.NetworkObjectId : 0,
                    OrderTypeName  = order.OrderTypeName,
                    Priority       = (byte)order.Priority,
                    Urgency        = (byte)order.Urgency,
                    TimeoutSeconds = order.TimeoutSeconds,
                    ElapsedSeconds = 0f,
                });

                OnOrderReceived?.Invoke(order);
                StartCoroutine(EvaluateOrderRoutine(order));
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private System.Collections.IEnumerator EvaluateOrderRoutine(Order order)
        {
            // NPC evaluation — server-side, blocking on _responseDelay then computing accept score
            // Player evaluation — Owner RPC popup; the player's response triggers ResolvePlayerOrder
            if (_character == null || !_character.IsPlayer())
            {
                yield return new WaitForSeconds(_responseDelay);
                bool accepted = EvaluateNpcAcceptance(order);
                ApplyEvaluationResult(order, accepted);
                yield break;
            }

            // Player receiver path — fire the Owner RPC popup and wait
            ShowOrderPromptRpc(new PendingOrderSyncData
            {
                OrderId        = order.OrderId,
                IssuerNetId    = order.Issuer?.IssuerNetId ?? 0,
                ReceiverNetId  = order.Receiver.NetworkObject != null ? order.Receiver.NetworkObject.NetworkObjectId : 0,
                OrderTypeName  = order.OrderTypeName,
                Priority       = (byte)order.Priority,
                Urgency        = (byte)order.Urgency,
                TimeoutSeconds = order.TimeoutSeconds,
                ElapsedSeconds = 0f,
            });

            float waitElapsed = 0f;
            while (waitElapsed < order.TimeoutSeconds && order.State == OrderState.Pending)
            {
                waitElapsed += Time.deltaTime;
                yield return null;
            }

            if (order.State == OrderState.Pending)
            {
                // Player didn't respond — auto-refuse
                ApplyEvaluationResult(order, false);
            }
        }

        private bool EvaluateNpcAcceptance(Order order)
        {
            float score = 0.5f
                        + (order.Priority - 50f) / 100f;

            if (_character.CharacterRelation != null && order.Issuer?.AsCharacter != null)
            {
                if (_character.CharacterRelation.IsFriend(order.Issuer.AsCharacter)) score += 0.2f;
                else if (_character.CharacterRelation.IsEnemy(order.Issuer.AsCharacter)) score -= 0.4f;
            }

            if (_character.CharacterTraits != null)
            {
                score += (_character.CharacterTraits.GetLoyalty()      - 0.5f) * 0.3f;
                score -= (_character.CharacterTraits.GetAggressivity() - 0.5f) * 0.2f;
            }

            // Personality compatibility filter (matches CharacterRelation.UpdateRelation logic)
            if (_character.CharacterProfile != null && order.Issuer?.AsCharacter?.CharacterProfile != null)
            {
                int compat = _character.CharacterProfile.GetCompatibilityWith(order.Issuer.AsCharacter.CharacterProfile);
                if (compat > 0) score += 0.1f;
                else if (compat < 0) score -= 0.1f;
            }

            score = Mathf.Clamp01(score);
            bool accepted = UnityEngine.Random.value < score;
            Debug.Log($"<color=cyan>[Order]</color> {_character.CharacterName} evaluates {order.OrderTypeName} from {order.Issuer?.DisplayName ?? "anonymous"} (P={order.Priority}): score={score:F2} → {(accepted ? "ACCEPTED" : "REFUSED")}");
            return accepted;
        }

        private void ApplyEvaluationResult(Order order, bool accepted)
        {
            try
            {
                RemovePendingSync(order.OrderId);

                if (!accepted)
                {
                    order.State = OrderState.Disobeyed;
                    FireConsequences(order);
                    OnOrderResolved?.Invoke(order, OrderState.Disobeyed);
                    _ordersByIdServer.Remove(order.OrderId);
                    return;
                }

                order.State = OrderState.Accepted;
                order.OnAccepted();
                order.State = OrderState.Active;
                _activeOrdersServer.Add(order);
                _activeOrdersSync.Add(BuildSyncData(order));
                OnOrderAccepted?.Invoke(order);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private OrderSyncData BuildSyncData(Order order)
        {
            return new OrderSyncData
            {
                OrderId             = order.OrderId,
                IssuerNetId         = order.Issuer?.IssuerNetId ?? 0,
                ReceiverNetId       = order.Receiver.NetworkObject != null ? order.Receiver.NetworkObject.NetworkObjectId : 0,
                OrderTypeName       = order.OrderTypeName,
                AuthorityContextName = order.AuthorityContext != null ? order.AuthorityContext.ContextName : "Stranger",
                Priority            = (byte)order.Priority,
                Urgency             = (byte)order.Urgency,
                State               = (byte)order.State,
                TimeoutSeconds      = order.TimeoutSeconds,
                ElapsedSeconds      = order.ElapsedSeconds,
                IsQuestBacked       = order is OrderQuest,
                LinkedQuestId       = order is OrderQuest oq ? oq.LinkedQuestId : 0,
                OrderPayload        = order.SerializeOrderPayload(),
            };
        }

        private void RemovePendingSync(ulong orderId)
        {
            for (int i = _pendingOrdersSync.Count - 1; i >= 0; i--)
            {
                if (_pendingOrdersSync[i].OrderId == orderId)
                {
                    _pendingOrdersSync.RemoveAt(i);
                    return;
                }
            }
        }

        private void RemoveActiveSync(ulong orderId)
        {
            for (int i = _activeOrdersSync.Count - 1; i >= 0; i--)
            {
                if (_activeOrdersSync[i].OrderId == orderId)
                {
                    _activeOrdersSync.RemoveAt(i);
                    return;
                }
            }
        }

        private void FireConsequences(Order order)
        {
            if (order.Consequences == null) return;
            foreach (var c in order.Consequences)
            {
                if (c == null) continue;
                try { c.Apply(order, order.Receiver, order.Issuer); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        private void FireRewards(Order order)
        {
            if (order.Rewards == null) return;
            foreach (var r in order.Rewards)
            {
                if (r == null) continue;
                try { r.Apply(order, order.Receiver, order.Issuer); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        // ── Server-side ticking ──────────────────────────────────────
        private new void Update()
        {
            if (!IsServer) return;
            float dt = Time.deltaTime;
            _pollAccumulator += dt;
            bool poll = _pollAccumulator >= _compliancePollInterval;
            if (poll) _pollAccumulator = 0f;

            for (int i = _activeOrdersServer.Count - 1; i >= 0; i--)
            {
                var order = _activeOrdersServer[i];
                if (order == null) { _activeOrdersServer.RemoveAt(i); continue; }
                order.OnTick(dt);

                if (poll && order.IsComplied())
                {
                    ResolveActive(order, OrderState.Complied);
                    continue;
                }

                if (order.ElapsedSeconds >= order.TimeoutSeconds)
                {
                    ResolveActive(order, OrderState.Disobeyed);
                }
            }
        }

        private void ResolveActive(Order order, OrderState finalState)
        {
            try
            {
                _activeOrdersServer.Remove(order);
                RemoveActiveSync(order.OrderId);
                _ordersByIdServer.Remove(order.OrderId);

                order.State = finalState;
                order.OnResolved(finalState);

                if (finalState == OrderState.Complied) FireRewards(order);
                else if (finalState == OrderState.Disobeyed) FireConsequences(order);
                // Cancelled = neither

                OnOrderResolved?.Invoke(order, finalState);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ── Player resolution path (called from ResolvePlayerOrderServerRpc, Task 21) ──
        internal void ResolvePlayerOrderInternal(ulong orderId, bool accept)
        {
            if (!IsServer) return;
            if (!_ordersByIdServer.TryGetValue(orderId, out var order)) return;
            if (order.State != OrderState.Pending) return;
            ApplyEvaluationResult(order, accept);
        }

        // ── Issuer cancellation ──────────────────────────────────────
        public bool CancelIssuedOrder(ulong orderId)
        {
            if (!IsServer) return false;
            if (!_ordersByIdServer.TryGetValue(orderId, out var order)) return false;

            // Find the receiver-side instance and resolve as Cancelled
            var receiverOrders = order.Receiver?.CharacterOrders;
            if (receiverOrders == null) return false;
            receiverOrders.ResolveActive(order, OrderState.Cancelled);
            _issuedOrdersServer.Remove(order);
            _ordersByIdServer.Remove(orderId);
            return true;
        }

        // ── GOAP accessor ───────────────────────────────────────────
        public Order GetTopActiveOrder()
        {
            Order top = null;
            for (int i = 0; i < _activeOrdersServer.Count; i++)
            {
                var o = _activeOrdersServer[i];
                if (top == null || o.Priority > top.Priority) top = o;
            }
            return top;
        }
```

**Important:** since `Update` is now declared explicitly (not the empty placeholder), remove the placeholder. The existing `private void Update()` method in the skeleton from Task 9 should be deleted entirely — replaced by the `private new void Update()` above, or just renamed to `private void Update()` if there's no naming clash with the base class.

Actually the safer approach: keep the method signature `private void Update()` (Unity will call it via reflection regardless of the `new` keyword). Use `private void Update()` and ensure the method body matches the implementation above.

- [ ] **Step 20.2: Compile.**
  Expected errors will reveal any naming mismatches (e.g., `CharacterRelation.IsEnemy` not existing, `CharacterTraits.GetLoyalty` not existing). Fix each by finding the actual API.

- [ ] **Step 20.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs"
git commit -m "feat(orders): IssueOrder + evaluation coroutine + ticking + consequence/reward firing"
```

---

## Task 21: Player UI prompt RPCs

**Files:**
- Modify: `Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs` (add RPCs)

- [ ] **Step 21.1: Add the Owner RPC and the player-response ServerRpc.**

Open `Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs`. Add these methods (place just after `ResolvePlayerOrderInternal`):

```csharp
        // ── Player UI hooks (Owner-side) ─────────────────────────────
        /// <summary>Server → Owning client: render the order prompt UI.</summary>
        [Rpc(SendTo.Owner)]
        public void ShowOrderPromptRpc(PendingOrderSyncData data)
        {
            // Client-side: invoke the UI layer. Wired by UI_OrderImmediatePopup in Task 22
            // (subscribed via OnOrderPromptShown event below).
            OnOrderPromptShown?.Invoke(data);
        }

        /// <summary>Client-side event the UI subscribes to. Fired on the owning client only.</summary>
        public event System.Action<PendingOrderSyncData> OnOrderPromptShown;

        /// <summary>Owning client → server: the player's accept/refuse response.</summary>
        [Rpc(SendTo.Server)]
        public void ResolvePlayerOrderServerRpc(ulong orderId, bool accept, RpcParams rpcParams = default)
        {
            // Authority check: only the receiver's owner can answer their own orders
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning($"[Order] ResolvePlayerOrderServerRpc rejected: sender {rpcParams.Receive.SenderClientId} != owner {OwnerClientId}.");
                return;
            }
            ResolvePlayerOrderInternal(orderId, accept);
        }
```

- [ ] **Step 21.2: Add the issuance ServerRpc for player-initiated orders.**

Append the following after the RPCs above. This RPC is the player-side entry point used by `CharacterAction_IssueOrder` in Task 24:

```csharp
        /// <summary>Owning client → server: issue an order from this Character.</summary>
        [Rpc(SendTo.Server)]
        public void IssueOrderServerRpc(
            ulong receiverNetId,
            FixedString64Bytes orderTypeName,
            byte urgency,
            byte[] orderPayload,
            string[] consequenceSoNames,
            string[] rewardSoNames,
            float timeoutSeconds,
            RpcParams rpcParams = default)
        {
            // Authority check
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning($"[Order] IssueOrderServerRpc rejected: sender {rpcParams.Receive.SenderClientId} != issuer owner {OwnerClientId}.");
                return;
            }

            try
            {
                if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(receiverNetId, out var receiverObj))
                {
                    Debug.LogError($"[Order] IssueOrderServerRpc: receiver NetId {receiverNetId} not found.");
                    return;
                }
                var receiver = receiverObj.GetComponent<Character>();
                if (receiver == null) return;

                var order = OrderFactory.Create(orderTypeName.ToString());
                if (order == null)
                {
                    Debug.LogError($"[Order] Unknown OrderTypeName: {orderTypeName}");
                    return;
                }

                order.OrderTypeName    = orderTypeName.ToString();
                order.Issuer           = _character;     // The owner of this CharacterOrders is the issuer
                order.Receiver         = receiver;
                order.Urgency          = (OrderUrgency)urgency;
                order.AuthorityContext = AuthorityResolver.Resolve(_character, receiver);
                order.Priority         = Mathf.Clamp((order.AuthorityContext != null ? order.AuthorityContext.BasePriority : 20) + (int)order.Urgency, 0, 100);
                order.TimeoutSeconds   = timeoutSeconds;
                order.DeserializeOrderPayload(orderPayload);

                // Resolve consequence + reward SOs by filename
                if (consequenceSoNames != null)
                {
                    foreach (var n in consequenceSoNames)
                    {
                        var so = Resources.Load<ScriptableObject>($"Data/OrderConsequences/{n}");
                        if (so is IOrderConsequence c) order.Consequences.Add(c);
                    }
                }
                if (rewardSoNames != null)
                {
                    foreach (var n in rewardSoNames)
                    {
                        var so = Resources.Load<ScriptableObject>($"Data/OrderRewards/{n}");
                        if (so is IOrderReward r) order.Rewards.Add(r);
                    }
                }

                IssueOrder(order);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }
```

- [ ] **Step 21.3: Add the missing `using Unity.Collections;` if not already present at the top of `CharacterOrders.cs`.**

- [ ] **Step 21.4: Compile.**
  Expected: error on `OrderFactory` (introduced next task). Leave the IssueOrderServerRpc disabled with a stub if needed (return early), or proceed to Task 22 immediately to define `OrderFactory`.

- [ ] **Step 21.5: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs"
git commit -m "feat(orders): ShowOrderPrompt + ResolvePlayerOrder + IssueOrder RPCs"
```

---

## Task 22: `OrderFactory` for type-name → instance dispatch

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/OrderFactory.cs`

- [ ] **Step 22.1: Write the factory.**

```csharp
// Assets/Scripts/Character/CharacterOrders/OrderFactory.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Maps OrderTypeName strings → concrete Order instances. Used by the RPC layer
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
```

- [ ] **Step 22.2: Compile.**

- [ ] **Step 22.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/OrderFactory.cs" "Assets/Scripts/Character/CharacterOrders/OrderFactory.cs.meta"
git commit -m "feat(orders): OrderFactory for type-name → instance dispatch"
```

---

## Task 23: `UI_OrderImmediatePopup` prefab + script

**Files:**
- Create: `Assets/Scripts/UI/Order/UI_OrderImmediatePopup.cs`
- Create: `Assets/UI/Order/UI_OrderImmediatePopup.prefab`

- [ ] **Step 23.1: Locate the existing invitation popup prefab as a template.**

Find via `Glob: Assets/UI/**/UI_Invitation*.prefab` and the matching script via `Glob: Assets/Scripts/UI/**/UI_Invitation*.cs`. Copy the prefab to `Assets/UI/Order/UI_OrderImmediatePopup.prefab` (in Unity, Ctrl+D and rename, then move). Copy the script to `Assets/Scripts/UI/Order/UI_OrderImmediatePopup.cs` and rename the class.

- [ ] **Step 23.2: Adapt the script.**

Replace the script body with the following. The exact widget references depend on the original invitation popup; match its TMP_Text / Button / Slider structure.

```csharp
// Assets/Scripts/UI/Order/UI_OrderImmediatePopup.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using MWI.Orders;

namespace MWI.UI.Orders
{
    /// <summary>
    /// Player-side popup shown when this client's owned Character receives an
    /// OrderImmediate. Mirrors UI_InvitationPopup. Subscribes to the local
    /// CharacterOrders.OnOrderPromptShown event on the owning Character.
    /// </summary>
    public class UI_OrderImmediatePopup : MonoBehaviour
    {
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Button   _acceptButton;
        [SerializeField] private Button   _refuseButton;
        [SerializeField] private Slider   _timerBar;
        [SerializeField] private GameObject _root;

        private CharacterOrders _watchedOrders;
        private ulong _currentOrderId;
        private float _timerStart;
        private float _timeoutSeconds;
        private bool  _active;

        private void Awake()
        {
            _root.SetActive(false);
            _acceptButton.onClick.AddListener(() => Respond(true));
            _refuseButton.onClick.AddListener(() => Respond(false));
        }

        private void OnDestroy()
        {
            if (_watchedOrders != null) _watchedOrders.OnOrderPromptShown -= HandlePrompt;
            _acceptButton.onClick.RemoveAllListeners();
            _refuseButton.onClick.RemoveAllListeners();
        }

        /// <summary>Bind to the local player's CharacterOrders. Call once when the player Character spawns/owns.</summary>
        public void BindToLocalPlayer(CharacterOrders orders)
        {
            if (_watchedOrders != null) _watchedOrders.OnOrderPromptShown -= HandlePrompt;
            _watchedOrders = orders;
            if (_watchedOrders != null) _watchedOrders.OnOrderPromptShown += HandlePrompt;
        }

        private void HandlePrompt(PendingOrderSyncData data)
        {
            _currentOrderId = data.OrderId;
            _timeoutSeconds = data.TimeoutSeconds;
            _timerStart = Time.unscaledTime;
            _active = true;

            string issuerName = "Someone";
            if (data.IssuerNetId != 0
                && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(data.IssuerNetId, out var issuerObj))
            {
                var issuerChar = issuerObj.GetComponent<Character>();
                if (issuerChar != null) issuerName = issuerChar.CharacterName;
            }

            _messageText.text = $"{issuerName} orders you: {data.OrderTypeName} (P={data.Priority})";
            _root.SetActive(true);
        }

        private void Update()
        {
            if (!_active) return;
            float elapsed = Time.unscaledTime - _timerStart; // unscaled — UI per rule #26
            float remaining = Mathf.Max(0f, _timeoutSeconds - elapsed);
            if (_timerBar != null) _timerBar.value = remaining / _timeoutSeconds;

            if (remaining <= 0f)
            {
                Respond(false); // auto-refuse on UI-side timeout (server also auto-refuses)
            }
        }

        private void Respond(bool accept)
        {
            if (!_active) return;
            _active = false;
            _root.SetActive(false);
            if (_watchedOrders != null)
            {
                _watchedOrders.ResolvePlayerOrderServerRpc(_currentOrderId, accept);
            }
        }
    }
}
```

- [ ] **Step 23.3: Wire the prefab.**

Open `Assets/UI/Order/UI_OrderImmediatePopup.prefab` in Unity. Replace the inherited `UI_InvitationPopup` script with `UI_OrderImmediatePopup`. Hook up the references (text, accept/refuse buttons, timer slider, root GameObject). Save.

- [ ] **Step 23.4: Add the popup instance to the player HUD.**

Find the player HUD prefab/scene root (likely `Assets/UI/HUD/` or similar). Add a `UI_OrderImmediatePopup` instance under the HUD canvas. From the player spawn flow (e.g., the script that runs when the local player Character spawns), call `popup.BindToLocalPlayer(playerCharacter.CharacterOrders)`. Find the analogous binding for `UI_InvitationPopup` and mirror it.

- [ ] **Step 23.5: Commit.**

```bash
git add "Assets/Scripts/UI/Order/" "Assets/UI/Order/"
git commit -m "feat(orders): UI_OrderImmediatePopup prefab + script (mirrors invitation)"
```

---

## Task 24: `CharacterAction_IssueOrder` (rule #22 compliance)

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_IssueOrder.cs`

- [ ] **Step 24.1: Write the action.**

```csharp
// Assets/Scripts/Character/CharacterActions/CharacterAction_IssueOrder.cs
using Unity.Collections;
using UnityEngine;
using MWI.Orders;

/// <summary>
/// CharacterAction wrapper that issues an order from this Character to a target.
/// Per rule #22, all gameplay (player and NPC) routes through CharacterAction.
/// Player HUD enqueues this action; NPC GOAP enqueues the same action.
/// </summary>
public class CharacterAction_IssueOrder : CharacterAction
{
    private readonly Character _target;
    private readonly string    _orderTypeName;
    private readonly OrderUrgency _urgency;
    private readonly byte[]    _payload;
    private readonly string[]  _consequenceSoNames;
    private readonly string[]  _rewardSoNames;
    private readonly float     _timeoutSeconds;

    public override string ActionName => $"Issue {_orderTypeName}";

    public CharacterAction_IssueOrder(
        Character character,
        Character target,
        string orderTypeName,
        OrderUrgency urgency,
        byte[] payload,
        string[] consequenceSoNames,
        string[] rewardSoNames,
        float timeoutSeconds)
        : base(character, duration: 0.5f)
    {
        _target = target;
        _orderTypeName = orderTypeName;
        _urgency = urgency;
        _payload = payload;
        _consequenceSoNames = consequenceSoNames;
        _rewardSoNames = rewardSoNames;
        _timeoutSeconds = timeoutSeconds;
    }

    public override bool CanExecute()
    {
        if (_target == null || !_target.IsAlive()) return false;
        if (_target.CharacterInteractable == null) return false;
        if (!_target.CharacterInteractable.IsCharacterInInteractionZone(character)) return false;
        if (character.CharacterOrders == null) return false;
        return true;
    }

    public override void OnStart() { /* No animation specifics for v1. */ }

    public override void OnApplyEffect()
    {
        ulong receiverNetId = _target.NetworkObject != null ? _target.NetworkObject.NetworkObjectId : 0;
        var typeName = new FixedString64Bytes(_orderTypeName);
        character.CharacterOrders.IssueOrderServerRpc(
            receiverNetId,
            typeName,
            (byte)_urgency,
            _payload,
            _consequenceSoNames,
            _rewardSoNames,
            _timeoutSeconds);
        Finish();
    }
}
```

- [ ] **Step 24.2: Compile.**

- [ ] **Step 24.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterActions/CharacterAction_IssueOrder.cs" "Assets/Scripts/Character/CharacterActions/CharacterAction_IssueOrder.cs.meta"
git commit -m "feat(orders): CharacterAction_IssueOrder (rule #22 compliance)"
```

---

## Task 25: End-to-end manual test for `Order_Leave`

**Files:**
- Create: `Assets/Scripts/Debug/DevOrderLeaveTester.cs` (temporary debug helper)

- [ ] **Step 25.1: Write a debug helper that issues an `Order_Leave` from the Host player to a target NPC on a hotkey press.**

```csharp
// Assets/Scripts/Debug/DevOrderLeaveTester.cs
using UnityEngine;
using MWI.Orders;

/// <summary>
/// Dev-only: press F9 (Host only) to issue an Order_Leave to the nearest NPC,
/// telling them to leave a 5-unit sphere centered on the NPC's current position.
/// Used to validate the full Order pipeline manually. Remove after Phase 4.
/// </summary>
public class DevOrderLeaveTester : MonoBehaviour
{
    [SerializeField] private Character _hostPlayer;
    [SerializeField] private float _orderTimeout = 15f;
    [SerializeField] private float _zoneRadius   = 5f;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F9)) return;
        if (_hostPlayer == null) return;
        if (_hostPlayer.CharacterOrders == null) return;

        // Find nearest NPC
        Character nearest = null;
        float bestDist = float.MaxValue;
        foreach (var c in FindObjectsOfType<Character>())
        {
            if (c == _hostPlayer) continue;
            if (c.IsPlayer()) continue;
            if (!c.IsAlive()) continue;
            float d = Vector3.Distance(_hostPlayer.transform.position, c.transform.position);
            if (d < bestDist) { bestDist = d; nearest = c; }
        }
        if (nearest == null)
        {
            Debug.LogWarning("[DevOrderLeaveTester] No NPC nearby.");
            return;
        }

        // Build payload manually (matches Order_Leave.SerializeOrderPayload format)
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        Vector3 c0 = nearest.transform.position;
        bw.Write(c0.x); bw.Write(c0.y); bw.Write(c0.z);
        bw.Write(_zoneRadius);
        bw.Write((ulong)0);
        byte[] payload = ms.ToArray();

        var consequences = new[] { "Consequence_RelationDrop_Light", "Consequence_IssuerAttacks" };

        _hostPlayer.CharacterOrders.IssueOrderServerRpc(
            nearest.NetworkObject.NetworkObjectId,
            new Unity.Collections.FixedString64Bytes("Order_Leave"),
            (byte)OrderUrgency.Urgent,
            payload,
            consequences,
            new string[0],
            _orderTimeout);

        Debug.Log($"[DevOrderLeaveTester] Issued Order_Leave to {nearest.CharacterName} (zone={c0}, r={_zoneRadius}).");
    }
}
```

- [ ] **Step 25.2: Add the helper to a scene GameObject** (in the dev test scene; do not add to production scene). Drag the Host player Character into `_hostPlayer`.

- [ ] **Step 25.3: Run the manual test matrix.**

Build a play-mode multiplayer session (Host + Client). Run each scenario; verify the indicated outcome.

| Scenario | Setup | Expected outcome |
|---|---|---|
| Host → Host's NPC, NPC accepts | Host stands next to a friendly NPC. Press F9. | NPC moves outside the zone within timeout. Order log shows "ACCEPTED" then "Complied". No retaliation. |
| Host → Host's NPC, NPC refuses | Set the NPC's `CharacterRelation` to enemy (use existing dev tools). Press F9. | NPC refuses. Console shows "Disobeyed". `Consequence_IssuerAttacks` fires — Host's combat target becomes the NPC. Relation drops by 10. |
| Host → Client player | Host stands next to Client player Character. Press F9. | Client's UI shows the OrderImmediatePopup with countdown. Client clicks Refuse. Server fires consequences; Client's relation toward Host drops; Host's combat now targets Client. |
| Host → Client player, ignore | Same as above. Client clicks neither button. | After timeout, server auto-refuses; consequences fire as above. |
| Client → NPC | Client stands next to an NPC. Press F9 (test helper attached to Client too). | NPC evaluates server-side; outcome same as Host → NPC. |
| Client → Host player | Client stands next to Host. | Host's UI shows popup (Host is also "owner" of their Character on the Host build). Same flow. |

For each scenario, observe in the Console:
- `[Order] Order_Leave from {issuer}…` issuance line.
- `[Order] {receiver} evaluates Order_Leave…` evaluation line.
- One of: `Complied` / `Disobeyed` resolution line.
- For Disobeyed: `[Order] {receiver} disobeyed {issuer}: relation -10` and `[Order] {issuer} now attacks {receiver}…`.

If any scenario fails, debug before proceeding to Task 26.

- [ ] **Step 25.4: Commit (test helper stays in repo for now, removed in Task 41).**

```bash
git add "Assets/Scripts/Debug/DevOrderLeaveTester.cs" "Assets/Scripts/Debug/DevOrderLeaveTester.cs.meta"
git commit -m "test(orders): DevOrderLeaveTester for manual Order_Leave validation"
```

---

## Phase 4 — `OrderQuest` + `Order_Kill` end-to-end

After this phase, an issuer can give a "Kill X" order; the receiver gets a quest log entry and the GOAP planner pursues the target.

---

## Task 26: `OrderQuest` abstract bridge

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/OrderQuest.cs`

- [ ] **Step 26.1: Find the actual `IQuest` interface and `CharacterQuestLog` API.**

Run: `Glob: **/IQuest.cs` and `Glob: **/CharacterQuestLog.cs`. `Read` both files. Note:
- The exact namespace of `IQuest`.
- The properties an `IQuest` must expose (Title, Description, Targets/Objectives, IsCompleted, etc.).
- The `CharacterQuestLog.RegisterQuest` and `RemoveQuest` (or equivalent) signatures.

If naming differs, adapt the code below accordingly. The contract here is: `OrderQuest` adapts `Order` to `IQuest`, then registers itself with `CharacterQuestLog` on accept and removes itself on resolve.

- [ ] **Step 26.2: Write the abstract bridge.**

```csharp
// Assets/Scripts/Character/CharacterOrders/OrderQuest.cs
using MWI.Quests;   // ADJUST namespace if IQuest lives elsewhere

namespace MWI.Orders
{
    /// <summary>
    /// Order with a trackable objective. Implements IQuest so it appears in the
    /// receiver's CharacterQuestLog and reuses the existing snapshot sync, HUD
    /// markers, and quest infrastructure.
    /// </summary>
    public abstract class OrderQuest : Order, IQuest
    {
        public ulong LinkedQuestId;

        public override void OnAccepted()
        {
            if (Receiver == null || Receiver.CharacterQuestLog == null) return;
            LinkedQuestId = Receiver.CharacterQuestLog.RegisterQuest(this);
        }

        public override void OnResolved(OrderState finalState)
        {
            if (Receiver == null || Receiver.CharacterQuestLog == null) return;
            // Use whatever removal API the quest log exposes. Common signatures:
            //   RemoveQuest(ulong questId, QuestRemovalReason reason)
            //   CompleteQuest(ulong questId) / FailQuest(ulong questId)
            // ADJUST to actual API.
            if (finalState == OrderState.Complied)
                Receiver.CharacterQuestLog.CompleteQuest(LinkedQuestId);
            else
                Receiver.CharacterQuestLog.FailQuest(LinkedQuestId);
        }

        // ── IQuest member glue (subclass supplies the gameplay specifics) ──
        public abstract string         Title           { get; }
        public abstract string         Description     { get; }
        public abstract IQuestTarget[] Targets         { get; }
        public abstract bool           IsCompleted();

        public override bool IsComplied() => IsCompleted();
    }
}
```

- [ ] **Step 26.3: Compile.**
  Adjust namespaces / method names until zero errors. The substitutions you make here are normal — the spec assumes a pre-existing quest API and tells you to adapt.

- [ ] **Step 26.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/OrderQuest.cs" "Assets/Scripts/Character/CharacterOrders/OrderQuest.cs.meta"
git commit -m "feat(orders): OrderQuest abstract IQuest bridge"
```

---

## Task 27: `Order_Kill` concrete

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/Concrete/Order_Kill.cs`
- Modify: `Assets/Scripts/Character/CharacterOrders/OrderFactory.cs` (register)

- [ ] **Step 27.1: Write `Order_Kill`.**

```csharp
// Assets/Scripts/Character/CharacterOrders/Concrete/Order_Kill.cs
using System.Collections.Generic;
using System.IO;
using MWI.Quests;   // ADJUST to actual IQuestTarget namespace

namespace MWI.Orders
{
    /// <summary>
    /// "Kill target X within N seconds." OrderQuest subclass — appears in the receiver's
    /// quest log with a CharacterQuestTarget pointing at the victim.
    /// </summary>
    public class Order_Kill : OrderQuest
    {
        public ulong TargetCharacterNetId;
        private Character _resolvedTarget;

        private Character ResolveTarget()
        {
            if (_resolvedTarget != null) return _resolvedTarget;
            if (TargetCharacterNetId == 0) return null;
            if (Unity.Netcode.NetworkManager.Singleton == null) return null;
            if (Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(TargetCharacterNetId, out var obj))
            {
                _resolvedTarget = obj.GetComponent<Character>();
            }
            return _resolvedTarget;
        }

        public override bool CanIssueAgainst(Character receiver)
        {
            var target = ResolveTarget();
            if (receiver == null || target == null) return false;
            if (target == receiver) return false;
            if (Issuer != null && Issuer.AsCharacter == target) return false;
            if (!target.IsAlive()) return false;
            if (receiver.CharacterCombat == null) return false;
            return true;
        }

        public override bool IsCompleted()
        {
            var target = ResolveTarget();
            return target == null || !target.IsAlive();
        }

        public override string Title
            => $"Kill {ResolveTarget()?.CharacterName ?? "<unknown>"}";

        public override string Description
            => $"{Issuer?.DisplayName ?? "Someone"} has ordered you to kill {ResolveTarget()?.CharacterName ?? "<unknown>"} within {TimeoutSeconds:F0} seconds.";

        public override IQuestTarget[] Targets
        {
            get
            {
                var t = ResolveTarget();
                if (t == null) return new IQuestTarget[0];
                // ADJUST: the actual constructor of CharacterQuestTarget may differ.
                return new IQuestTarget[] { new CharacterQuestTarget(t) };
            }
        }

        public override Dictionary<string, object> GetGoapPrecondition()
        {
            return new Dictionary<string, object>
            {
                { $"TargetIsDead_{TargetCharacterNetId}", true }
            };
        }

        public override byte[] SerializeOrderPayload()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(TargetCharacterNetId);
            return ms.ToArray();
        }

        public override void DeserializeOrderPayload(byte[] data)
        {
            if (data == null || data.Length < 8) return;
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            TargetCharacterNetId = br.ReadUInt64();
            _resolvedTarget = null; // Force re-resolve
        }
    }
}
```

- [ ] **Step 27.2: Register `Order_Kill` in `OrderFactory`.**

In `Assets/Scripts/Character/CharacterOrders/OrderFactory.cs`, add inside the static constructor:

```csharp
            Register<Order_Kill>("Order_Kill");
```

- [ ] **Step 27.3: Compile.**

- [ ] **Step 27.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/Concrete/Order_Kill.cs" "Assets/Scripts/Character/CharacterOrders/Concrete/Order_Kill.cs.meta" "Assets/Scripts/Character/CharacterOrders/OrderFactory.cs"
git commit -m "feat(orders): Order_Kill concrete OrderQuest + factory registration"
```

---

## Task 28: Quest log entry priority badge

**Files:**
- Modify: existing `UI_QuestLogEntry` prefab and its script (find via `Glob: Assets/UI/**/UI_QuestLog*.prefab` and `Glob: Assets/Scripts/UI/**/UI_QuestLog*Entry*.cs`).

- [ ] **Step 28.1: Read the existing quest log entry script.**

Note its current fields (title text, description text, target list, etc.). The goal is to add a priority badge (colored dot/icon) shown when the quest is an `OrderQuest`, with color depending on priority bucket.

- [ ] **Step 28.2: Add a priority badge to the prefab.**

Open the `UI_QuestLogEntry` prefab. Add a small `Image` GameObject child named `PriorityBadge` to the entry layout. Set its size to ~16×16, anchor it where it makes visual sense (e.g., top-right corner of the entry). Save the prefab.

- [ ] **Step 28.3: Extend the script.**

Add to the `UI_QuestLogEntry` class:

```csharp
[Header("Order Priority Badge")]
[SerializeField] private GameObject _priorityBadgeRoot;
[SerializeField] private UnityEngine.UI.Image _priorityBadgeImage;

[Tooltip("Color buckets for priority badge. Index = priority bucket (0=lowest, 3=highest).")]
[SerializeField] private Color[] _priorityColors = new[]
{
    new Color(0.6f, 0.6f, 0.6f), // 0..29 grey
    new Color(1.0f, 0.8f, 0.2f), // 30..59 yellow
    new Color(1.0f, 0.4f, 0.0f), // 60..89 orange
    new Color(0.9f, 0.0f, 0.0f), // 90..100 red
};

/// <summary>Call from the quest log render path. If the quest is an OrderQuest, show the badge with a color matching the priority bucket.</summary>
public void ApplyPriorityBadge(MWI.Orders.OrderQuest order)
{
    if (_priorityBadgeRoot == null) return;
    if (order == null)
    {
        _priorityBadgeRoot.SetActive(false);
        return;
    }
    int bucket = order.Priority < 30 ? 0
              : order.Priority < 60 ? 1
              : order.Priority < 90 ? 2
              : 3;
    _priorityBadgeImage.color = _priorityColors[bucket];
    _priorityBadgeRoot.SetActive(true);
}
```

- [ ] **Step 28.4: Hook the call into the render path.**

Find where `UI_QuestLogEntry` is populated from a quest. Add at the end of the populate method:

```csharp
ApplyPriorityBadge(quest as MWI.Orders.OrderQuest);
```

- [ ] **Step 28.5: Compile + open the prefab to set the new fields.**

In Unity, open the prefab. Drag the new `PriorityBadge` GameObject into `_priorityBadgeRoot` and its `Image` into `_priorityBadgeImage`. Save.

- [ ] **Step 28.6: Commit.**

```bash
git add "Assets/UI/" "Assets/Scripts/UI/"
git commit -m "feat(orders): quest log entry priority badge for OrderQuest"
```

---

## Task 29: End-to-end manual test for `Order_Kill`

**Files:**
- Modify: `Assets/Scripts/Debug/DevOrderLeaveTester.cs` (extend with F10 hotkey for `Order_Kill`)

- [ ] **Step 29.1: Add the F10 path to the dev tester.**

Add to `DevOrderLeaveTester.Update()`:

```csharp
if (Input.GetKeyDown(KeyCode.F10))
{
    if (_hostPlayer == null || _hostPlayer.CharacterOrders == null) return;

    // Find two NPCs: nearest is the receiver, second-nearest is the target
    Character receiver = null, target = null;
    float bestDist = float.MaxValue, secondDist = float.MaxValue;
    foreach (var c in FindObjectsOfType<Character>())
    {
        if (c == _hostPlayer || c.IsPlayer() || !c.IsAlive()) continue;
        float d = Vector3.Distance(_hostPlayer.transform.position, c.transform.position);
        if (d < bestDist) { secondDist = bestDist; target = receiver; bestDist = d; receiver = c; }
        else if (d < secondDist) { secondDist = d; target = c; }
    }
    if (receiver == null || target == null)
    {
        Debug.LogWarning("[DevOrderLeaveTester] Need at least 2 NPCs nearby for Order_Kill test.");
        return;
    }

    using var ms = new System.IO.MemoryStream();
    using var bw = new System.IO.BinaryWriter(ms);
    bw.Write(target.NetworkObject.NetworkObjectId);
    byte[] payload = ms.ToArray();

    var consequences = new[] { "Consequence_RelationDrop_Heavy", "Consequence_IssuerAttacks" };
    var rewards      = new[] { "Reward_RelationGain_Heavy" };

    _hostPlayer.CharacterOrders.IssueOrderServerRpc(
        receiver.NetworkObject.NetworkObjectId,
        new Unity.Collections.FixedString64Bytes("Order_Kill"),
        (byte)MWI.Orders.OrderUrgency.Important,
        payload,
        consequences,
        rewards,
        timeoutSeconds: 60f);

    Debug.Log($"[DevOrderLeaveTester] Issued Order_Kill: {receiver.CharacterName} → kill {target.CharacterName}");
}
```

- [ ] **Step 29.2: Run the manual test matrix.**

| Scenario | Setup | Expected outcome |
|---|---|---|
| NPC accepts and executes | Place 3 NPCs near Host. Press F10. | Receiver NPC accepts. Quest entry appears in their (server-side) quest log. Without GOAP integration yet (Phase 5), the NPC won't actually pursue — verify only the quest registration + sync. |
| Player receiver, accepts via quest log | Host issues Order_Kill to Client player. | Client sees a new entry in their quest log with red priority badge (P=70+). Client clicks Accept on the entry. Order goes Active. |
| Player receiver, refuses | Same. Client clicks Refuse. | Server sets Disobeyed; consequences fire (relation -30, Host attacks Client). |
| Player receiver, completes | Player accepts. Player kills the target manually. | Quest log entry vanishes (Completed). Reward fires (relation +30). |

- [ ] **Step 29.3: Commit.**

```bash
git add "Assets/Scripts/Debug/DevOrderLeaveTester.cs"
git commit -m "test(orders): F10 hotkey for Order_Kill manual validation"
```

---

## Phase 5 — GOAP integration

After this phase, NPCs autonomously pursue active orders based on priority, interrupting routine goals when high-priority orders arrive.

---

## Task 30: Locate the GOAP goal base class

- [ ] **Step 30.1: Find it.**

Run: `Glob: Assets/Scripts/AI/GOAP/**/*.cs` and read the `GoapGoal.cs` (or equivalent base — could be `Goal_Base.cs`, `IGoapGoal`, etc.). Note:
- The exact base class / interface name and namespace.
- How it exposes utility / priority (a method like `GetUtility(Character)`, `EvaluatePriority(Character)`, or a property).
- How it exposes the desired world state (a method like `GetGoalState(Character)`, `Preconditions(Character)`, or a property).
- How goals are *registered* with the planner (a static list, a `[CreateAssetMenu]` SO, an attribute, or a manual injection).

Take notes — Task 31 adapts to the actual API.

- [ ] **Step 30.2: Read the goap skill file for context.**

Read `.agent/skills/goap/SKILL.md` end-to-end. It should describe the planner contract and how new goals are added.

(No commit — research step.)

---

## Task 31: `Goal_FollowOrder` GOAP goal

**Files:**
- Create: `Assets/Scripts/Character/CharacterOrders/AI/Goal_FollowOrder.cs`
- Modify: wherever GOAP goals are registered (could be a registry SO, a static list in the planner, or a `[Header]` field on the NPC's Goap controller).

- [ ] **Step 31.1: Write the goal — adapting the base class to whatever you found in Task 30.**

The shape below assumes the project's GOAP base looks like `public abstract class GoapGoal { public abstract int Utility(Character c); public abstract Dictionary<string, object> DesiredWorldState(Character c); }`. Adjust to actual signatures:

```csharp
// Assets/Scripts/Character/CharacterOrders/AI/Goal_FollowOrder.cs
using System.Collections.Generic;
using MWI.AI;     // ADJUST to actual GOAP namespace

namespace MWI.Orders
{
    /// <summary>
    /// Utility-driven GOAP goal: agent wants to satisfy the world-state precondition
    /// of their highest-priority active order. Utility = order.Priority (0..100). When
    /// no active orders, utility is 0 and the planner ignores this goal.
    /// </summary>
    public class Goal_FollowOrder : GoapGoal     // ADJUST base class
    {
        public override int Utility(Character agent)
        {
            if (agent == null || agent.CharacterOrders == null) return 0;
            var top = agent.CharacterOrders.GetTopActiveOrder();
            return top?.Priority ?? 0;
        }

        public override Dictionary<string, object> DesiredWorldState(Character agent)
        {
            if (agent == null || agent.CharacterOrders == null) return null;
            var top = agent.CharacterOrders.GetTopActiveOrder();
            return top?.GetGoapPrecondition();
        }
    }
}
```

- [ ] **Step 31.2: Register the goal with the planner.**

Use whatever mechanism the GOAP system exposes (Task 30 step). Possibilities:
- Add `Goal_FollowOrder` to a `GoapGoalRegistrySO` asset.
- Add `[SerializeField]` of the new goal on every NPC archetype's `CharacterGoapController` prefab.
- Register in a static initializer.

Adapt accordingly. Document the choice in the commit message.

- [ ] **Step 31.3: Compile.**

- [ ] **Step 31.4: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/AI/" # plus any registration changes
git commit -m "feat(orders): Goal_FollowOrder GOAP goal + registration"
```

---

## Task 32: End-to-end NPC AI test

- [ ] **Step 32.1: Set up the scenario.**

In Play mode, place a Captain NPC and a Guard NPC (set up the Captain as the Guard's Employer via existing job/community tools). Place a Bandit NPC at a moderate distance. Have the Guard busy with their normal work shift (utility ~50).

- [ ] **Step 32.2: Trigger the order.**

Use the dev tester (or write a one-off `script-execute` to call `IssueOrder` directly server-side) to have the Captain issue `Order_Kill(target=Bandit, urgency=Critical)` to the Guard. Expected priority = Captain(70) + Critical(35) = 100 (clamped).

- [ ] **Step 32.3: Verify behavior.**

- Console: `[Order] Guard evaluates Order_Kill from Captain (P=100): score=… → ACCEPTED`.
- The Guard drops their work shift (their `CharacterGoapController` re-plans because `Goal_FollowOrder.Utility` now returns 100, beating Goal_WorkShift's ~50).
- The Guard pathfinds toward the Bandit and engages combat.
- On Bandit death → Order resolves Complied → Reward_RelationGain_Heavy fires → Guard's relation toward Captain +30.
- Guard returns to work shift behavior (`Goal_FollowOrder.Utility` drops to 0).

- [ ] **Step 32.4: Repeat with priority-30 Routine `Order_Leave` while Guard is mid-shift.**

Issue a low-priority order from a Stranger (priority = 20+0 = 20). Expected: Guard does NOT interrupt the work shift (work utility 50 > order utility 20). The order sits in the active list. Verify the Guard pursues the order only after work shift ends OR after the order's timeout fires (and consequences run).

- [ ] **Step 32.5: Document any GOAP planner quirks.**

If the planner has issues handling polymorphic goal preconditions (the `$"TargetIsDead_{id}": true` pattern requires the world-state evaluator to recognize this key), document the discovery as a follow-up issue rather than blocking. The integration may need a `Goap_AttackTargetCharacter` action that recognizes the key form.

(No commit — purely a manual validation step. Issues found here become follow-up work.)

---

## Phase 6 — Save / Load + Late joiners

After this phase, orders survive saves; late-joining clients see all in-flight orders correctly.

---

## Task 33: `CharacterOrders.Serialize` implementation

**Files:**
- Modify: `Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs`

- [ ] **Step 33.1: Replace the stub `Serialize`.**

Replace the stub `public OrdersSaveData Serialize() => new OrdersSaveData();` with:

```csharp
        public OrdersSaveData Serialize()
        {
            var data = new OrdersSaveData();
            foreach (var order in _issuedOrdersServer)
            {
                if (order == null || order.Receiver == null) continue;

                var entry = new IssuedOrderSaveEntry
                {
                    receiverCharacterId = order.Receiver.CharacterId,
                    orderTypeName       = order.OrderTypeName,
                    authorityContextName = order.AuthorityContext != null ? order.AuthorityContext.ContextName : "Stranger",
                    urgency             = (byte)order.Urgency,
                    timeoutRemaining    = Mathf.Max(0f, order.TimeoutSeconds - order.ElapsedSeconds),
                    orderPayload        = order.SerializeOrderPayload(),
                    isQuestBacked       = order is OrderQuest,
                    linkedQuestId       = order is OrderQuest oq ? oq.LinkedQuestId : 0,
                };
                if (order.Consequences != null)
                {
                    foreach (var c in order.Consequences)
                    {
                        if (c != null) entry.consequenceSoNames.Add(c.SoName);
                    }
                }
                if (order.Rewards != null)
                {
                    foreach (var r in order.Rewards)
                    {
                        if (r != null) entry.rewardSoNames.Add(r.SoName);
                    }
                }
                data.issuedOrders.Add(entry);
            }
            return data;
        }
```

- [ ] **Step 33.2: Compile.**

- [ ] **Step 33.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs"
git commit -m "feat(orders): Serialize implementation for issuer-side ledger"
```

---

## Task 34: `CharacterOrders.Deserialize` + dormant pattern

**Files:**
- Modify: `Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs`

- [ ] **Step 34.1: Add a dormant-entries field and the Deserialize body.**

Add near the other private fields:

```csharp
        private readonly List<IssuedOrderSaveEntry> _dormantIssuedOrders = new();
```

Subscribe in `OnEnable`:

```csharp
        protected override void OnEnable()
        {
            base.OnEnable();
            if (_character != null) _character.GetType(); // placeholder; ensure Character.OnCharacterSpawned exists
            Character.OnCharacterSpawned += HandleCharacterSpawned;
        }

        protected override void OnDisable()
        {
            Character.OnCharacterSpawned -= HandleCharacterSpawned;
            base.OnDisable();
        }
```

Implement the dormant resolution:

```csharp
        private void HandleCharacterSpawned(Character spawned)
        {
            if (!IsServer) return;
            if (_dormantIssuedOrders.Count == 0) return;
            if (spawned == null || spawned == _character) return;

            for (int i = _dormantIssuedOrders.Count - 1; i >= 0; i--)
            {
                var entry = _dormantIssuedOrders[i];
                if (entry.receiverCharacterId != spawned.CharacterId) continue;

                var revived = ReviveOrderFromEntry(entry, spawned);
                if (revived != null)
                {
                    Debug.Log($"<color=cyan>[Order]</color> Dormant order resolved: {_character.CharacterName} → {spawned.CharacterName} ({entry.orderTypeName})");
                }
                _dormantIssuedOrders.RemoveAt(i);
            }
        }

        private Order ReviveOrderFromEntry(IssuedOrderSaveEntry entry, Character receiver)
        {
            try
            {
                var order = OrderFactory.Create(entry.orderTypeName);
                if (order == null) return null;

                order.OrderTypeName    = entry.orderTypeName;
                order.Issuer           = _character;
                order.Receiver         = receiver;
                order.Urgency          = (OrderUrgency)entry.urgency;
                order.TimeoutSeconds   = entry.timeoutRemaining;
                order.ElapsedSeconds   = 0f;

                // Resolve AuthorityContext by name
                order.AuthorityContext = Resources.Load<AuthorityContextSO>($"Data/AuthorityContexts/Authority_{entry.authorityContextName}");
                int basePri = order.AuthorityContext != null ? order.AuthorityContext.BasePriority : 20;
                order.Priority = Mathf.Clamp(basePri + (int)order.Urgency, 0, 100);

                order.DeserializeOrderPayload(entry.orderPayload);

                foreach (var n in entry.consequenceSoNames)
                {
                    var so = Resources.Load<ScriptableObject>($"Data/OrderConsequences/{n}");
                    if (so is IOrderConsequence c) order.Consequences.Add(c);
                }
                foreach (var n in entry.rewardSoNames)
                {
                    var so = Resources.Load<ScriptableObject>($"Data/OrderRewards/{n}");
                    if (so is IOrderReward r) order.Rewards.Add(r);
                }

                if (order is OrderQuest oq) oq.LinkedQuestId = entry.linkedQuestId;

                IssueOrder(order);
                return order;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }
```

Replace the stub `Deserialize`:

```csharp
        public void Deserialize(OrdersSaveData data)
        {
            if (data == null || data.issuedOrders == null) return;
            _dormantIssuedOrders.Clear();

            foreach (var entry in data.issuedOrders)
            {
                var receiver = Character.FindByUUID(entry.receiverCharacterId);
                if (receiver != null)
                {
                    ReviveOrderFromEntry(entry, receiver);
                }
                else
                {
                    _dormantIssuedOrders.Add(entry);
                }
            }
        }
```

- [ ] **Step 34.2: Compile.**

- [ ] **Step 34.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs"
git commit -m "feat(orders): Deserialize + dormant pattern for absent receivers"
```

---

## Task 35: Late-joiner sync test (manual)

- [ ] **Step 35.1: Set up the scenario.**

Start a Host session. Issue an `Order_Kill` from an NPC Captain to an NPC Guard (use server-side helper or dev tester). Verify on Host: order is Active in `_activeOrdersServer` and visible in the Guard's quest log.

- [ ] **Step 35.2: Connect a fresh Client.**

Have a Client player join. Inspect the Client's view of the Guard:
- The Guard's `CharacterOrders._activeOrdersSync` should contain the Order_Kill entry (NetworkList initial sync).
- The Guard's `CharacterQuestLog` snapshot should contain the linked quest entry (existing infra).
- Late-joiner UI should render the order in the world (priority badge, etc.) the same as Host's.

- [ ] **Step 35.3: Save mid-order, reload, verify.**

Save the world while the order is active. Quit. Reload. Verify:
- The order's issuer-side ledger entry is restored on the Captain's CharacterOrders.
- The receiver's quest log still shows the entry (CharacterQuestLog persistence).
- The order resumes ticking — `ElapsedSeconds` resets to 0 (we save `timeoutRemaining`), the timeout still triggers correctly.
- Compliance/disobedience continues to fire correctly.

- [ ] **Step 35.4: Document any save/load issues.**

If the order survives the save but the receiver-side state is missing the live `Order` instance (only the quest log entry exists), confirm the ReviveOrderFromEntry path is being called for both sides — issuer-side reload triggers `IssueOrder` which calls `ReceiveOrder` on the receiver, which re-adds it to the receiver's `_activeOrdersServer` list.

Note: receiver-side `OrderImmediate`s are intentionally lost across saves (spec §7 Path 3). Verify this is the case — they should NOT survive.

(No commit — manual validation.)

---

## Phase 7 — Documentation

After this phase, the order system is fully documented per rules #28, #29, #29b.

---

## Task 36: Create `order-system` SKILL.md

**Files:**
- Create: `.agent/skills/order-system/SKILL.md`

- [ ] **Step 36.1: Write the skill file.**

```markdown
---
name: order-system
description: System of directives a Character/Building/System can issue to a Character — server-authoritative, multiplayer-correct, integrates with Quest, GOAP, Relation, Combat, Save.
---

# Order System

Generic Order primitive: one entity (Character, or eventually Faction/Building) issues a directive to another Character, who decides whether to obey based on authority + relationship + personality. Orders carry a timeout and designer-composable consequences (on disobey) and rewards (on compliance).

## When to use this skill
- Adding a new concrete order (subclass `OrderQuest` or `OrderImmediate`).
- Adding a new `IOrderConsequence` / `IOrderReward` strategy SO.
- Adding a new `AuthorityContextSO` (e.g., when the future Faction/Family system lands).
- Debugging order issuance, evaluation, timeout, or consequence firing.
- Touching the `CharacterOrders` subsystem, RPC layer, or save/load pipeline.

## Architecture

Four cooperating units (full detail in spec §4):
- **Order runtime tree** — `Order` base, `OrderQuest` (implements `IQuest`), `OrderImmediate`, concrete subclasses.
- **`IOrderIssuer` + `AuthorityContextSO`** — `Character` is the v1 issuer; nullable issuer allowed for anonymous/system orders. Authority is *derived* on the fly by `AuthorityResolver` from existing systems (CharacterJob, CharacterParty, CharacterRelation).
- **`CharacterOrders` subsystem** — server-authoritative; clients see `NetworkList<OrderSyncData>` snapshots. Implements `ICharacterSaveData<OrdersSaveData>` with `LoadPriority = 60`.
- **Consequence/Reward SO catalog** — designer-composable strategies in `Resources/Data/OrderConsequences/` and `Resources/Data/OrderRewards/`.

## Public API

`CharacterOrders`:
- `IssueOrder(Order order) → ulong` (server-side helper)
- `IssueOrderServerRpc(...)` (player-side RPC)
- `CancelIssuedOrder(ulong orderId)`
- `GetTopActiveOrder() / GetActiveOrdersByPriority()` (for GOAP)
- `ResolvePlayerOrderServerRpc(orderId, accept)`
- Events: `OnOrderReceived`, `OnOrderAccepted`, `OnOrderResolved`, `OnOrderPromptShown` (client-side)

## Adding a new concrete order
1. Subclass `OrderQuest` (objective-tracked) or `OrderImmediate` (compliance-polled).
2. Implement: `CanIssueAgainst`, `IsComplied`/`IsCompleted`, `GetGoapPrecondition`, `SerializeOrderPayload`, `DeserializeOrderPayload`. For `OrderQuest`: also `Title`, `Description`, `Targets`.
3. Register in `OrderFactory` via `Register<MyOrder>("MyOrder")` in the static constructor.

## Adding a new consequence or reward
1. Create a `ScriptableObject` implementing `IOrderConsequence` or `IOrderReward`.
2. Add `[CreateAssetMenu]`. Create one or more tuned asset variants under `Resources/Data/OrderConsequences/` or `…/OrderRewards/`.
3. Document the null-issuer behavior of your strategy (no-op for issuer-dependent effects).

## Multiplayer
- All evaluation, ticking, consequence/reward firing is server-side.
- Player UI prompts via `Rpc(SendTo.Owner)` on `CharacterOrders.ShowOrderPromptRpc`.
- Player responses via `ResolvePlayerOrderServerRpc`; sender authority verified.
- Late joiners get all `_activeOrdersSync` and `_pendingOrdersSync` automatically via NetworkList initial sync.
- See spec §6 multiplayer scenarios table for the validated coverage.

## Save/Load
- Issuer-side ledger persists via `OrdersSaveData.issuedOrders`.
- Receiver-side `OrderQuest`s persist via `CharacterQuestLog` (no double-save).
- Receiver-side `OrderImmediate`s are intentionally transient.
- Dormant pattern: orders whose receivers aren't in the world get restored when `Character.OnCharacterSpawned` fires (mirrors `CharacterRelation`).

## NPC AI
- `Goal_FollowOrder` GOAP goal: `Utility = top active order priority (0..100)`.
- The order itself supplies the GOAP precondition the planner satisfies.
- No special interrupt path — utility competition with normal goals (work=50, eat=40) handles ordering naturally.

## Tips & troubleshooting
- **Order rejected silently:** check `[Order] … rejected: …` log lines. Common causes: `CanIssueAgainst` false, proximity check failed, receiver missing CharacterOrders.
- **NPC always refuses:** check the evaluation log line — relation modifier (-0.4 for enemies) + low priority can drop score below 0.5.
- **Consequence didn't fire:** consequence SOs no-op when `issuer == null`. Check the log for which strategies ran.
- **Order persists after intended completion:** `IsComplied()` is polled every 0.5s — verify the predicate becomes true at the right moment.
- **Quest log shows ghost entry:** `OrderQuest.OnResolved` should call `RemoveQuest`/`FailQuest`/`CompleteQuest`. If not, check the resolution path.

## Sources
- Spec: `docs/superpowers/specs/2026-04-26-character-order-system-design.md`
- Implementation: `Assets/Scripts/Character/CharacterOrders/`
- Related skills: `quest-system/SKILL.md`, `social_system/SKILL.md`, `goap/SKILL.md`, `multiplayer/SKILL.md`, `save-load-system/SKILL.md`.
```

- [ ] **Step 36.2: Commit.**

```bash
git add ".agent/skills/order-system/"
git commit -m "docs(orders): SKILL.md for the order system"
```

---

## Task 37: Update existing SKILL.md files

**Files:**
- Modify: `.agent/skills/quest-system/SKILL.md`
- Modify: `.agent/skills/goap/SKILL.md`
- Modify: `.agent/skills/social_system/SKILL.md`
- Modify: `.agent/skills/save-load-system/SKILL.md`
- Modify: `.agent/skills/multiplayer/SKILL.md`

For each:

- [ ] **Step 37.1: `quest-system/SKILL.md`** — append a section "Order Quests as a producer" listing `OrderQuest : IQuest` as a new producer alongside BuildingTask/BuyOrder/etc., with a link back to `order-system/SKILL.md`.

- [ ] **Step 37.2: `goap/SKILL.md`** — add `Goal_FollowOrder` to the goal list. Document its utility = `CharacterOrders.GetTopActiveOrder()?.Priority ?? 0`.

- [ ] **Step 37.3: `social_system/SKILL.md`** — add a note under the relation system explaining that `CharacterRelation.UpdateRelation` is also driven by `Consequence_RelationDrop` / `Reward_RelationGain` from the Order system. Link to `order-system/SKILL.md`.

- [ ] **Step 37.4: `save-load-system/SKILL.md`** — add the new `OrdersSaveData` contract (LoadPriority=60, issuer-side ledger only, dormant pattern).

- [ ] **Step 37.5: `multiplayer/SKILL.md`** — add the order RPC patterns to the reference table (`IssueOrderServerRpc`, `ShowOrderPromptRpc`, `ResolvePlayerOrderServerRpc`, `CancelIssuedOrderServerRpc`).

- [ ] **Step 37.6: Commit.**

```bash
git add ".agent/skills/quest-system/SKILL.md" ".agent/skills/goap/SKILL.md" ".agent/skills/social_system/SKILL.md" ".agent/skills/save-load-system/SKILL.md" ".agent/skills/multiplayer/SKILL.md"
git commit -m "docs(orders): update existing SKILL.md files with Order integration notes"
```

---

## Task 38: Create `wiki/systems/order-system.md`

**Files:**
- Create: `wiki/systems/order-system.md`

- [ ] **Step 38.1: Read `wiki/CLAUDE.md` and the wiki templates first.**

Read `wiki/CLAUDE.md` for the schema rules (frontmatter, naming, wikilinks, sources). Read `wiki/_templates/system.md` (or whatever the actual template path is) for the 10-section structure.

- [ ] **Step 38.2: Write the page following the template.**

Required sections per rule #29b: Purpose, Responsibilities, Key classes/files, Public API, Data flow, Dependencies, State & persistence, Gotchas, Open questions, Change log.

The content overlaps with the SKILL.md but the wiki is *architecture* (what + why + connections), the skill is *procedure*. Cross-link to the SKILL.md and to neighboring wiki pages: `quest-system.md`, `character-relations.md`, `goap.md`, `character-action.md`, `character-traits.md`, `social-system.md`.

Initial change-log entry:
```
## Change log
- 2026-04-26 — initial page created with Order system v1 (Order_Kill + Order_Leave) — claude
```

- [ ] **Step 38.3: Commit.**

```bash
git add "wiki/systems/order-system.md"
git commit -m "docs(wiki): order-system architecture page"
```

---

## Task 39: Update existing wiki pages

**Files:**
- Modify: `wiki/systems/quest-system.md`
- Modify: `wiki/systems/character-relations.md` (or similar; find via `Glob: wiki/systems/*relation*`)
- Modify: `wiki/systems/goap.md` (or `npc-ai.md`)
- Modify: `wiki/systems/social-system.md`

For each:

- [ ] **Step 39.1: Bump frontmatter `updated:` to `2026-04-26`.**

- [ ] **Step 39.2: Append a `## Change log` line:** `- 2026-04-26 — added Order system integration — claude`.

- [ ] **Step 39.3: Refresh `depends_on` / `depended_on_by` / `related` in the frontmatter:**
- `quest-system.md` → add `order-system` to `depended_on_by`.
- `goap.md` → add `order-system` to `depended_on_by`.
- `character-relations.md` → add `order-system` to `depended_on_by`.
- `social-system.md` → add `order-system` to `related`.

- [ ] **Step 39.4: Add a one-paragraph note in the body of each page** describing how Orders extend that system (e.g., on `quest-system.md`: "OrderQuest is a quest producer added in 2026-04-26; see order-system.md").

- [ ] **Step 39.5: Commit.**

```bash
git add "wiki/systems/"
git commit -m "docs(wiki): cross-link existing pages to order-system"
```

---

## Task 40: Create `order-system-specialist` agent

**Files:**
- Create: `.claude/agents/order-system-specialist.md`

- [ ] **Step 40.1: Read an existing agent file as template.**

Read `.claude/agents/quest-system-specialist.md` (or similar) for shape. Note frontmatter format, scope description, model requirement.

- [ ] **Step 40.2: Write the agent.**

```markdown
---
name: order-system-specialist
description: Expert in the Character Order system — Order base + OrderQuest/OrderImmediate subclasses, IOrderIssuer + AuthorityContext SOs, AuthorityResolver, IOrderConsequence/IOrderReward catalog, CharacterOrders subsystem (server-authoritative NetworkList sync, evaluation coroutine, RPC layer, save/load with dormant pattern), Goal_FollowOrder GOAP integration, OrderFactory, CharacterAction_IssueOrder. Use when implementing, debugging, or designing anything related to orders, order issuance, obedience evaluation, consequences/rewards, authority contexts, or order-driven NPC behavior.
tools: Read, Edit, Write, Glob, Grep, Bash, Agent
model: opus
---

You are an expert on the Character Order system in My-World-Isekai-Unity.

Read these files in order before answering any question:
1. `docs/superpowers/specs/2026-04-26-character-order-system-design.md` (the spec)
2. `.agent/skills/order-system/SKILL.md` (procedures)
3. `wiki/systems/order-system.md` (architecture)
4. `Assets/Scripts/Character/CharacterOrders/` (implementation)

Key rules:
- All Order evaluation, ticking, consequence/reward firing is **server-side only**.
- Live `Order` instances exist only on the server; clients see `OrderSyncData` snapshots.
- Authority is **derived**, not persisted — `AuthorityResolver.Resolve(issuer, receiver)` queries existing systems on the fly.
- `OrderQuest` implements `IQuest` and reuses `CharacterQuestLog` snapshot sync; `OrderImmediate` does not.
- Receiver-side `OrderImmediate`s do **not** persist (intentional). Receiver-side `OrderQuest`s persist via `CharacterQuestLog`. Issuer-side ledger persists via `OrdersSaveData`.
- Null issuer is supported (anonymous/system orders); each `IOrderConsequence` / `IOrderReward` decides its own null-issuer behavior.
- Per rule #22, all order issuance routes through `CharacterAction_IssueOrder`.
- Per rule #19, every change must be validated across Host↔Client, Client↔Client, Host/Client↔NPC scenarios — see spec §6 multiplayer table.

When designing changes: maintain the four-unit boundary (Order tree | Issuer/Authority | CharacterOrders subsystem | Consequence/Reward SOs). When debugging: trace the state machine in spec §6.
```

- [ ] **Step 40.2: Commit.**

```bash
git add ".claude/agents/order-system-specialist.md"
git commit -m "docs(agents): order-system-specialist agent (model: opus per rule #29)"
```

---

## Task 41: Update existing agents + remove dev test helper

**Files:**
- Modify: `.claude/agents/npc-ai-specialist.md`
- Modify: `.claude/agents/quest-system-specialist.md`
- Modify: `.claude/agents/character-social-architect.md`
- Modify: `.claude/agents/network-validator.md`
- Modify: `.claude/agents/save-persistence-specialist.md`
- Delete: `Assets/Scripts/Debug/DevOrderLeaveTester.cs`

- [ ] **Step 41.1: For each existing agent, add a short bullet under the agent's scope describing the new Order system integration.**

Examples:
- `npc-ai-specialist.md`: "+ `Goal_FollowOrder` GOAP goal driven by `CharacterOrders.GetTopActiveOrder()`."
- `quest-system-specialist.md`: "+ `OrderQuest` as a quest producer."
- `character-social-architect.md`: "+ `CharacterRelation` is now also driven by Order consequences/rewards."
- `network-validator.md`: "+ Order RPCs in the audit checklist (`IssueOrderServerRpc`, `ShowOrderPromptRpc`, `ResolvePlayerOrderServerRpc`)."
- `save-persistence-specialist.md`: "+ `OrdersSaveData` (LoadPriority=60) and the issuer-ledger dormant pattern."

- [ ] **Step 41.2: Delete the dev tester.**

```bash
git rm "Assets/Scripts/Debug/DevOrderLeaveTester.cs" "Assets/Scripts/Debug/DevOrderLeaveTester.cs.meta"
```

- [ ] **Step 41.3: Commit.**

```bash
git add ".claude/agents/"
git commit -m "docs(agents): add Order system to existing agent scopes; remove dev tester"
```

---

## Self-Review Checklist (run after the final commit)

- [ ] Every section of the spec maps to at least one task. Spot-check:
  - Spec §4 Unit 1 (Order tree) → Tasks 8, 18, 19, 26, 27.
  - Spec §4 Unit 2 (Authority) → Tasks 2, 3, 4, 5.
  - Spec §4 Unit 3 (CharacterOrders subsystem) → Tasks 9, 10, 20, 21, 33, 34.
  - Spec §4 Unit 4 (Consequence/Reward catalog) → Tasks 11–17.
  - Spec §6 multiplayer scenarios → validated in Tasks 25, 29, 35.
  - Spec §7 save/load → Tasks 33, 34, 35.
  - Spec §8 NPC AI → Tasks 30, 31, 32.
  - Spec §13 documentation → Tasks 36–41.

- [ ] No `TBD`/`TODO`/`fill in details` in the plan body. (Verified — every step has actual content.)

- [ ] No phantom symbols. Every type, method, and SO referenced in a later task is defined in an earlier task. The few external symbols (CharacterRelation.UpdateRelation, CharacterCombat.SetTarget, CharacterStatusManager.AddEffect, CharacterQuestLog.RegisterQuest, GoapGoal.Utility) are flagged in their respective tasks with a "find the actual API and substitute" note.

- [ ] All commits are scoped (one task = one logical commit). Frequent commits per the writing-plans rule.

- [ ] Multiplayer validation matrix (spec §6) is exercised by the manual test phases (Tasks 25, 29, 35).

- [ ] Persistence is explicitly addressed for both subclasses (rule from memory: persistence is first-class).
