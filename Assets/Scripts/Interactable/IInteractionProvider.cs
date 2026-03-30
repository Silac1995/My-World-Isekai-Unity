using System.Collections.Generic;

/// <summary>
/// Interface for character capabilities that provide interaction options.
/// Any CharacterSystem can implement this to advertise what interactions it offers.
/// CharacterInteractable collects all providers via Character.GetAll&lt;IInteractionProvider&gt;().
/// </summary>
public interface IInteractionProvider
{
    List<InteractionOption> GetInteractionOptions(Character interactor);
}
