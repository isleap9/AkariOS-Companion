# AkariOS-Companion — Session Handoff

## What was done

### 1. Status-bar log (`TxtLog`) — all toggle output now visible
- `App.xaml.cs:21` — executor log routes to `App.Tool.Log` when available, falls back to `AppLog.Write`.
- Fixed `??` on `void` compile error with explicit `if/else`.

### 2. Registry engine — faithful port of old `ApplySettingCore`
Old repo moved: `C:\Users\isleap\Documents\GitHub\Akari-Tool-OLD` → `C:\Users\isleap\Documents\GitHub\Akari-Tool`.

- `Services/WindowsRegistryService.cs` — added single write chokepoint `ApplySetting` / `ApplySettingCore` (per-NIC + per-monitor recursion over registry sub-keys, key-existence, composite `key=value;` merge, binary bit/byte, standard write/delete). Added `DeleteKey` with shallow/protected guards (same list as old project). Each branch logs `[REGISTRY]`.
- `Services/SettingOperationExecutor.cs` (279 lines, was 490) — thin caller only. Deleted invented `ResolveTarget`, `FirstNonNull`, `Write`, `InferKind`, `ApplyRegistrySetting`, `ApplyRegistrySettingWithValue`. `ApplyToggle` loops entries → `ApplySetting`. `ApplySelection` writes **every** entry sharing the value name → `ApplySetting(specificValue)`.
- `Services/SettingStateReader.cs` — fixed composite read (was splitting on `:`, now `=` via `ResolveCompositeState` comparing against `EnabledValue`/`DefaultValue`); fixed `BlobBitIsSet` (`!= 0`, was `== mask`); toggle reads require all same-name mirrors to agree; selection reads probe all same-name hives, first present wins.

### 3. Gaming page — no catalog changes, engine covers all 12 groups
Game Mode, Processor, Graphics, Storage, Network, Xbox, Security, System Services, Scheduled Tasks, System Restore, Accessibility, Visual Effects.

### 4. Windows Update page — built from placeholder
- `Models/UpdateCatalog.cs` (new) — all 10 settings, 3 groups, IDs preserved.
- `Services/WindowsUpdatePolicyHandler.cs` (new) — `updates-policy-mode` 4-mode handler (Normal / Security Only / Paused / Disabled): service start-types, task folders, System32 DLL rename/restore with takeown+icacls, SoftwareDistribution cleanup, AU/UX registry sets. Detection probes: renamed DLLs → pause timestamps → `DeferFeatureUpdates` → normal.
- `ViewModels/UpdateViewModel.cs` (new) — mirrors `GamingViewModel`; policy index via handler, selection apply is async fire-and-forget with `IsBusy`.
- `Views/UpdatePage.xaml` (rewritten) — data-driven `ItemsControl`, ComboBox vs ToggleSwitch off `IsSelection`, uses `BoolToVis`/`NullToVis`/`InverseBool`.
- `Views/UpdatePage.xaml.cs` — sets `UpdateViewModel(App.StateReader, App.Executor, App.Registry, App.Tool.Log)`.

### Bugs found via VM testing (fixed)
- Delivery Optimization wrote HKCU only, never HKLM → dropdown looked applied, machine ignored it. Fixed by fan-out.
- Nagle's Algorithm (latent): `{iface}` placeholder substitution wrote to the `Interfaces` container instead of per-adapter sub-keys. Fixed by sub-key enumeration port.

Build state: `dotnet build` clean, 0 warnings, 0 errors.

## What's left to test (on a VM, as admin)

1. Gaming page — toggle each group ON then OFF, confirm real registry values change:
   - DirectX flip/VRR/HDR (`HKCU\...\DirectX\UserGpuPreferences` composite `SwapEffectUpgradeEnable`, `VRROptimizeEnable`, `AutoHDREnable`)
   - Auto Color Management (per-monitor expansion under `GraphicsDrivers\MonitorDataStore`)
   - HAGS / MPO / overlays (delete-on-enable: `HwSchMode`, `OverlayTestMode`, `DisableOverlays`, `OverlayMinFPS`)
   - Nagle's (`HKLM\...\Tcpip\Parameters\Interfaces\{GUID}\TcpAckFrequency`, `TCPNoDelay` per adapter)
   - Storage Sense (HKCU + HKLM `AllowStorageSenseGlobal` agree)
   - Visual Effects binary flags (`UserPreferencesMask` bits)
2. Update page:
   - Delivery Optimization Disabled → both HKCU + HKLM `DODownloadMode=99`; dropdown reads back Disabled
   - Each of the 9 toggles writes its documented UX/policy value
   - Policy modes 0–3: services (`wuauserv`, `UsoSvc`, `WaaSMedicSvc`), task folders, DLL rename/restore, pause timestamps. Mode 3 is destructive (renames System32 DLLs, wipes SoftwareDistribution) — VM snapshot first.
3. Status bar: every toggle prints `[APPLY]` → `[REGISTRY] Applying:` → `[OK]/[FAIL]` → `[DONE]/[RESTART]`. If a setting looks inert, paste the `[REGISTRY]` line — it names the exact key hit.

## What's left to build

- **Privacy page** — placeholder (`Views/PrivacyPage.xaml.cs:8`). Port catalog + ViewModel (same pattern as Update).
- **Notifications page** — placeholder (`Views/NotificationsPage.xaml.cs:8`). Same pattern.
- `ApplyDefault` path (`SettingDefinitionToggleState` vs old `GetParentDisableValue`) — both read `DisabledValue` so they agree, but unverified on a machine.
- Icons: dropped `Icon`/`IconPack` from `UpdateCatalog` (Gaming rows don't bind icons either). Add back if wanted.

## Quick resume commands

```powershell
cd C:\Users\isleap\Documents\AkariOS-Companion
dotnet build
```
