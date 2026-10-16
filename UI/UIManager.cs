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
    static ControlBar _bar;
    static HuntTrackerHud _hud;
    static VBloodListPanel _list;
    static FamiliarPanel _familiars;
    static readonly List<Window> _windows = new();
    static float _appliedScale;
    static double _inGameSince;
    static double _nextRefreshAt;
    static bool _huntsChanged;
    static bool _familiarsChanged;

    // Something the player can move: dragged by Handle (except over Exclude), position saved on release.
    sealed class Draggable
    {
        public RectTransform Root;
        public RectTransform Handle;
        public RectTransform Exclude;
        public bool Centred;        // anchored to the screen centre (windows) vs the top-left (HUD, bar)
        public Action<Vector2> Save;
    }

    static readonly List<Draggable> _draggables = new();
    static Draggable _drag;
    static Vector2 _dragStartMouse;
    static Vector2 _dragStartPosition;
    static bool _dragMoved;      // this drag passed the threshold (saved on release)
    static bool _suppressClick;  // the click that ends a drag shouldn't also press the button

    /// <summary>True while the cursor is over one of our visible panels (read by ClickGuard).</summary>
    public static bool PointerOverUI { get; private set; }

    /// <summary>True while one of our text boxes has keyboard focus (read by TypingLock).</summary>
    public static bool TypingInUI { get; private set; }

    public static void ToggleList() => _list?.Toggle();

    /// <summary>True if the click that's finishing was really the end of a drag (so ignore it).</summary>
    public static bool ConsumeDragClick()
    {
        // The button's click fires before our drag-end handling, so this uses its own flag: clearing
        // _dragMoved here used to skip saving the new position.
        bool suppress = _suppressClick;
        _suppressClick = false;
        return suppress;
    }

    public static void Tick()
    {
        if (!Core.InGame)
        {
            _inGameSince = 0;
            PointerOverUI = false;
            TypingInUI = false;
            _drag = null;
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
        TypingInUI = IsTypingInOurField() || (_familiars?.Capturing ?? false);
        HandleHotkeys();
        _familiars?.Update();
        HandlePointer();

        if (_huntsChanged)
        {
            _huntsChanged = false;
            _hud.Rebuild();
            _list.LayoutRows();
        }
        if (_familiarsChanged)
        {
            _familiarsChanged = false;
            _familiars?.Rebuild();
        }

        if (now >= _nextRefreshAt)
        {
            _nextRefreshAt = now + RefreshInterval;
            _hud.Refresh();
            foreach (var window in _windows) window.Refresh();
        }
        _bar.Refresh(); // cheap (restyles only on change) and keeps the buttons in step with every close
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
        _windows.Add(_list);
        if (Settings.FamiliarsEnabled)
        {
            _familiars = new FamiliarPanel(_root.transform);
            _windows.Add(_familiars);
        }

        // One button per feature. Add new features here.
        var entries = new List<ControlBar.Entry>
        {
            new() { Label = "Tracker", IsOpen = () => _hud.Visible, Toggle = () => _hud.SetVisible(!_hud.Visible) },
            new() { Label = "V Bloods", IsOpen = () => _list.Visible, Toggle = _list.Toggle },
        };
        if (_familiars != null)
            entries.Add(new() { Label = "Familiars", IsOpen = () => _familiars.Visible, Toggle = _familiars.Toggle });
        _bar = new ControlBar(_root.transform, entries);

        _draggables.Add(new Draggable { Root = _bar.Root, Handle = _bar.Grip, Save = p => Settings.BarPosition = p });
        _draggables.Add(new Draggable { Root = _hud.Root, Handle = _hud.Header, Save = p => Settings.HudPosition = p });
        foreach (var window in _windows)
        {
            var w = window;
            _draggables.Add(new Draggable
            {
                Root = w.Root, Handle = w.TitleBar, Exclude = w.CloseButton, Centred = true,
                Save = p => w.SavedPosition = p,
            });
        }

        HuntStore.Changed += OnHuntsChanged;
        FamiliarStore.Changed += OnFamiliarsChanged;
        Core.Log.LogInfo("UI built.");
    }

    public static void Destroy()
    {
        HuntStore.Changed -= OnHuntsChanged;
        FamiliarStore.Changed -= OnFamiliarsChanged;
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _bar = null;
        _hud = null;
        _list = null;
        _familiars = null;
        _windows.Clear();
        _draggables.Clear();
        _drag = null;
        PointerOverUI = false;
        TypingInUI = false;
    }

    // Deferred to the next tick so a click handler never rebuilds the row it's running from.
    static void OnHuntsChanged() => _huntsChanged = true;
    static void OnFamiliarsChanged() => _familiarsChanged = true;

    static void ApplyScale()
    {
        float scale = Settings.UIScale;
        if (Mathf.Approximately(scale, _appliedScale)) return;
        _appliedScale = scale;
        _scaler.referenceResolution = ReferenceResolution / scale;
    }

    static void HandleHotkeys()
    {
        if (IsTypingInGameField() || (_familiars?.Capturing ?? false)) return;

        var key = Settings.ToggleKey;
        if (key != KeyCode.None && Input.GetKeyDown(key)) ToggleList();

        if (_familiars != null && FamiliarStore.TryGetPressed(out string familiar))
            FamiliarService.Summon(familiar, remember: false);
    }

    static TMP_InputField FocusedField()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null) return null;
        var field = selected.GetComponent<TMP_InputField>();
        return field != null && field.isFocused ? field : null;
    }

    static bool IsTypingInGameField() => FocusedField() != null;

    static bool IsTypingInOurField()
    {
        var field = FocusedField();
        return field != null && field.transform.IsChildOf(_root.transform);
    }

    static void HandlePointer()
    {
        Vector2 mouse = Input.mousePosition;
        PointerOverUI = false;
        foreach (var d in _draggables)
        {
            if (!Contains(d.Root, mouse)) continue;
            PointerOverUI = true;
            break;
        }

        if (Input.GetMouseButtonDown(0))
        {
            _dragMoved = false;
            _suppressClick = false;
            if (PointerOverUI) ClickGuard.ArmPhantomGuard();

            // Topmost first: later siblings draw over earlier ones.
            Draggable hit = null;
            foreach (var d in _draggables)
            {
                if (!Contains(d.Handle, mouse) || Contains(d.Exclude, mouse)) continue;
                if (hit == null || d.Root.GetSiblingIndex() > hit.Root.GetSiblingIndex()) hit = d;
            }
            if (hit != null)
            {
                _drag = hit;
                _dragStartMouse = mouse;
                _dragStartPosition = hit.Root.anchoredPosition;
            }
        }

        if (_drag == null) return;

        if (!Input.GetMouseButton(0))
        {
            if (_dragMoved) _drag.Save(_drag.Root.anchoredPosition);
            _drag = null;
            return;
        }

        Vector2 delta = (mouse - _dragStartMouse) / Mathf.Max(0.01f, _canvas.scaleFactor);
        if (!_dragMoved && delta.magnitude * _canvas.scaleFactor < DragThreshold) return;
        _dragMoved = true;
        _suppressClick = true;

        Vector2 size = _canvasRect.rect.size;
        Vector2 position = _dragStartPosition + delta;
        if (_drag.Centred)
        {
            Vector2 half = size * 0.5f;
            position.x = Mathf.Clamp(position.x, -half.x, half.x);
            position.y = Mathf.Clamp(position.y, -half.y, half.y);
        }
        else
        {
            Vector2 rootSize = _drag.Root.sizeDelta;
            position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, size.x - rootSize.x));
            position.y = Mathf.Clamp(position.y, -(size.y - 28f), 0f);
        }
        _drag.Root.anchoredPosition = position;
    }

    static bool Contains(RectTransform rect, Vector2 screenPoint) =>
        rect != null && rect.gameObject.activeInHierarchy
        && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
}
