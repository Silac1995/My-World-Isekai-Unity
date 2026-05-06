---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/blob-assets-create.html
fetched: 2026-05-05
section: systems
---

# Create a Blob Asset

To create a [blob asset](blob-assets-concept.html), perform the following steps:

1. Create a [BlobBuilder](../api/Unity.Entities.BlobBuilder.html). This needs to allocate some memory internally.
2. Use [BlobBuilder.ConstructRoot](../api/Unity.Entities.BlobBuilder.ConstructRoot.html) to construct the root of the blob asset.
3. Fill the structure with your data.
4. Use [BlobBuilder.CreateBlobAssetReference](../api/Unity.Entities.BlobBuilder.CreateBlobAssetReference.html) to create a [BlobAssetReference](../api/Unity.Entities.BlobAssetReference-1.html). This copies the blob asset to its final location.
5. Dispose the `BlobBuilder`.

## Basic Example

The following example stores a struct with primitive members as a blob asset:

```csharp
struct MarketData
{
    public float PriceOranges;
    public float PriceApples;
}

BlobAssetReference<MarketData> CreateMarketData()
{
    // Create a new builder that will use temporary memory to construct the blob asset
    var builder = new BlobBuilder(Allocator.Temp);

    // Construct the root object for the blob asset. Notice the use of `ref`.
    ref MarketData marketData = ref builder.ConstructRoot<MarketData>();

    // Now fill the constructed root with the data:
    // Apples compare to Oranges in the universally accepted ratio of 2 : 1 .
    marketData.PriceApples = 2f;
    marketData.PriceOranges = 4f;

    // Now copy the data from the builder into its final place, which will
    // use the persistent allocator
    var result = builder.CreateBlobAssetReference<MarketData>(Allocator.Persistent);

    // Make sure to dispose the builder itself so all internal memory is disposed.
    builder.Dispose();
    return result;
}
```

The `BlobBuilder` constructs the data stored in the blob asset, makes sure that all internal references are stored as offsets, and then copies the finished blob asset into a single allocation referenced by the returned `BlobAssetReference<T>`.

## Arrays in Blob Assets

You must use the [BlobArray](../api/Unity.Entities.BlobArray-1.html) type to create an array within a blob asset. This is because arrays are implemented with relative offsets internally. The following is an example of how to allocate an array of blob data and fill it:

```csharp
struct Hobby
{
    public float Excitement;
    public int NumOrangesRequired;
}

struct HobbyPool
{
    public BlobArray<Hobby> Hobbies;
}

BlobAssetReference<HobbyPool> CreateHobbyPool()
{
    var builder = new BlobBuilder(Allocator.Temp);
    ref HobbyPool hobbyPool = ref builder.ConstructRoot<HobbyPool>();

    // Allocate enough room for two hobbies in the pool. Use the returned BlobBuilderArray
    // to fill in the data.
    const int numHobbies = 2;
    BlobBuilderArray<Hobby> arrayBuilder = builder.Allocate(
        ref hobbyPool.Hobbies,
        numHobbies
    );

    // Initialize the hobbies.

    // An exciting hobby that consumes a lot of oranges.
    arrayBuilder[0] = new Hobby
    {
        Excitement = 1,
        NumOrangesRequired = 7
    };

    // A less exciting hobby that conserves oranges.
    arrayBuilder[1] = new Hobby
    {
        Excitement = 0.2f,
        NumOrangesRequired = 2
    };

    var result = builder.CreateBlobAssetReference<HobbyPool>(Allocator.Persistent);
    builder.Dispose();
    return result;
}
```

## Strings in Blob Assets

You must use the [BlobString](../api/Unity.Entities.BlobString.html) type to create a string within a blob asset. The following is an example of a string allocated with the `BlobBuilder` API.

```csharp
struct CharacterSetup
{
    public float Loveliness;
    public BlobString Name;
}

BlobAssetReference<CharacterSetup> CreateCharacterSetup(string name)
{
    var builder = new BlobBuilder(Allocator.Temp);
    ref CharacterSetup character = ref builder.ConstructRoot<CharacterSetup>();

    character.Loveliness = 9001; // it's just a very lovely character

    // Create a new BlobString and set it to the given name.
    builder.AllocateString(ref character.Name, name);

    var result = builder.CreateBlobAssetReference<CharacterSetup>(Allocator.Persistent);
    builder.Dispose();
    return result;
}
```

## Internal Pointers

To manually set an internal pointer, use the [`BlobPtr<T>`](../api/Unity.Entities.BlobPtr-1.html) type.

```csharp
struct FriendList
{
    public BlobPtr<BlobString> BestFriend;
    public BlobArray<BlobString> Friends;
}

BlobAssetReference<FriendList> CreateFriendList()
{
    var builder = new BlobBuilder(Allocator.Temp);
    ref FriendList friendList = ref builder.ConstructRoot<FriendList>();

    const int numFriends = 3;
    var arrayBuilder = builder.Allocate(ref friendList.Friends, numFriends);
    builder.AllocateString(ref arrayBuilder[0], "Alice");
    builder.AllocateString(ref arrayBuilder[1], "Bob");
    builder.AllocateString(ref arrayBuilder[2], "Joachim");

    // Set the best friend pointer to point to the second array element.
    builder.SetPointer(ref friendList.BestFriend, ref arrayBuilder[2]);

    var result = builder.CreateBlobAssetReference<FriendList>(Allocator.Persistent);
    builder.Dispose();
    return result;
}
```

## Accessing Blob Assets on a Component

Once you've made a `BlobAssetReference<T>` to a blob asset, you can store this reference on component and access it. You must access all parts of a blob asset that contain internal pointers [by reference](blob-assets-concept.html#supported-data).

```csharp
struct Hobbies : IComponentData
{
    public BlobAssetReference<HobbyPool> Blob;
}

float GetExcitingHobby(ref Hobbies component, int numOranges)
{
    // Get a reference to the pool of available hobbies. Note that it needs to be passed by
    // reference, because otherwise the internal reference in the BlobArray would be invalid.
    ref HobbyPool pool = ref component.Blob.Value;

    // Find the most exciting hobby we can participate in with our current number of oranges.
    float mostExcitingHobby = 0;
    for (int i = 0; i < pool.Hobbies.Length; i++)
    {
        // This is safe to use without a reference, because the Hobby struct does not
        // contain internal references.
        var hobby = pool.Hobbies[i];
        if (hobby.NumOrangesRequired > numOranges)
            continue;
        if (hobby.Excitement >= mostExcitingHobby)
            mostExcitingHobby = hobby.Excitement;
    }

    return mostExcitingHobby;
}
```

## Dispose a Blob Asset Reference

Any blob assets that you allocate at runtime with [BlobBuilder.CreateBlobAssetReference](../api/Unity.Entities.BlobBuilder.CreateBlobAssetReference.html) need to be disposed manually.

However, you don't need to manually dispose of any blob assets that were loaded as part of an entity scene loaded from disk. All of these blob assets are reference counted and automatically released once no component references them anymore.

```csharp
public partial struct BlobAssetInRuntimeSystem : ISystem
{
    private BlobAssetReference<MarketData> _blobAssetReference;

    public void OnCreate(ref SystemState state)
    {
        using (var builder = new BlobBuilder(Allocator.Temp))
        {
            ref MarketData marketData = ref builder.ConstructRoot<MarketData>();
            marketData.PriceApples = 2f;
            marketData.PriceOranges = 4f;
            _blobAssetReference =
                builder.CreateBlobAssetReference<MarketData>(Allocator.Persistent);
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        // Calling Dispose on the BlobAssetReference will destroy the referenced
        // BlobAsset and free its memory
        _blobAssetReference.Dispose();
    }
}
```

## Debugging Blob Asset Contents

Blob assets use relative offsets to implement internal references. This means that copying a `BlobString` struct, or any other type with these internal references, copies the relative offset contained, but not what it's pointing to. The result of this is an unusable `BlobString` that represents a random string of characters. While this is easy to avoid in your own code, debugging utilities often do exactly that. Therefore, the contents of a `BlobString` aren't displayed correctly in a debugger.

However, there is support for displaying the values of a `BlobAssetReference<T>` and all its contents. If you want to look up the contents of a `BlobString`, navigate to the containing `BlobAssetReference<T>` and start debugging from there.

## Blob Assets in Baking

You can use [bakers and baking systems](baking.html) to create blob assets offline and have them be available in runtime.

To handle blob assets, the [BlobAssetStore](../api/Unity.Entities.BlobAssetStore.html) is used. The `BlobAssetStore` keeps internal ref counting and ensures that blob assets are disposed if nothing references it anymore. The Bakers internally have access to a `BlobAssetStore`, but to create blob assets in a Baking System, you need to retrieve the `BlobAssetStore` from the Baking System.

## Register a Blob Asset with a Baker

Because bakers are deterministic and incremental, you need to follow some extra steps to use blob assets in baking. As well as creating a [BlobAssetReference](../api/Unity.Entities.BlobAssetReference-1.html) with the [BlobBuilder](../api/Unity.Entities.BlobBuilder.html), you need to register the BlobAsset to the baker.

To register the blob asset to the baker, you call [AddBlobAsset](../api/Unity.Entities.IBaker.AddBlobAsset.html) with the BlobAssetReference:

```csharp
struct MarketData
{
    public float PriceOranges;
    public float PriceApples;
}

struct MarketDataComponent : IComponentData
{
    public BlobAssetReference<MarketData> Blob;
}

public class MarketDataAuthoring : MonoBehaviour
{
    public float PriceOranges;
    public float PriceApples;
}

class MarketDataBaker : Baker<MarketDataAuthoring>
{
    public override void Bake(MarketDataAuthoring authoring)
    {
        // Create a new builder that will use temporary memory to construct the blob asset
        var builder = new BlobBuilder(Allocator.Temp);

        // Construct the root object for the blob asset. Notice the use of `ref`.
        ref MarketData marketData = ref builder.ConstructRoot<MarketData>();

        // Now fill the constructed root with the data:
        // Apples compare to Oranges in the universally accepted ratio of 2 : 1 .
        marketData.PriceApples = authoring.PriceApples;
        marketData.PriceOranges = authoring.PriceOranges;

        // Now copy the data from the builder into its final place, which will
        // use the persistent allocator
        var blobReference =
            builder.CreateBlobAssetReference<MarketData>(Allocator.Persistent);

        // Make sure to dispose the builder itself so all internal memory is disposed.
        builder.Dispose();

        // Register the Blob Asset to the Baker for de-duplication and reverting.
        AddBlobAsset<MarketData>(ref blobReference, out var hash);
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new MarketDataComponent() {Blob = blobReference});
    }
}
```

**Important**

If you don't register a blob asset to the baker, the ref counting doesn't update and the blob asset could be de-allocated unexpectedly.

The baker uses `BlobAssetStore` to de-duplicate and refcount the blob assets. It also decreases the ref counting of the associated blob assets to revert the blob assets when the baker is re-run. Without this step, the bakers would break incremental behaviour. Because of this, the `BlobAssetStore` isn't available directly from the baker, and only through baker methods.

### De-duplication with Custom Hashes

The previous example let the baker handle all de-duplication, but that means you have to create the blob asset first before the baker de-duplicates and disposes the extra blob asset. In some cases you might want to de-duplicate before the blob asset is created in the baker.

To do this, you can use a custom hash instead of letting the baker generate one. If multiple bakers either have access to, or generate the same hash for the same blob assets, you can use this hash to de-duplicate before generating a blob asset. Use [TryGetBlobAssetReference](../api/Unity.Entities.IBaker.TryGetBlobAssetReference.html) to check if the custom hash is already registered to the baker:

```csharp
class MarketDataCustomHashBaker : Baker<MarketDataAuthoring>
{
    public override void Bake(MarketDataAuthoring authoring)
    {
        var customHash = new Unity.Entities.Hash128(
            (uint) authoring.PriceOranges.GetHashCode(),
            (uint) authoring.PriceApples.GetHashCode(), 0, 0);

        if (!TryGetBlobAssetReference(customHash,
                out BlobAssetReference<MarketData> blobReference))
        {
            // Create a new builder that will use temporary memory to construct the blob asset
            var builder = new BlobBuilder(Allocator.Temp);

            // Construct the root object for the blob asset. Notice the use of `ref`.
            ref MarketData marketData = ref builder.ConstructRoot<MarketData>();

            // Now fill the constructed root with the data:
            // Apples compare to Oranges in the universally accepted ratio of 2 : 1 .
            marketData.PriceApples = authoring.PriceApples;
            marketData.PriceOranges = authoring.PriceOranges;

            // Now copy the data from the builder into its final place, which will
            // use the persistent allocator
            blobReference =
                builder.CreateBlobAssetReference<MarketData>(Allocator.Persistent);

            // Make sure to dispose the builder itself so all internal memory is disposed.
            builder.Dispose();

            // Register the Blob Asset to the Baker for de-duplication and reverting.
            AddBlobAssetWithCustomHash<MarketData>(ref blobReference, customHash);
        }

        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new MarketDataComponent() {Blob = blobReference});
    }
}
```

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
