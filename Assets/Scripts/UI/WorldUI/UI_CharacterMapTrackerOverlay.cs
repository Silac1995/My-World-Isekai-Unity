using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Minimal play-mode debug overlay that displays the local player's CharacterMapTracker
/// NetworkVariable values. Needed because NGO 2.10's NetworkBehaviour editor only renders
/// NetworkVariables of primitive/enum/string types — FixedString128Bytes and Vector3 all
/// print "Type not renderable" in the Inspector, so live values are otherwise invisible.
///
/// Attach this to any always-loaded GameObject (e.g. the scene's UI root). The overlay
/// finds the local player's Character each frame and draws its map-tracker state via
/// OnGUI in the top-left corner.
/// </summary>
public class UI_CharacterMapTrackerOverlay : MonoBehaviour
{
    [Tooltip("Toggle the overlay on/off without having to disable the component.")]
    [SerializeField] private bool _visible = true;

    [Tooltip("Press this key at runtime to toggle the overlay.")]
    [SerializeField] private KeyCode _toggleKey = KeyCode.F6;

    [SerializeField] private int _fontSize = 13;
    [SerializeField] private Vector2 _screenOffset = new Vector2(12f, 12f);

    private GUIStyle _labelStyle;
    private GUIStyle _boxStyle;

    private void Update()
    {
        if (_toggleKey != KeyCode.None && Input.GetKeyDown(_toggleKey))
        {
            _visible = !_visible;
        }
    }

    private void OnGUI()
    {
        if (!_visible) return;
        EnsureStyles();

        var tracker = FindLocalPlayerMapTracker();
        if (tracker == null)
        {
            GUI.Label(new Rect(_screenOffset.x, _screenOffset.y, 400, 40),
                "MapTracker: <no local player>", _labelStyle);
            return;
        }

        string content =
            $"<b>Character Map Tracker</b>\n" +
            $"  Character:       {tracker.name}\n" +
            $"  CurrentMapID:    \"{tracker.CurrentMapID.Value}\"\n" +
            $"  CurrentRegionId: \"{tracker.CurrentRegionId.Value}\"\n" +
            $"  HomeMapId:       \"{tracker.HomeMapId.Value}\"\n" +
            $"  HomePosition:    {tracker.HomePosition.Value}\n" +
            $"  WorldPosition:   {tracker.transform.position}";

        Vector2 size = _labelStyle.CalcSize(new GUIContent(content));
        size.x = Mathf.Max(size.x + 20f, 320f);
        size.y = Mathf.Max(size.y + 16f, 100f);

        var rect = new Rect(_screenOffset.x, _screenOffset.y, size.x, size.y);
        GUI.Box(rect, GUIContent.none, _boxStyle);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f),
            content, _labelStyle);
    }

    private void EnsureStyles()
    {
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = _fontSize;
            _labelStyle.richText = true;
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.wordWrap = false;
        }
        if (_boxStyle == null)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
            tex.Apply();
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = tex;
        }
    }

    private static CharacterMapTracker FindLocalPlayerMapTracker()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return null;

        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null) return null;

        return playerObj.GetComponentInChildren<CharacterMapTracker>(includeInactive: false);
    }
}
