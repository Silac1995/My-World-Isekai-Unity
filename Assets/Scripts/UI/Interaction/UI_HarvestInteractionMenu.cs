using System.Collections.Generic;
using UnityEngine;

namespace MWI.UI.Interaction
{
    /// <summary>
    /// Hold-E interaction menu shown when the player hovers a Harvestable. Lists every
    /// option from <see cref="Harvestable.GetInteractionOptions(Character)"/>; greyed-out
    /// rows show the missing-tool reason. See farming spec §6.2.
    ///
    /// Singleton-on-demand: lazy-spawned from Resources/UI/UI_HarvestInteractionMenu.
    /// Renamed from UI_InteractionMenu to avoid collision with the pre-existing
    /// global-namespace UI_InteractionMenu (button-bar UI under Assets/Scripts/UI/WorldUI/).
    /// </summary>
    public class UI_HarvestInteractionMenu : MonoBehaviour
    {
        [SerializeField] private UI_HarvestInteractionOptionRow _rowPrefab;
        [SerializeField] private Transform _rowParent;

        private static UI_HarvestInteractionMenu _instance;
        private Character _actor;
        private Harvestable _target;
        private System.Action _onClosed;
        private readonly List<UI_HarvestInteractionOptionRow> _rows = new List<UI_HarvestInteractionOptionRow>();

        public static UI_HarvestInteractionMenu EnsureInstance()
        {
            if (_instance == null)
            {
                var prefab = Resources.Load<UI_HarvestInteractionMenu>("UI/UI_HarvestInteractionMenu");
                if (prefab == null)
                {
                    Debug.LogError("[UI_HarvestInteractionMenu] Prefab not found at Resources/UI/UI_HarvestInteractionMenu.");
                    return null;
                }
                _instance = Instantiate(prefab);
                DontDestroyOnLoad(_instance.gameObject);
                _instance.gameObject.SetActive(false);
            }
            return _instance;
        }

        public static void Open(Character actor, Harvestable target, System.Action onClosed = null)
        {
            var menu = EnsureInstance();
            if (menu == null)
            {
                onClosed?.Invoke();
                return;
            }
            menu._actor = actor;
            menu._target = target;
            menu._onClosed = onClosed;
            menu.Rebuild();
            menu.gameObject.SetActive(true);
        }

        public static void Close()
        {
            if (_instance == null) return;
            _instance.gameObject.SetActive(false);
            var cb = _instance._onClosed;
            _instance._onClosed = null;
            cb?.Invoke();
        }

        private void Rebuild()
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i] != null) Destroy(_rows[i].gameObject);
            _rows.Clear();

            if (_target == null || _rowPrefab == null || _rowParent == null) return;

            var options = _target.GetInteractionOptions(_actor);
            for (int i = 0; i < options.Count; i++)
            {
                var row = Instantiate(_rowPrefab, _rowParent);
                row.Bind(options[i], OnSelected);
                _rows.Add(row);
            }
        }

        private void OnSelected(HarvestInteractionOption opt)
        {
            if (_actor != null && opt.ActionFactory != null)
            {
                var action = opt.ActionFactory(_actor);
                if (action != null && _actor.CharacterActions != null)
                    _actor.CharacterActions.ExecuteAction(action);
            }
            Close();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }
    }
}
