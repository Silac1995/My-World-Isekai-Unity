using System;
using UnityEngine;

/// <summary>
/// Action that synchronizes an animation between two characters.
/// Uses AnimSync to coordinate the animations.
/// </summary>
public class CharacterSyncAnimationAction : CharacterAction
{
    private Character _partner;
    private string _triggerName;
    private AnimSync _animSync;
    private Action<Character, Character> _onSyncEndedHandler;

    /// <summary>
    /// Creates an animation synchronization action.
    /// </summary>
    /// <param name="character">The initiating character</param>
    /// <param name="partner">The partner to synchronize with</param>
    /// <param name="triggerName">Name of the animation trigger to fire</param>
    /// <param name="duration">Duration of the animation (0 = auto-detection)</param>
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
            Debug.LogWarning("[CharacterSyncAnimationAction] The partner is null or dead!");
            return false;
        }

        if (!character.IsFree() || !_partner.IsFree())
        {
            Debug.LogWarning("[CharacterSyncAnimationAction] One of the characters is not free!");
            return false;
        }

        if (AnimSync.IsCharacterSyncing(character) || AnimSync.IsCharacterSyncing(_partner))
        {
            Debug.LogWarning("[CharacterSyncAnimationAction] One of the characters is already synchronizing!");
            return false;
        }

        return true;
    }

    public override void OnStart()
    {
        _animSync = character.GetComponent<AnimSync>();
        if (_animSync == null)
        {
            Debug.LogWarning("[CharacterSyncAnimationAction] No AnimSync on the initiating character.");
            Finish();
            return;
        }

        _onSyncEndedHandler = OnSyncEndedInternal;
        _animSync.OnSyncEnded += _onSyncEndedHandler;
        _animSync.StartSync(_partner, _triggerName, Duration);

        Debug.Log($"<color=cyan>[SyncAction]</color> {character.CharacterName} synchronizes the animation '{_triggerName}' with {_partner.CharacterName}");
    }

    public override void OnCancel()
    {
        UnsubscribeFromAnimSync();
    }

    public override void OnApplyEffect()
    {
        // The effect is applied at the end of the sync via OnSyncEnded
    }

    private void OnSyncEndedInternal(Character _, Character __)
    {
        UnsubscribeFromAnimSync();
        Finish();
    }

    /// <summary>
    /// Unsubscribes from OnSyncEnded only once to avoid leaks and double Finish() calls.
    /// </summary>
    private void UnsubscribeFromAnimSync()
    {
        if (_animSync == null) return;

        _animSync.OnSyncEnded -= _onSyncEndedHandler;
        _onSyncEndedHandler = null;
        _animSync = null;
    }
}
