using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Plugins;
using Stellar.Abstractions.Services;
using UnityEngine;

namespace Stellar.PlayerHUD;

/// <summary>
/// Heads-up display showing the local player's HP, stamina, and level bars with animated
/// transitions sourced from <see cref="IPlayerState"/>. Demonstrates the uGUI HUD toolkit:
/// the plugin describes its layout once as a <see cref="HudElement"/> tree and the framework
/// handles rendering, per-tick refresh, and bar animation — the plugin only supplies live
/// values via Funcs.
///
/// Hotkeys (suggested defaults — user can rebind in Settings):
///   F11        toggle visibility
///   Ctrl+F11   pause the snapshot copy (freeze the UI at current values)
/// </summary>
public sealed class Plugin : IStellarPlugin
{
    public string Name => "PlayerHUD";

    private readonly IPluginServices _services;
    private readonly ILocalization _loc;
    private readonly IWindowControl _hud;
    private readonly IHotkeyAction _toggleAction;
    private readonly IHotkeyAction _pauseAction;
    private IColorSlot _hpSlot = null!;
    private IColorSlot _staminaSlot = null!;

    private PlayerSnapshot _snapshot;
    private bool _paused;

    public Plugin(IPluginServices services)
    {
        _services = services;
        _loc = services.Localization;
        _services.Log.Info("[PlayerHUD] plugin constructed");

        RegisterColours();

        // The HUD tree. The Conditional reproduces the IMGUI "Player not loaded" branch
        // (and lets the handler detach pre-login). Fill colours are read from the slots at
        // registration; live recolour is a later enhancement.
        _hud = _services.Windows.Register(new WindowRegistration(
            new WindowSpec(
                "playerhud.main",
                _loc.T("hud.window.title"),
                new WindowRect(2231f, 35f, 306f, 80f),
                WindowCategory.HUD,
                WindowPanelStyle.Borderless)
            {
                Surface          = SurfaceStyle.HudOverlay,
                Anchor           = WindowAnchor.TopLeft,
                Draggable        = true,
                EditModeDragOnly = true,
                StartVisible     = true,
                // Player vitals HUD: draw only while in-world, and hide behind blocking
                // full-screen UI (old AutoHideBehindGameMenus) and the line-selector menu.
                ShouldRender = () => _services.ClientState.Phase == GamePhase.World
                                     && (_services.ClientState.UiState & (GameUIState.Blocking | GameUIState.AnyMenu)) == 0,
            },
            new ConditionalElement(
                When: () => _snapshot.IsAvailable,
                Then: new ColumnElement(new HudElement[]
                {
                    new RowElement(new HudElement[]
                    {
                        new PillElement(() => _loc.TFormat("hud.level", _snapshot.Level)),
                        new TextElement(() => _snapshot.Name ?? _loc.T("hud.unknownName")),
                    }, Gap: 6f),
                    new BarElement(() => Frac(_snapshot.Health, _snapshot.MaxHealth), _hpSlot.Value,
                                   () => $"{_snapshot.Health} / {_snapshot.MaxHealth}", Prefix: _loc.T("hud.hp")),
                    new BarElement(() => Frac(_snapshot.Stamina, _snapshot.MaxStamina), _staminaSlot.Value,
                                   () => $"{_snapshot.Stamina} / {_snapshot.MaxStamina}", Prefix: _loc.T("hud.stamina")),
                    new TextElement(() => _loc.TFormat("hud.pos", _snapshot.Position.X, _snapshot.Position.Y, _snapshot.Position.Z)),
                }, Gap: 4f),
                Else: new TextElement(() => _loc.T("hud.notLoaded")))));

        _toggleAction = _services.Hotkeys.DeclareAction(
            new HotkeyAction(
                Id:              "playerhud.toggle",
                Description:     _loc.T("hud.hotkey.toggle"),
                SuggestedDefault: new KeyBinding(StellarKeyCode.F11)),
            callback: () => _hud.SetVisible(!_hud.IsShown));

        _pauseAction = _services.Hotkeys.DeclareAction(
            new HotkeyAction(
                Id:              "playerhud.pause",
                Description:     _loc.T("hud.hotkey.pause"),
                SuggestedDefault: new KeyBinding(StellarKeyCode.F11, ModifierKeys.Ctrl)),
            callback: TogglePause);

        _services.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _services.Framework.Update -= OnUpdate;
        _hpSlot.Dispose();
        _staminaSlot.Dispose();
        _pauseAction.Dispose();
        _toggleAction.Dispose();
        _hud.Remove();
    }

    private void RegisterColours()
    {
        var registry = _services.Theme.ColorRegistry;
        _hpSlot = registry.Register("PlayerHUD.HpBar.Fill", _loc.T("hud.color.hpBar"), new Dictionary<ThemePreset, ColorRgba>
        {
            [ThemePreset.Default] = ColorRgba.FromHex(0x4CC15Cffu),
            [ThemePreset.Dark]    = ColorRgba.FromHex(0x52A35Effu),
            [ThemePreset.Light]   = ColorRgba.FromHex(0x46C85Effu),
            [ThemePreset.Crimson] = ColorRgba.FromHex(0xE04848ffu),
        });
        _staminaSlot = registry.Register("PlayerHUD.StaminaBar.Fill", _loc.T("hud.color.staminaBar"), new Dictionary<ThemePreset, ColorRgba>
        {
            [ThemePreset.Default] = ColorRgba.FromHex(0xF4A23Fffu),
            [ThemePreset.Dark]    = ColorRgba.FromHex(0xF9A24Bffu),
            [ThemePreset.Light]   = ColorRgba.FromHex(0xF0A53Cffu),
            [ThemePreset.Crimson] = ColorRgba.FromHex(0xFFB871ffu),
        });
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _services.Log.Info($"[PlayerHUD] {(_paused ? "paused" : "resumed")}");
    }

    private void OnUpdate(float deltaTime)
    {
        if (_paused) return;

        var ps = _services.PlayerState;
        _snapshot = new PlayerSnapshot
        {
            IsAvailable = ps.IsAvailable,
            Name        = ps.Name,
            Level       = ps.Level,
            Health      = ps.Health,
            MaxHealth   = ps.MaxHealth,
            Stamina     = ps.Stamina,
            MaxStamina  = ps.MaxStamina,
            Position    = ps.Position,
        };
        _hud.MarkDirty();   // optional hint; the framework also polls at ~10 Hz
    }

    private static float Frac(int v, int max) => max > 0 ? (float)v / max : 0f;

    private struct PlayerSnapshot
    {
        public bool IsAvailable;
        public string? Name;
        public int Level;
        public int Health, MaxHealth, Stamina, MaxStamina;
        public Position3D Position;
    }
}
