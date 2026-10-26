using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DragUI.Patches;
using DragUI.Services;

namespace DragUI.UI;

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
    static ExperiencePanel _experience;
    static DpsPanel _dps;
    static CharacterPanel _character;
    static CooldownPanel _cooldowns;
    static LoadoutPanel _loadouts;
    static readonly List<Window> _windows = new();
    static float _appliedScale;
    static double _inGameSince;
    static double _nextRefreshAt;
    static bool _huntsChanged;
    static bool _familiarsChanged;
    static bool _loadoutsChanged;

    // Something the player can move: dragged by Handle (except over Exclude), position saved on release.
    sealed class Draggable
    {
        public RectTransform Root;
        public RectTransform Handle;
        public RectTransform Exclude;
        public bool Centred;        // anchored to the screen centre (windows) vs the top-left (HUD, bar)
        public Action<Vector2> Save;
        public Action Click;        // pressed and released without dragging
    }

    static readonly List<Draggable> _draggables = new();
    static Draggable _drag;
    static Vector2 _dragStartMouse;
    static Vector2 _dragStartPosition;
    static bool _dragMoved;      // this drag passed the threshold (saved on release)
    static bool _suppressClick;  // the click that ends a drag shouldn't also press the button
    static bool _resizing;       // dragging the control bar's resize corner
    static float _resizeStartWidth;

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
        if (_cooldowns != null && _cooldowns.Visible) CooldownTester.Tick(); // per frame, only while it's open
        if (_familiarsChanged)
        {
            _familiarsChanged = false;
            _familiars?.Rebuild();
        }
        if (_loadoutsChanged)
        {
            _loadoutsChanged = false;
            _loadouts?.Rebuild();
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
        _root = new GameObject("DragUI");
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
        if (Settings.CooldownTesterEnabled)
        {
            _cooldowns = new CooldownPanel(_root.transform);
            _windows.Add(_cooldowns);
        }
        if (Settings.SheetEnabled)
        {
            _character = new CharacterPanel(_root.transform);
            _windows.Add(_character);
        }
        if (Settings.LoadoutsEnabled)
        {
            _loadouts = new LoadoutPanel(_root.transform);
            _windows.Add(_loadouts);
        }
        if (Settings.DpsEnabled)
        {
            _dps = new DpsPanel(_root.transform);
            _windows.Add(_dps);
        }
        if (Settings.XpEnabled)
        {
            _experience = new ExperiencePanel(_root.transform);
            _windows.Add(_experience);
        }

        // One button per feature. Add new features here.
        var entries = new List<ControlBar.Entry>
        {
            // The V Blood list opens from the tracker's gear (or its header).
            new() { Label = "Tracker", IsOpen = () => _hud.Visible, Toggle = () => _hud.SetVisible(!_hud.Visible) },
        };
        if (_familiars != null)
            entries.Add(new() { Label = "Familiars", IsOpen = () => _familiars.Visible, Toggle = _familiars.Toggle });
        var character = new List<ControlBar.Entry>();
        if (_character != null)
            character.Add(new() { Label = "Stats", IsOpen = () => _character.Visible, Toggle = _character.Toggle });
        if (_cooldowns != null)
            character.Add(new() { Label = "CD Test", IsOpen = () => _cooldowns.Visible, Toggle = _cooldowns.Toggle });
        if (_loadouts != null)
            character.Add(new() { Label = "Loadouts", IsOpen = () => _loadouts.Visible, Toggle = _loadouts.Toggle });
        if (character.Count > 0)
            entries.Add(new() { Label = "Character", Width = 96f, Children = character });
        if (_dps != null)
            entries.Add(new()
            {
                Label = "DPS", Width = 100f,
                IsOpen = () => _dps.Visible || DamageMeter.Current != null,
                Toggle = _dps.Toggle,
                GetLabel = () => DamageMeter.Latest is { } f ? $"DPS {ExperienceTracker.Format(f.Dps)}" : "DPS -",
            });
        if (_experience != null)
            entries.Add(new()
            {
                Label = "XP", Width = 104f,
                IsOpen = () => _experience.Visible,
                Toggle = _experience.Toggle,
                GetLabel = () => ExperienceTracker.CurrentRate > 0 ? $"XP {ExperienceTracker.Format(ExperienceTracker.CurrentRate)}/h" : "XP -/h",
            });
        entries.Add(new()
        {
            Label = "Auto TP", Width = 92f,
            IsOpen = () => AutoTeleport.Enabled,
            Toggle = () => AutoTeleport.SetEnabled(!AutoTeleport.Enabled),
            GetLabel = () => AutoTeleport.Enabled ? "Auto TP on" : "Auto TP off",
        });
        entries.Add(new()
        {
            Label = "Alarm", Width = 96f,
            IsOpen = () => BossAlarm.Enabled,
            Toggle = () => BossAlarm.SetEnabled(!BossAlarm.Enabled),
            GetLabel = AlarmLabel,
        });
        entries.Add(new()
        {
            Label = "Teleport", Width = 210f,
            IsVisible = () => BossAlarm.Active != null,
            IsFlashing = () => BossAlarm.Flashing,
            IsOpen = () => true,
            Toggle = BossAlarm.TeleportToActive,
            GetLabel = TeleportLabel,
        });
        _bar = new ControlBar(_root.transform, entries);

        var bar = new Draggable { Root = _bar.Root, Handle = _bar.Grip, Save = p => Settings.BarPosition = p };
        bar.Click = () =>
        {
            _bar.ToggleRowColumn();
            _bar.Root.anchoredPosition = ClampToScreen(bar, _bar.Root.anchoredPosition);
            bar.Save(_bar.Root.anchoredPosition);
        };
        _draggables.Add(bar);
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
        FamiliarCatalog.Changed += OnFamiliarsChanged;
        LoadoutStore.Changed += OnLoadoutsChanged;
        Core.Log.LogInfo("UI built.");
    }

    static string AlarmLabel()
    {
        if (!BossAlarm.Enabled) return "Alarm off";
        if (BossAlarm.Next is not { } next) return "Alarm on";
        return $"Alarm {FormatWait(BossAlarm.Until(next))}";
    }

    // Counts down to the spawn while the alarm's lead time runs.
    static string TeleportLabel()
    {
        if (BossAlarm.Active is not { } active) return "Teleport";
        var wait = BossAlarm.Until(active);
        return wait > TimeSpan.Zero
            ? $"TP {active.Event.Label} {(int)wait.TotalMinutes}:{wait.Seconds:00}"
            : $"TP {active.Event.Label}";
    }

    static string FormatWait(TimeSpan wait) =>
        wait.TotalMinutes < 1 ? "<1m" : wait.TotalHours >= 1 ? $"{(int)wait.TotalHours}h{wait.Minutes:00}" : $"{(int)wait.TotalMinutes}m";

    public static void Destroy()
    {
        HuntStore.Changed -= OnHuntsChanged;
        FamiliarStore.Changed -= OnFamiliarsChanged;
        FamiliarCatalog.Changed -= OnFamiliarsChanged;
        LoadoutStore.Changed -= OnLoadoutsChanged;
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _bar = null;
        _hud = null;
        _list = null;
        _familiars = null;
        _experience = null;
        _dps = null;
        _character = null;
        _cooldowns = null;
        _loadouts = null;
        _windows.Clear();
        _draggables.Clear();
        _drag = null;
        _resizing = false;
        PointerOverUI = false;
        TypingInUI = false;
    }

    // Deferred to the next tick so a click handler never rebuilds the row it's running from.
    static void OnHuntsChanged() => _huntsChanged = true;
    static void OnFamiliarsChanged() => _familiarsChanged = true;
    static void OnLoadoutsChanged() => _loadoutsChanged = true;

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
        PointerOverUI = Contains(_bar.OpenMenu, mouse);
        foreach (var d in _draggables)
        {
            if (PointerOverUI) break;
            PointerOverUI = Contains(d.Root, mouse);
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
            if (Contains(_bar.ResizeHandle, mouse) && TopmostAt(mouse) == _bar.Root)
            {
                _resizing = true;
                _dragStartMouse = mouse;
                _resizeStartWidth = _bar.Width;
            }
            else if (hit != null)
            {
                _drag = hit;
                _dragStartMouse = mouse;
                _dragStartPosition = hit.Root.anchoredPosition;
            }
        }

        if (_resizing)
        {
            HandleResize(mouse);
            return;
        }
        if (_drag == null) return;

        if (!Input.GetMouseButton(0))
        {
            if (_dragMoved) _drag.Save(_drag.Root.anchoredPosition);
            else if (Contains(_drag.Handle, mouse)) _drag.Click?.Invoke();
            _drag = null;
            return;
        }

        if (!PassedThreshold(mouse, out Vector2 delta)) return;
        _drag.Root.anchoredPosition = ClampToScreen(_drag, _dragStartPosition + delta);
    }

    // Only the width is dragged: the bar's height follows from how many rows the buttons need.
    static void HandleResize(Vector2 mouse)
    {
        if (!Input.GetMouseButton(0))
        {
            if (_dragMoved) _bar.SaveWidth();
            _resizing = false;
            return;
        }

        if (!PassedThreshold(mouse, out Vector2 delta)) return;
        float maxWidth = _canvasRect.rect.width - _bar.Root.anchoredPosition.x - ControlBar.Chrome;
        _bar.Resize(Mathf.Min(_resizeStartWidth + delta.x, maxWidth));
    }

    /// <summary>Mouse movement since the press, in canvas units, once it's far enough to be a drag.</summary>
    static bool PassedThreshold(Vector2 mouse, out Vector2 delta)
    {
        delta = (mouse - _dragStartMouse) / Mathf.Max(0.01f, _canvas.scaleFactor);
        if (!_dragMoved && delta.magnitude * _canvas.scaleFactor < DragThreshold) return false;
        _dragMoved = true;
        _suppressClick = true;
        return true;
    }

    static Vector2 ClampToScreen(Draggable d, Vector2 position)
    {
        Vector2 size = _canvasRect.rect.size;
        if (d.Centred)
        {
            Vector2 half = size * 0.5f;
            position.x = Mathf.Clamp(position.x, -half.x, half.x);
            position.y = Mathf.Clamp(position.y, -half.y, half.y);
        }
        else
        {
            Vector2 rootSize = d.Root.sizeDelta;
            position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, size.x - rootSize.x));
            position.y = Mathf.Clamp(position.y, -(size.y - 28f), 0f);
        }
        return position;
    }

    // The draggable root drawn on top at this point, if any.
    static RectTransform TopmostAt(Vector2 mouse)
    {
        RectTransform top = null;
        foreach (var d in _draggables)
            if (Contains(d.Root, mouse) && (top == null || d.Root.GetSiblingIndex() > top.GetSiblingIndex()))
                top = d.Root;
        return top;
    }

    static bool Contains(RectTransform rect, Vector2 screenPoint) =>
        rect != null && rect.gameObject.activeInHierarchy
        && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
}
