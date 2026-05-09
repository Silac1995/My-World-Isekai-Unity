---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/concepts-entities.html
fetched: 2026-05-05
section: concepts
---

# Entity Concepts

An entity represents something discrete in your program that has its own set of data, such as a character, visual effect, UI element, or even something abstract like a network transaction. Similar to an unmanaged lightweight GameObject, an entity acts as an ID which associates individual unique components together, rather than containing any code or serving as a container for its associated components.

![Conceptual diagram showing Entity A and B sharing the same components of Speed, Direction, Position, and Renderer, plus Entity C having just Speed, Direction, and Position. Entity A and B share an archetype. A system in the middle of the diagram manipulates the Position, Speed, and Direction components.](images/entities-concepts.png)

*Discrete entities labelled Entity A, B, and C.*

Collections of entities are stored in a World, where a world's EntityManager manages all the entities in the world. EntityManager contains methods that you can use to create, destroy, and modify the entities within that world.

## Common EntityManager Methods

| Method | Description |
|--------|-------------|
| CreateEntity | Creates a new entity. |
| Instantiate | Copies an existing entity and creates a new entity from that copy. |
| DestroyEntity | Destroys an existing entity. |
| AddComponent | Adds a component to an existing entity. |
| RemoveComponent | Removes a component from an existing entity. |
| GetComponent | Retrieves the value of an entity's component. |
| SetComponent | Overwrites the value of an entity's component. |

**Note:** When you create or destroy an entity, this is a "structural change", which impacts the performance of your application. For more information, see the documentation on Structural changes.

An entity doesn't have a type, but you can categorize entities by the types of components associated with them. The EntityManager keeps track of the unique combinations of components on existing entities. These unique combinations are called archetypes.

## Entities in the Editor

In the Editor, the following icon represents an Entity: ![Entity icon - a hexagon.](images/editor-entity-icon.png). You'll see this when you use the specific Entities windows and Inspectors.

## Additional Resources

- Introduction to systems
- World concepts
- Archetypes concepts
- Component concepts

---

## Outgoing Hyperlinks

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/class-GameObject.html - GameObject
- ../api/Unity.Entities.World.html - World
- ../api/Unity.Entities.EntityManager.html - EntityManager
- concepts-structural-changes.html - Structural changes
- concepts-archetypes.html - Archetypes concepts
- concepts-components.html - Component concepts
- editor-workflows.html - Entities windows and Inspectors
- systems-intro.html - Introduction to systems
- concepts-worlds.html - World concepts
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
