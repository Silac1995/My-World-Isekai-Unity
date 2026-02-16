using System;
using UnityEngine;

/// <summary>
/// Action qui synchronise une animation entre deux personnages.
/// Utilise AnimSync pour coordonner les animations.
/// </summary>
public class CharacterSyncAnimationAction : CharacterAction
{
    private Character _partner;
    private string _triggerName;
    private AnimSync _animSync;
    private Action<Character, Character> _onSyncEndedHandler;

    /// <summary>
    /// Crée une action de synchronisation d'animation.
    /// </summary>
    /// <param name="character">Le personnage initiateur</param>
    /// <param name="partner">Le partenaire avec qui synchroniser</param>
    /// <param name="triggerName">Nom du trigger d'animation à déclencher</param>
    /// <param name="duration">Durée de l'animation (0 = auto-détection)</param>
    public CharacterSyncAnimationAction(Character character, Character partner, string triggerName, float duration = 0f)
        : base(character, duration)
    {
        _partner = partner ?? throw new ArgumentNullException(nameof(partner));
        _triggerName = triggerName ?? throw new ArgumentNullException(nameof(triggerName));
    }

    public override bool CanExecute()
    {
        if (_partner == null || !_partner.IsAlive())
        {
            Debug.LogWarning("[CharacterSyncAnimationAction] Le partenaire est null ou mort !");
            return false;
        }

        if (!character.IsFree() || !_partner.IsFree())
        {
            Debug.LogWarning("[CharacterSyncAnimationAction] Un des personnages n'est pas libre !");
            return false;
        }

        if (AnimSync.IsCharacterSyncing(character) || AnimSync.IsCharacterSyncing(_partner))
        {
            Debug.LogWarning("[CharacterSyncAnimationAction] Un des personnages est déjà en synchronisation !");
            return false;
        }

        return true;
    }

    public override void OnStart()
    {
        _animSync = character.GetComponent<AnimSync>();
        if (_animSync == null)
        {
            Debug.LogWarning("[CharacterSyncAnimationAction] Aucun AnimSync sur le personnage initiateur.");
            Finish();
            return;
        }

        _onSyncEndedHandler = OnSyncEndedInternal;
        _animSync.OnSyncEnded += _onSyncEndedHandler;
        _animSync.StartSync(_partner, _triggerName, Duration);

        Debug.Log($"<color=cyan>[SyncAction]</color> {character.CharacterName} synchronise l'animation '{_triggerName}' avec {_partner.CharacterName}");
    }

    public override void OnCancel()
    {
        UnsubscribeFromAnimSync();
    }

    public override void OnApplyEffect()
    {
        // L'effet est appliqué à la fin de la sync via OnSyncEnded
    }

    private void OnSyncEndedInternal(Character _, Character __)
    {
        UnsubscribeFromAnimSync();
        Finish();
    }

    /// <summary>
    /// Désabonne de OnSyncEnded une seule fois pour éviter fuites et double Finish().
    /// </summary>
    private void UnsubscribeFromAnimSync()
    {
        if (_animSync == null) return;

        _animSync.OnSyncEnded -= _onSyncEndedHandler;
        _onSyncEndedHandler = null;
        _animSync = null;
    }
}
