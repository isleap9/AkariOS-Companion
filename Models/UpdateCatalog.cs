using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace AkariOSCompanion.Models;

/// <summary>
/// Declarative Windows Update catalog — ported from the old Akari-Tool project's
/// UpdateOptimizations.cs. Three groups (Update Policy → Delivery &amp; Store →
/// Update Behavior), IDs preserved for compatibility.
/// </summary>
public static class UpdateCatalog
{
    public static IReadOnlyList<SettingGroup> Build() =>
    [
        // ══════════════════════════════════════════════════════════════════════
        // UPDATE POLICY
        // ══════════════════════════════════════════════════════════════════════
        new SettingGroup
        {
            Name = "Update Policy",
            FeatureId = "update-policy",
            Settings =
            [
                new SettingDefinition
                {
                    Id = "updates-policy-mode",
                    Name = "Windows Update Policy",
                    Description = "Control how Windows updates are installed on your system",
                    InputType = InputType.Selection,
                    IsSubjectivePreference = true,
                    RegistrySettings = Array.Empty<RegistrySetting>(),
                    ComboBox = new ComboBoxMetadata
                    {
                        Options =
                        [
                            new ComboBoxOption { DisplayName = "Normal (Windows Default)", IsDefault = true },
                            new ComboBoxOption { DisplayName = "Security Updates Only (Recommended)", IsRecommended = true },
                            new ComboBoxOption { DisplayName = "Paused for a long time (Unpause in Settings)" },
                            new ComboBoxOption { DisplayName = "Disabled (NOT Recommended, Security Risk)" },
                        ],
                    },
                },
            ],
        },

        // ══════════════════════════════════════════════════════════════════════
        // DELIVERY & STORE
        // ══════════════════════════════════════════════════════════════════════
        new SettingGroup
        {
            Name = "Delivery & Store",
            FeatureId = "update-delivery",
            Settings =
            [
                new SettingDefinition
                {
                    Id = "updates-delivery-optimization",
                    Name = "Delivery Optimization",
                    Description = "Share downloaded updates with other PCs on your network or the internet to reduce bandwidth usage",
                    InputType = InputType.Selection,
                    IsSubjectivePreference = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_CURRENT_USER\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                            ValueName = "DODownloadMode",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = null,
                            DisabledValue = null,
                            IsPrimary = true,
                        },
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                            ValueName = "DODownloadMode",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = null,
                            DisabledValue = null,
                        },
                    ],
                    ComboBox = new ComboBoxMetadata
                    {
                        Options =
                        [
                            new ComboBoxOption
                            {
                                DisplayName = "Windows Default",
                                IsDefault = true,
                                ValueMappings = new Dictionary<string, object?> { ["DODownloadMode"] = null },
                            },
                            new ComboBoxOption
                            {
                                DisplayName = "Devices on LAN Only",
                                ValueMappings = new Dictionary<string, object?> { ["DODownloadMode"] = 1 },
                            },
                            new ComboBoxOption
                            {
                                DisplayName = "Devices on LAN and Internet",
                                ValueMappings = new Dictionary<string, object?> { ["DODownloadMode"] = 3 },
                            },
                            new ComboBoxOption
                            {
                                DisplayName = "Disabled",
                                IsRecommended = true,
                                ValueMappings = new Dictionary<string, object?> { ["DODownloadMode"] = 0 },
                            },
                        ],
                    },
                },
                new SettingDefinition
                {
                    Id = "updates-store-auto-download",
                    Name = "Microsoft Store Auto-Downloads",
                    Description = "Automatically downloads app updates from the Microsoft Store in the background.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = false,
                    RecommendedToggleState = false,
                    DefaultToggleState = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\WindowsStore",
                            ValueName = "AutoDownload",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = new object?[] { 4, null },
                            DisabledValue = new object?[] { 2 },
                        },
                    ],
                },
            ],
        },

        // ══════════════════════════════════════════════════════════════════════
        // UPDATE BEHAVIOR
        // ══════════════════════════════════════════════════════════════════════
        new SettingGroup
        {
            Name = "Update Behavior",
            FeatureId = "update-behavior",
            Settings =
            [
                new SettingDefinition
                {
                    Id = "updates-latest-updates",
                    Name = "Get Latest Updates",
                    Description = "Receives the latest updates as soon as they are available, before the standard rollout.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = false,
                    RecommendedToggleState = false,
                    DefaultToggleState = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
                            ValueName = "IsContinuousInnovationOptedIn",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = new object?[] { 1, null },
                            DisabledValue = new object?[] { 0 },
                        },
                    ],
                },
                new SettingDefinition
                {
                    Id = "updates-other-products",
                    Name = "Updates for Other Microsoft Products",
                    Description = "Receives updates for other Microsoft products alongside Windows Update.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = true,
                    RecommendedToggleState = false,
                    DefaultToggleState = false,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
                            ValueName = "AllowMUUpdateService",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = new object?[] { 1 },
                            DisabledValue = new object?[] { 0 },
                        },
                    ],
                },
                new SettingDefinition
                {
                    Id = "updates-restart-asap",
                    Name = "Restart as Soon as Possible",
                    Description = "Restarts the device as soon as possible to finish installing updates, even during active hours.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = false,
                    RecommendedToggleState = false,
                    DefaultToggleState = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
                            ValueName = "IsExpedited",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = 1,
                            EnabledValue = new object?[] { 1 },
                            DisabledValue = new object?[] { 0 },
                        },
                    ],
                },
                new SettingDefinition
                {
                    Id = "updates-restart-options",
                    Name = "Managed Restart Options",
                    Description = "Allows users to configure restart options for Windows Update.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = false,
                    RecommendedToggleState = false,
                    DefaultToggleState = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                            ValueName = "NoAutoRebootWithLoggedOnUsers",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = new object?[] { null },
                            DisabledValue = new object?[] { 1 },
                        },
                    ],
                },
                new SettingDefinition
                {
                    Id = "updates-notification-level",
                    Name = "Update Notifications",
                    Description = "Shows notifications about Windows Update activity including restart prompts.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = true,
                    RecommendedToggleState = false,
                    DefaultToggleState = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
                            ValueName = "SetUpdateNotificationLevel",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = new object?[] { null },
                            DisabledValue = new object?[] { 2 },
                        },
                    ],
                },
                new SettingDefinition
                {
                    Id = "updates-restart-notification",
                    Name = "Restart Notification",
                    Description = "Shows a notification before restarting to complete update installation.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = true,
                    RecommendedToggleState = false,
                    DefaultToggleState = false,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
                            ValueName = "RestartNotificationsAllowed2",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = new object?[] { 1 },
                            DisabledValue = new object?[] { 0 },
                        },
                    ],
                },
                new SettingDefinition
                {
                    Id = "updates-metered-connection",
                    Name = "Updates on Metered Connections",
                    Description = "Allows Windows Update to download updates on metered network connections.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = false,
                    RecommendedToggleState = false,
                    DefaultToggleState = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
                            ValueName = "AllowAutoWindowsUpdateDownloadOverMeteredNetwork",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = 1,
                            EnabledValue = new object?[] { 1 },
                            DisabledValue = new object?[] { 0 },
                        },
                    ],
                },
                new SettingDefinition
                {
                    Id = "updates-driver-controls",
                    Name = "Driver Update Controls",
                    Description = "Allows Windows to automatically download and install driver updates through Windows Update.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = true,
                    RecommendedToggleState = false,
                    DefaultToggleState = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
                            ValueName = "ExcludeWUDriversInQualityUpdate",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = new object?[] { null },
                            DisabledValue = new object?[] { 1 },
                        },
                    ],
                },
                new SettingDefinition
                {
                    Id = "updates-driver-coinstallers",
                    Name = "Driver Co-installers",
                    Description = "Allows Windows to install driver co-installers and extension INFs from Windows Update.",
                    InputType = InputType.Toggle,
                    IsSubjectivePreference = true,
                    RecommendedToggleState = false,
                    DefaultToggleState = true,
                    RegistrySettings =
                    [
                        new RegistrySetting
                        {
                            KeyPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer",
                            ValueName = "DisableCoInstallers",
                            ValueType = RegistryValueKind.DWord,
                            RecommendedValue = null,
                            DefaultValue = null,
                            EnabledValue = new object?[] { null },
                            DisabledValue = new object?[] { 1 },
                        },
                    ],
                },
            ],
        },
    ];
}
