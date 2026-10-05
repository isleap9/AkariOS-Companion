using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AkariOSCompanion.Models;
using AkariOSCompanion.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AkariOSCompanion.ViewModels;

/// <summary>One selectable row bound to the Gaming page UI.</summary>
public partial class GamingSettingItem : ObservableObject
{
    private readonly SettingDefinition _definition;
    private readonly ISettingStateReader _reader;
    private readonly ISettingOperationExecutor _executor;
    private readonly Action<string> _log;

    private bool _isOn;
    private int _selectedIndex = -1;
    private string? _status;

    public string Id => _definition.Id;
    public string Name => _definition.Name;
    public string Description => _definition.Description;
    public bool IsSelection => _definition.InputType == InputType.Selection;
    public IReadOnlyList<ComboBoxOption> Options =>
        _definition.ComboBox?.Options ?? (IReadOnlyList<ComboBoxOption>)System.Array.Empty<ComboBoxOption>();

    public GamingSettingItem(SettingDefinition definition,
                             ISettingStateReader reader,
                             ISettingOperationExecutor executor,
                             Action<string> log)
    {
        _definition = definition;
        _reader = reader;
        _executor = executor;
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
        set { if (SetProperty(ref _selectedIndex, value)) ApplySelection(value); }
    }

    /// <summary>Non-null when the live value matches no declared option.</summary>
    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    // ── Load live state ──────────────────────────────────────────────────────

    public void Refresh()
    {
        var state = _reader.ReadState(_definition);
        if (!state.Success)
        {
            Status = state.ErrorMessage;
            return;
        }
        _isOn = state.IsEnabled;
        _selectedIndex = state.CurrentIndex;

        // Preserve an outstanding restart notice; only replace the label when the
        // read found a genuinely custom value.
        if (state.IsCustomState) Status = "Custom";
        else if (Status is not ("Applied" or "Applied - restart required")) Status = null;
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(Status));
    }

    // ── Apply ────────────────────────────────────────────────────────────────

    private void ApplyToggle(bool enabled)
    {
        if (_definition.RequiresConfirmation)
        {
            var warn = enabled ? _definition.EnableWarning : _definition.DisableWarning;
            _log($"[CONFIRM] {_definition.Name}: {warn ?? "apply this change?"}");
        }

        // Announce the attempt, the target state, and whether a reboot is expected —
        // before the write, so the user always sees that something is happening.
        var expectRestart = _definition.RequiresRestart;
        _log($"[APPLY] {_definition.Name}: {(enabled ? "ON" : "OFF")}"
            + (expectRestart ? " (reboot required to take effect)" : ""));

        var result = _executor.ApplyToggle(_definition, enabled);
        Report(result.Success, result, enabled ? "ON" : "OFF", expectRestart);
        Refresh();
    }

    private void ApplySelection(int index)
    {
        if (index < 0) return;
        var options = _definition.ComboBox?.Options;
        var label = options is not null && index < options.Count
            ? options[index].DisplayName
            : $"index {index}";

        var expectRestart = _definition.RequiresRestart;
        _log($"[APPLY] {_definition.Name}: \"{label}\""
            + (expectRestart ? " (reboot required to take effect)" : ""));

        var result = _executor.ApplySelection(_definition, index);
        Report(result.Success, result, $"\"{label}\"", expectRestart);
        Refresh();
    }

    /// <summary>
    /// Reports the outcome of every apply to the user: what was attempted, whether
    /// it succeeded, exactly which registry writes landed, and whether a reboot is
    /// needed. Nothing is applied silently.
    /// </summary>
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

        // Row label tells the user the state of the change at a glance.
        Status = expectRestart ? "Applied - restart required" : "Applied";

        _log($"[OK] {name}: {what} applied ({result.AppliedCount} write(s))");

        // Spell out each individual registry write so nothing is hidden.
        foreach (var w in result.AppliedWrites)
            _log($"       wrote {w}");

        if (expectRestart)
            _log($"[RESTART] {name}: a restart is required before this takes effect.");
        else
            _log($"[DONE] {name}: no restart needed.");
    }

}

/// <summary>A catalog section and its rows, for the grouped page layout.</summary>
public sealed class GamingSection
{
    public string Title { get; }
    public IReadOnlyList<GamingSettingItem> Items { get; }

    public GamingSection(string title, IReadOnlyList<GamingSettingItem> items)
    {
        Title = title;
        Items = items;
    }
}

public partial class GamingViewModel : ObservableObject
{
    private readonly ISettingStateReader _reader;
    private readonly ISettingOperationExecutor _executor;
    private readonly Action<string> _log;

    public ObservableCollection<GamingSection> Sections { get; } = new();

    /// <summary>Flat list of every row, used for the batched machine read.</summary>
    public IEnumerable<GamingSettingItem> AllSettings =>
        Sections.SelectMany(s => s.Items);

    public GamingViewModel(ISettingStateReader reader,
                           ISettingOperationExecutor executor,
                           Action<string> log)
    {
        _reader = reader;
        _executor = executor;
        _log = log;

        foreach (var group in GamingCatalog.Build())
            Sections.Add(new GamingSection(group.Name,
                group.Settings.Select(s => new GamingSettingItem(s, reader, executor, log)).ToList()));
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

            // Let the dispatcher repaint between batches.
            await Task.Yield();
        }
    }
}
