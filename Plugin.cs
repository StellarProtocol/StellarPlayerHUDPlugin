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
    private readonly IHudHandle _hud;
    private readonly IHotkeyAction _toggleAction;
    private readonly IHotkeyAction _pauseAction;
    private IColorSlot _hpSlot = null!;
    private IColorSlot _staminaSlot = null!;

    private PlayerSnapshot _snapshot;
    private bool _paused;

    public Plugin(IPluginServices services)
    {
        _services = services;
        _services.Log.Info("[PlayerHUD] plugin constructed");

        RegisterColours();

        // The HUD tree. The Conditional reproduces the IMGUI "Player not loaded" branch
        // (and lets the handler detach pre-login). Fill colours are read from the slots at
        // registration; live recolour is a later enhancement.
        _hud = _services.Hud.Register(new HudSpec(
            Id: "playerhud.main",
            Anchor: HudAnchor.FreeOverlay,
            DefaultRect: new WindowRect(2231f, 35f, 306f, 80f),
            Root: new ConditionalElement(
                When: () => _snapshot.IsAvailable,
                Then: new ColumnElement(new HudElement[]
                {
                    new RowElement(new HudElement[]
                    {
                        new PillElement(() => $"Lv {_snapshot.Level}"),
                        new TextElement(() => _snapshot.Name ?? "(unknown)"),
                    }, Gap: 6f),
                    new BarElement(() => Frac(_snapshot.Health, _snapshot.MaxHealth), _hpSlot.Value,
                                   () => $"{_snapshot.Health} / {_snapshot.MaxHealth}", Prefix: "HP"),
                    new BarElement(() => Frac(_snapshot.Stamina, _snapshot.MaxStamina), _staminaSlot.Value,
                                   () => $"{_snapshot.Stamina} / {_snapshot.MaxStamina}", Prefix: "Stamina"),
                    new TextElement(() => $"Pos {_snapshot.Position.X:0.0}, {_snapshot.Position.Y:0.0}, {_snapshot.Position.Z:0.0}"),
                }, Gap: 4f),
                Else: new TextElement(() => "Player not loaded"))));

        _toggleAction = _services.Hotkeys.DeclareAction(
            new HotkeyAction(
                Id:              "playerhud.toggle",
                Description:     "Toggle PlayerHUD",
                SuggestedDefault: new KeyBinding(StellarKeyCode.F11)),
            callback: () => _hud.SetVisible(!_hud.IsShown));

        _pauseAction = _services.Hotkeys.DeclareAction(
            new HotkeyAction(
                Id:              "playerhud.pause",
                Description:     "Pause PlayerHUD snapshot refresh",
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
        _hpSlot = registry.Register("PlayerHUD.HpBar.Fill", "HP bar", new Dictionary<ThemePreset, ColorRgba>
        {
            [ThemePreset.Default] = ColorRgba.FromHex(0x4CC15Cffu),
            [ThemePreset.Dark]    = ColorRgba.FromHex(0x52A35Effu),
            [ThemePreset.Light]   = ColorRgba.FromHex(0x46C85Effu),
            [ThemePreset.Crimson] = ColorRgba.FromHex(0xE04848ffu),
        });
        _staminaSlot = registry.Register("PlayerHUD.StaminaBar.Fill", "Stamina bar", new Dictionary<ThemePreset, ColorRgba>
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
