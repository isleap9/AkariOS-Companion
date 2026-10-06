# AkariOS-Companion — Research Memory (OPENCODE-RESEARCH.md)

Scope decision (2026-10-06): keep Visuals + `Models/*Catalog` data for
Gaming, Privacy, Windows Updates, Notifications. Delete ported Akari-Tool
core logic in `Services/` and rewrite engine from scratch.
Validation = Microsoft docs + VM `reg query` + Ultimate scripts (100% trusted).

## 1. VM ground truths (W11 26H2, proven)

- `HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings\IsContinuousInnovationOptedIn`
  `1`=ON, `0`=OFF. Absent = ON (Windows default). Companion `[1,null]/[0]` mapping is correct.
- `CIOptinModified` (REG_QWORD, unix-millis, e.g. `1791255889427`) sits beside it.
  Settings-UI toggle leaves it unchanged. Not a switch. Ignore for read, optionally refresh on write.
- While paused (`PauseUpdatesExpiryTime 2051` + `NoAutoUpdate 1`) Settings forces
  "Get latest updates" display ON even with registry `0`. Must Resume + close/reopen
  Settings (X, not taskkill) before judging a toggle.
- Pause probe values (all 4 must be absent for Normal):
  `PauseUpdatesStartTime`, `PauseUpdatesExpiryTime`, `PausedQualityDate`, `PausedFeatureDate`.
- Full Normal-clear list = 16 UX values (Akari-Tool reference
  `WindowsUpdatePolicyHandler.cs:140-149`):
  `BranchReadinessLevel`, `DeferFeatureUpdates`, `DeferFeatureUpdatesPeriodInDays`,
  `DeferQualityUpdates`, `DeferQualityUpdatesPeriodInDays`,
  `PauseFeatureUpdatesStartTime/EndTime`, `PauseQualityUpdatesStartTime/EndTime`,
  `PauseUpdatesStartTime`, `PauseUpdatesExpiryTime`,
  `PausedQualityDate`, `PausedFeatureDate`,
  `FlightSettingsMaxPauseDays`, `PausedFeatureStatus`, `PausedQualityStatus`.
  Companion drifted (missing the middle 4) → Normal snapped back to Paused. Fixed 1:1.
- Pause write = 8 UX strings (reference `:185-195`):
  Feature/Quality Start=`2025-01-01T00:00:00Z`, End=`2051-12-31T00:00:00Z`,
  `PauseUpdatesStartTime`/`PausedQualityDate`/`PausedFeatureDate`=start,
  `PauseUpdatesExpiryTime`=end, plus `FlightSettingsMaxPauseDays=10023`,
  `PausedFeatureStatus=1`, `PausedQualityStatus=1`.
- Companion log bug: `Describe(rs, enabled, null)` always printed `(value removed)`.
  Fixed to log resolved value. If VM log still shows old text, old exe is running —
  check exe date, copy full `bin\Release\net8.0-windows\`.

## 2. Trusted scripts — AkariOS-Ultimate (C:\Users\isleap\Documents\GitHub\AkariOS-Ultimate)

- `3 Setup\12 Updates Pause.ps1` — pause model to copy. Uses TODAY (UTC
  `yyyy-MM-ddTHH:mm:ssZ`) for starts + `Today+365d` for ends, 6 values:
  `PauseUpdatesExpiryTime`, `PauseFeatureUpdatesEndTime`,
  `PauseFeatureUpdatesStartTime`, `PauseQualityUpdatesEndTime`,
  `PauseQualityUpdatesStartTime`, `PauseUpdatesStartTime`. No AU/policy keys,
  no services/DLL work. Opens `ms-settings:windowsupdate`.
- `2 Refresh\5 Updates Drivers Block.ps1` — block/unblock reference:
  Driver block: `PreventDeviceMetadataFromNetwork=1`, `DisableSendGenericDriverNotFoundToWER=1`,
  `DisableSendRequestAdditionalSoftwareToWER=1`, `SearchOrderConfig=0`,
  `SetAllowOptionalContent=0`, `AllowTemporaryEnterpriseFeatureControl=0`,
  `ExcludeWUDriversInQualityUpdate=1`, `AU\IncludeRecommendedUpdates=0`,
  `AU\EnableFeaturedSoftware=0`. Updates block: `DoNotConnectToWindowsUpdateInternetLocations=1`,
  `UpdateServiceUrlAlternate/WUStatusServer/WUServer=https://fuckyoumicrosoft.com/`,
  `SetDisableUXWUAccess=1`, `ExcludeWUDriversInQualityUpdate=1`,
  `AU\NoAutoUpdate=1`, `AU\UseWUServer=1`. Unblock = `reg delete` each.
- To mine next: `6 Windows\10 Gamemode.ps1`, `19 Gamebar.ps1`, `13 Bloatware.ps1`,
  `33 Defender Optimize.ps1`, notification/store scripts (`3 Setup\11 Store Settings.ps1`).

## 3. Microsoft docs to verify (fill in per setting)

- Update CSP / `waas-wu-settings`: `NoAutoUpdate`, `AUOptions`, `UseWUServer`,
  `ExcludeWUDriversInQualityUpdate`, `DoNotConnectToWindowsUpdateInternetLocations`,
  `SetDisableUXWUAccess`.
- `IsContinuousInnovationOptedIn` 0/1 + `CIOptinModified` semantics on 24H2/26H2.
- Delivery Optimization `DODownloadMode` (1=LAN, 3=LAN+Internet, 99=Bypass) HKCU vs HKLM precedence.
- Store `AutoDownload` 2 vs 4. `AllowMUUpdateService`, `IsExpedited`,
  `AllowAutoWindowsUpdateDownloadOverMeteredNetwork`, `RestartNotificationsAllowed2`.

## 4. New engine contract (rewrite target)

- `RegistrySetting { KeyPath, ValueName, Kind, OnValue, OffValue }` — no
  `EnabledValue[]/DisabledValue[]` sentinel arrays, no composite/binary/per-NIC
  special cases in the generic path. Special cases become explicit per-setting code.
- Every write does read-back verify: write → `ReadValue` → compare → log
  `wrote X=Y verified=True/False`. No silent success.
- Policy modes (Normal/Security/Paused/Disabled) are explicit functions using
  §1 lists + Ultimate pause model (TODAY/+365d vs fixed 2025/2051 — decide one).
- Detection logs which probe hit (`[UPDATE] Detect: ...`).

## 5. VM verification protocol

1. Snapshot. 2. Apply. 3. Paste Companion log. 4. `reg query` target key.
5. Close Settings via X, reopen, screenshot. 6. Reboot, repeat 4–5 for stickiness.
