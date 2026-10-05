using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace AkariOSCompanion.Models;

// ── Enums ────────────────────────────────────────────────────────────────────

public enum InputType
{
    Toggle,
    Selection,
    NumericRange,
    Action,
    CheckBox,
}

/// <summary>Which script (if any) a dropdown option runs when selected.</summary>
public enum ScriptOption
{
    Enabled,
    Disabled,

    /// <summary>Apply no script — a deliberate "leave it alone" choice.</summary>
    None,
}

/// <summary>Session a script must run in: SYSTEM (specialize) or the interactive user.</summary>
public enum RunContext
{
    System,
    User,
}

public enum DetectionType
{
    Registry,
    PowerCfg,
    ScheduledTask,
    PowerPlan,
    DnsServer,
    SystemRestore,
    SystemTrayIcons,
}

// ── Per-setting metadata ─────────────────────────────────────────────────────

/// <summary>One selectable option of a Selection (dropdown) setting.</summary>
public sealed record ComboBoxOption
{
    public required string DisplayName { get; init; }

    /// <summary>
    /// Maps a registry value name to the value this option writes. Null value means
    /// "delete this value" (the key-absent sentinel).
    /// </summary>
    public Dictionary<string, object?>? ValueMappings { get; init; }

    public int? SimpleValue { get; init; }

    /// <summary>Script this option runs; <see cref="ScriptOption.None"/> means touch nothing.</summary>
    public ScriptOption? Script { get; init; }

    /// <summary>Values substituted into the script when it runs.</summary>
    public Dictionary<string, string>? ScriptVariables { get; init; }

    public string? Tooltip { get; init; }
    public string? Warning { get; init; }
    public (string Title, string Message)? Confirmation { get; init; }
    public bool IsDefault { get; init; }
    public bool IsRecommended { get; init; }
}

public sealed record ComboBoxMetadata
{
    public required IReadOnlyList<ComboBoxOption> Options { get; init; }
    public string? CustomStateDisplayName { get; init; }
}

public sealed record NumericRangeMetadata
{
    public required double Minimum { get; init; }
    public required double Maximum { get; init; }
    public required double Step { get; init; }
    public required string Unit { get; init; }
}

/// <summary>One registry value backing a setting.</summary>
public sealed record RegistrySetting
{
    public required string KeyPath { get; init; }
    public string? ValueName { get; init; }

    /// <summary>Value written when the user turns the setting on / picks the recommended option.</summary>
    public required object? RecommendedValue { get; init; }

    /// <summary>Windows' own default value, used by the "reset to default" action.</summary>
    public required object? DefaultValue { get; init; }

    /// <summary>Values that count as "on". A null entry means the value being absent means "on".</summary>
    public object?[]? EnabledValue { get; init; }

    /// <summary>Values that count as "off".</summary>
    public object?[]? DisabledValue { get; init; }

    public required RegistryValueKind ValueType { get; init; }

    /// <summary>The value whose state decides the toggle when several are present.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Index of the byte inside a REG_BINARY blob that carries this setting's flag.</summary>
    public int? BinaryByteIndex { get; init; }

    public bool ModifyByteOnly { get; init; }

    /// <summary>Bit mask applied to <see cref="BinaryByteIndex"/> to test the flag.</summary>
    public byte? BitMask { get; init; }

    /// <summary>Sub-key of a composite REG_SZ value that this setting owns.</summary>
    public string? CompositeStringKey { get; init; }

    public bool ApplyPerNetworkInterface { get; init; }
    public bool ApplyPerMonitor { get; init; }
    public bool IsGroupPolicy { get; init; }
    public bool LockKeyAccess { get; init; }
}

public sealed record ScheduledTaskSetting
{
    public string Id { get; init; } = string.Empty;
    public string TaskPath { get; init; } = string.Empty;
    public required bool? RecommendedState { get; init; }
    public required bool? DefaultState { get; init; }
}

public sealed record PowerShellScriptSetting
{
    public string? Id { get; init; }
    public string? Script { get; init; }
    public string? EnabledScript { get; init; }
    public string? DisabledScript { get; init; }
    public string? Purpose { get; init; }
    public bool RequiresElevation { get; init; } = true;

    /// <summary>
    /// Session the script must run in. User context is required for anything
    /// touching HKCU or per-user adapter state.
    /// </summary>
    public RunContext RunContext { get; init; } = RunContext.System;
}

// ── Settings and groups ──────────────────────────────────────────────────────

public abstract record BaseDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? GroupName { get; init; }
    public string? Icon { get; init; }
    public InputType InputType { get; init; } = InputType.Toggle;

    /// <summary>
    /// A null/empty list means "OS default". Values present here are the alternative
    /// mechanisms this setting can be implemented with, tried in order.
    /// </summary>
    public IReadOnlyList<RegistrySetting> RegistrySettings { get; init; } =
        Array.Empty<RegistrySetting>();

    public IReadOnlyList<ScheduledTaskSetting> ScheduledTaskSettings { get; init; } =
        Array.Empty<ScheduledTaskSetting>();

    public IReadOnlyList<PowerShellScriptSetting> PowerShellScripts { get; init; } =
        Array.Empty<PowerShellScriptSetting>();

    public bool RequiresRestart { get; init; }
    public string? RestartService { get; init; }

    public ComboBoxMetadata? ComboBox { get; init; }
    public NumericRangeMetadata? NumericRange { get; init; }

    /// <summary>Which icon set <see cref="Icon"/> is drawn from ("Material" / "Fluent").</summary>
    public string? IconPack { get; init; }

    /// <summary>App version this setting first shipped in; null = original.</summary>
    public string? AddedInVersion { get; init; }

    /// <summary>Named value bundles this setting can be switched to in one action.</summary>
    public Dictionary<int, Dictionary<string, bool>>? SettingPresets { get; init; }

    /// <summary>Non-registry detection strategy, when registry reading is not enough.</summary>
    public DetectionType? DetectionType { get; init; }

    /// <summary>Minimum/maximum Windows build this setting applies to; null = no gate.</summary>
    public int? MinimumBuildNumber { get; init; }
    public int? MaximumBuildNumber { get; init; }
}

public sealed record SettingDefinition : BaseDefinition
{
    public bool RequiresConfirmation { get; init; }
    public bool IsSubjectivePreference { get; init; }

    /// <summary>
    /// Explicit "on" state for toggles whose recommendation cannot be expressed as a
    /// registry value (e.g. enabled == key absent). Wins over per-value derivation.
    /// </summary>
    public bool? RecommendedToggleState { get; init; }

    /// <summary>Explicit Windows-default state, parallel to <see cref="RecommendedToggleState"/>.</summary>
    public bool? DefaultToggleState { get; init; }

    /// <summary>Fallback unrecognised live states to the default option instead of "Custom".</summary>
    public bool ResolveUnmatchedToDefault { get; init; }

    public string? EnableWarning { get; init; }
    public string? DisableWarning { get; init; }
}

public sealed record SettingGroup
{
    public required string Name { get; init; }
    public required string FeatureId { get; init; }
    public required IReadOnlyList<SettingDefinition> Settings { get; init; }
}
