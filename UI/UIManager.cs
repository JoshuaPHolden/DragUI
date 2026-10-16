using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VBloodHunter.Patches;
using VBloodHunter.Services;

namespace VBloodHunter.UI;

// Owns the mod's canvas. Built a couple of seconds after the player is in game (login is the busiest
// moment for the IL2CPP runtime), hidden on logout, and re-shown on the next login. Dragging is done
// by polling the mouse here rather than with injected IDragHandler types.
internal static class UIManager
{
    const double BuildDelaySeconds = 2;
    const double RefreshInterval = 0.25;
    const float DragThreshold = 4f;
    static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

    static GameObject _root;
    static Canvas _canvas;
    static CanvasScaler _scaler;
    static RectTransform _canvasRect;
    static HuntTrackerHud _hud;
    static VBloodListPanel _list;
    static float _appliedScale;
    static double _inGameSince;
    static double _nextRefreshAt;
    static bool _huntsChanged;

    enum DragTarget { None, Hud, Panel }
    static DragTarget _drag;
    static Vector2 _dragStartMouse;
    static Vector2 _dragStartPosition;
    static bool _dragMoved;

    /// <summary>True while the cursor is over one of our visible panels (read by ClickGuard).</summary>
    public static bool PointerOverUI { get; private set; }

    public static void ToggleList()
    {
        if (_list == null) return;
        _list.SetVisible(!_list.Visible);
    }

    /// <summary>True if the click that's finishing was really the end of a drag (so ignore it).</summary>
    public static bool ConsumeDragClick()
    {
        bool moved = _dragMoved;
        _dragMoved = false;
        return moved;
    }

    public static void Tick()
    {
        if (!Core.InGame)
        {
            _inGameSince = 0;
            PointerOverUI = false;
            _drag = DragTarget.None;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;
        if (_inGameSince == 0) _inGameSince = now;

        if (_root == null)
        {
            if (now - _inGameSince < BuildDelaySeconds) return;
            Build();
        }
        if (!_root.activeSelf) _root.SetActive(true);

        ApplyScale();
        HandleHotkey();
        HandlePointer();

        if (_huntsChanged)
        {
            _huntsChanged = false;
            _hud.Rebuild();
            _list.LayoutRows();
        }

        if (now >= _nextRefreshAt)
        {
            _nextRefreshAt = now + RefreshInterval;
            _hud.Refresh();
            _list.Refresh();
        }
    }

    static void Build()
    {
        _root = new GameObject("VBloodHunterUI");
        Object.DontDestroyOnLoad(_root);
        _root.layer = 5;

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500;
        _canvas.overrideSorting = true;

        _scaler = _root.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        _scaler.matchWidthOrHeight = 1f;
        _appliedScale = 0;
        ApplyScale();

        _root.AddComponent<GraphicRaycaster>();
        _canvasRect = _root.GetComponent<RectTransform>();

        _hud = new HuntTrackerHud(_root.transform);
        _list = new VBloodListPanel(_root.transform);

        HuntStore.Changed += OnHuntsChanged;
        Core.Log.LogInfo("UI built.");
    }

    public static void Destroy()
    {
        HuntStore.Changed -= OnHuntsChanged;
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _hud = null;
        _list = null;
        PointerOverUI = false;
    }

    // Deferred to the next tick so a click handler never rebuilds the row it's running from.
    static void OnHuntsChanged() => _huntsChanged = true;

    static void ApplyScale()
    {
        float scale = Settings.UIScale;
        if (Mathf.Approximately(scale, _appliedScale)) return;
        _appliedScale = scale;
        _scaler.referenceResolution = ReferenceResolution / scale;
    }

    static void HandleHotkey()
    {
        var key = Settings.ToggleKey;
        if (key == KeyCode.None || !Input.GetKeyDown(key) || IsTypingInGameField()) return;
        ToggleList();
    }

    static bool IsTypingInGameField()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null) return false;
        var field = selected.GetComponent<TMP_InputField>();
        return field != null && field.isFocused;
    }

    static void HandlePointer()
    {
        Vector2 mouse = Input.mousePosition;
        PointerOverUI = Contains(_hud.Root, mouse) || (_list.Visible && Contains(_list.Root, mouse));

        if (Input.GetMouseButtonDown(0))
        {
            _dragMoved = false;
            if (PointerOverUI) ClickGuard.ArmPhantomGuard();

            if (Contains(_hud.Header, mouse))
                BeginDrag(DragTarget.Hud, mouse, _hud.Root.anchoredPosition);
            else if (_list.Visible && Contains(_list.TitleBar, mouse) && !Contains(_list.CloseButton, mouse))
                BeginDrag(DragTarget.Panel, mouse, _list.Root.anchoredPosition);
        }

        if (_drag == DragTarget.None) return;

        if (!Input.GetMouseButton(0))
        {
            if (_dragMoved)
            {
                if (_drag == DragTarget.Hud) Settings.HudPosition = _hud.Root.anchoredPosition;
                else Settings.PanelPosition = _list.Root.anchoredPosition;
            }
            _drag = DragTarget.None;
            return;
        }

        Vector2 delta = (mouse - _dragStartMouse) / Mathf.Max(0.01f, _canvas.scaleFactor);
        if (!_dragMoved && delta.magnitude * _canvas.scaleFactor < DragThreshold) return;
        _dragMoved = true;

        Vector2 size = _canvasRect.rect.size;
        Vector2 position = _dragStartPosition + delta;
        if (_drag == DragTarget.Hud)
        {
            Vector2 hudSize = _hud.Root.sizeDelta;
            position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, size.x - hudSize.x));
            position.y = Mathf.Clamp(position.y, -(size.y - 28f), 0f);
            _hud.SetPosition(position);
        }
        else
        {
            Vector2 half = size * 0.5f;
            position.x = Mathf.Clamp(position.x, -half.x, half.x);
            position.y = Mathf.Clamp(position.y, -half.y, half.y);
            _list.SetPosition(position);
        }
    }

    static void BeginDrag(DragTarget target, Vector2 mouse, Vector2 startPosition)
    {
        _drag = target;
        _dragStartMouse = mouse;
        _dragStartPosition = startPosition;
    }

    static bool Contains(RectTransform rect, Vector2 screenPoint) =>
        rect != null && rect.gameObject.activeInHierarchy
        && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
}
