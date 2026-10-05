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
        Status = state.IsCustomState ? "Custom" : null;
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(Status));
    }

    // ── Apply ────────────────────────────────────────────────────────────────

    private void ApplyToggle(bool enabled)
    {
        if (_definition.RequiresConfirmation)
        {
            _log($"[CONFIRM] {_definition.Name}: {(enabled ? _definition.EnableWarning : _definition.DisableWarning) ?? "apply?"}");
        }
        var result = _executor.ApplyToggle(_definition, enabled);
        Report(result.Success, result);
        Refresh();
    }

    private void ApplySelection(int index)
    {
        if (index < 0) return;
        var result = _executor.ApplySelection(_definition, index);
        Report(result.Success, result);
        Refresh();
    }

    private void Report(bool ok, OperationResult result)
    {
        if (ok)
        {
            Status = null;
            if (result.RequiresRestart is not null) _log($"[RESTART] {result.RequiresRestart}");
        }
        else
        {
            Status = result.ErrorMessage;
            _log($"[ERROR] {result.ErrorMessage}");
        }
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
