using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AkariOSCompanion.Models;
using AkariOSCompanion.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AkariOSCompanion.ViewModels;

/// <summary>One selectable row bound to the Windows Updates page UI.</summary>
public partial class UpdateSettingItem : ObservableObject
{
    private readonly SettingDefinition _definition;
    private readonly ISettingStateReader _reader;
    private readonly ISettingOperationExecutor _executor;
    private readonly WindowsUpdatePolicyHandler? _policyHandler;
    private readonly Action<string> _log;

    private bool _isOn;
    private int _selectedIndex = -1;
    private string? _status;
    private bool _isBusy;

    public string Id => _definition.Id;
    public string Name => _definition.Name;
    public string Description => _definition.Description;
    public bool IsSelection => _definition.InputType == InputType.Selection;
    public string? Warning => BuildWarnings.For(Id);
    public IReadOnlyList<ComboBoxOption> Options =>
        _definition.ComboBox?.Options ?? (IReadOnlyList<ComboBoxOption>)Array.Empty<ComboBoxOption>();

    /// <summary>Disable the control while a long-running policy change is in flight.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public UpdateSettingItem(SettingDefinition definition,
                             ISettingStateReader reader,
                             ISettingOperationExecutor executor,
                             WindowsUpdatePolicyHandler? policyHandler,
                             Action<string> log)
    {
        _definition = definition;
        _reader = reader;
        _executor = executor;
        _policyHandler = policyHandler;
        _log = log;
    }

    public bool IsOn
    {
        get => _isOn;
        set { if (SetProperty(ref _isOn, value)) ApplyToggle(value); }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set { if (SetProperty(ref _selectedIndex, value)) _ = ApplySelectionAsync(value); }
    }

    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    // ── Load live state ─────────────────────────────────────────────────────────

    public void Refresh()
    {
        // The policy dropdown has no single backing value — it needs the composite probe.
        if (_definition.Id == "updates-policy-mode" && _policyHandler is not null)
        {
            _selectedIndex = _policyHandler.GetCurrentPolicyIndex();
            OnPropertyChanged(nameof(SelectedIndex));
            return;
        }

        var state = _reader.ReadState(_definition);
        if (!state.Success)
        {
            Status = state.ErrorMessage;
            return;
        }
        _isOn = state.IsEnabled;
        _selectedIndex = state.CurrentIndex;

        if (state.UnavailableReason is not null) Status = state.UnavailableReason;
        else if (state.IsCustomState) Status = "Custom";
        else if (Status is not ("Applied" or "Applied - restart required")) Status = null;
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(Status));
    }

    // ── Apply ───────────────────────────────────────────────────────────────────

    private void ApplyToggle(bool enabled)
    {
        if (_definition.RequiresConfirmation)
        {
            var warn = enabled ? _definition.EnableWarning : _definition.DisableWarning;
            _log($"[CONFIRM] {_definition.Name}: {warn ?? "apply this change?"}");
        }

        var expectRestart = _definition.RequiresRestart;
        _log($"[APPLY] {_definition.Name}: {(enabled ? "ON" : "OFF")}"
            + (expectRestart ? " (reboot required to take effect)" : ""));

        var result = _executor.ApplyToggle(_definition, enabled);
        Report(result.Success, result, enabled ? "ON" : "OFF", expectRestart);
        Refresh();
    }

    /// <summary>
    /// Dropdown apply. The policy mode takes the special-handler path (services, tasks,
    /// DLL renames); everything else is a plain registry selection write.
    /// </summary>
    private async Task ApplySelectionAsync(int index)
    {
        if (index < 0) return;
        var options = _definition.ComboBox?.Options;
        var label = options is not null && index < options.Count
            ? options[index].DisplayName
            : $"index {index}";

        var expectRestart = _definition.RequiresRestart;

        // ── Special path: Windows Update Policy ──
        if (_definition.Id == "updates-policy-mode" && _policyHandler is not null)
        {
            _log($"[APPLY] {_definition.Name}: \"{label}\""
                + (expectRestart ? " (reboot required to take effect)" : ""));

            IsBusy = true;
            try
            {
                var ok = await _policyHandler.ApplyPolicyModeAsync(index);
                Status = ok ? "Applied - restart required" : "Failed";
                _log(ok
                    ? $"[DONE] {_definition.Name}: \"{label}\" applied. A restart is required."
                    : $"[FAIL] {_definition.Name}: could not apply \"{label}\"");
            }
            catch (Exception ex)
            {
                Status = "Failed";
                _log($"[FAIL] {_definition.Name}: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                Refresh();
            }
            return;
        }

        // ── Standard path ──
        _log($"[APPLY] {_definition.Name}: \"{label}\""
            + (expectRestart ? " (reboot required to take effect)" : ""));

        var result = _executor.ApplySelection(_definition, index);
        Report(result.Success, result, $"\"{label}\"", expectRestart);
        Refresh();
    }

    private void Report(bool ok, OperationResult result, string what, bool expectRestart)
    {
        var name = _definition.Name;

        if (!ok)
        {
            Status = "Failed";
            _log($"[FAIL] {name}: could not apply {what}");
            _log($"       reason: {result.ErrorMessage}");
            foreach (var f in result.Failures) _log($"       failed: {f}");
            return;
        }

        Status = expectRestart ? "Applied - restart required" : "Applied";
        _log($"[OK] {name}: {what} applied ({result.AppliedCount} write(s))");

        foreach (var w in result.AppliedWrites)
            _log($"       wrote {w}");

        if (expectRestart)
            _log($"[RESTART] {name}: a restart is required before this takes effect.");
        else
            _log($"[DONE] {name}: no restart needed.");
    }
}

/// <summary>A catalog section and its rows, for the grouped page layout.</summary>
public sealed class UpdateSection
{
    public string Title { get; }
    public IReadOnlyList<UpdateSettingItem> Items { get; }

    public UpdateSection(string title, IReadOnlyList<UpdateSettingItem> items)
    {
        Title = title;
        Items = items;
    }
}

public partial class UpdateViewModel : ObservableObject
{
    private readonly ISettingStateReader _reader;
    private readonly ISettingOperationExecutor _executor;
    private readonly Action<string> _log;

    public ObservableCollection<UpdateSection> Sections { get; } = new();

    public IEnumerable<UpdateSettingItem> AllSettings =>
        Sections.SelectMany(s => s.Items);

    public UpdateViewModel(ISettingStateReader reader,
                           ISettingOperationExecutor executor,
                           IWindowsRegistryService registry,
                           Action<string> log)
    {
        _reader = reader;
        _executor = executor;
        _log = log;

        var policyHandler = new WindowsUpdatePolicyHandler(registry, log);

        foreach (var group in UpdateCatalog.Build())
            Sections.Add(new UpdateSection(group.Name,
                group.Settings
                      .Select(s => new UpdateSettingItem(s, reader, executor, policyHandler, log))
                      .ToList()));
    }

    /// <summary>
    /// Reads every setting off the machine. Chunked so the UI thread can breathe
    /// between batches and repaint as results arrive.
    /// </summary>
    public async Task LoadAsync()
    {
        var all = AllSettings.ToList();
        const int Chunk = 12;
        for (var i = 0; i < all.Count; i += Chunk)
        {
            foreach (var item in all.Skip(i).Take(Chunk))
                item.Refresh();

            await Task.Yield();
        }
    }
}
