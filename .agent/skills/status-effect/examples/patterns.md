# Status Effect Patterns

## Creating a new Character Status Effect Definition

1. Create a concrete implementation of `StatusEffect` if needed, or instantiate an existing one in the project.
2. In the Unity Editor, create a new `CharacterStatusEffect` using the Create Asset Menu (`Create/Character Status Effect`).
3. Assign the base `StatusEffect` instances to the `statusEffects` list in the inspector.
4. Setup `Duration`, `Icon`, `Description`, and `VisualEffectPrefab`.

## Code Example: CharacterStatusEffect

```csharp
[CreateAssetMenu(fileName = "CharacterStatusEffect", menuName = "Character Status Effect")]
public class CharacterStatusEffect : ScriptableObject
{
    [SerializeField] private string statusEffectName;
    [SerializeField] private List<StatusEffect> statusEffects;
    [SerializeField] private float duration; // 0 = permanent
    [SerializeField] private GameObject visualEffectPrefab;
    [SerializeField] private Sprite icon;
    [SerializeField] private string description;

    public string StatusEffectName => statusEffectName;
    public IReadOnlyList<StatusEffect> StatusEffects => statusEffects.AsReadOnly();
    public float Duration => duration;
    public GameObject VisualEffectPrefab => visualEffectPrefab;
    public Sprite Icon => icon;
    public string Description => description;
}
```

## Code Example: StatusEffect Base

```csharp
public abstract class StatusEffect : ScriptableObject
{
    // Base class for defining the mechanical parameter changes.
    private string statusName;
    private List<StatsModifier> statsModifier;
}
```
