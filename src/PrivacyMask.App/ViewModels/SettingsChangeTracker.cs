using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;

namespace PrivacyMask.App.ViewModels;

internal sealed class SettingsChangeTracker : IDisposable
{
    private readonly SettingsViewModel _viewModel;
    private readonly List<INotifyPropertyChanged> _objects = [];
    private readonly List<INotifyCollectionChanged> _collections = [];
    private string _baseline;

    public SettingsChangeTracker(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        _baseline = Snapshot(viewModel.ToModel());
        Attach();
    }

    public bool HasChanges { get; private set; }
    public event Action<bool>? Changed;

    public void SetBaseline(AppSettings settings)
    {
        _baseline = Snapshot(settings);
        Refresh(edited: false);
    }

    private static string Snapshot(AppSettings settings)
    {
        var normalized = AppSettingsCloner.Clone(settings);
        normalized.OnboardingCompleted = true;
        normalized.CurrentMode = RuntimeMode.Standard;
        return JsonSerializer.Serialize(normalized);
    }

    private void Refresh(bool edited = true)
    {
        HasChanges = !string.Equals(_baseline, Snapshot(_viewModel.ToModel()), StringComparison.Ordinal);
        Changed?.Invoke(edited);
    }

    private void Attach()
    {
        Watch(_viewModel);
        WatchCollection(_viewModel.AppProfiles);
        WatchCollection(_viewModel.Hotkeys);
        foreach (var hotkey in _viewModel.Hotkeys) Watch(hotkey);
        foreach (var profile in _viewModel.AppProfiles)
        {
            Watch(profile);
            WatchCollection(profile.Zones);
            foreach (var zone in profile.Zones) Watch(zone);
        }
    }

    private void Watch(INotifyPropertyChanged item)
    {
        _objects.Add(item);
        item.PropertyChanged += PropertyChanged;
    }

    private void WatchCollection(INotifyCollectionChanged collection)
    {
        _collections.Add(collection);
        collection.CollectionChanged += CollectionChanged;
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Detach();
        Attach();
        Refresh();
    }

    private void PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var isPersisted = sender switch
        {
            SettingsViewModel => e.PropertyName is "LaunchAtLogin" or "StartMinimized" or "PanicPin",
            HotkeyBindingViewModel => e.PropertyName == "Enabled",
            AppProfileViewModel => e.PropertyName is "DisplayName" or "Enabled" or "StartupMode"
                or "SelectedPresetId" or "MaskIntensity" or "MaskColor" or "HoverRevealWidthPixels"
                or "HoverRevealHeightPixels" or "Zones" or "ExecutablePath",
            PrivacyZoneViewModel => e.PropertyName is "DisplayName" or "Enabled" or "X" or "Y"
                or "Width" or "Height" or "Strength" or "Style" or "RevealOnHover" or "HideDuringTemporaryReveal",
            _ => false,
        };
        if (!isPersisted) return;
        if (sender is AppProfileViewModel && e.PropertyName == "Zones")
        {
            Detach();
            Attach();
        }
        Refresh();
    }

    private void Detach()
    {
        foreach (var item in _objects) item.PropertyChanged -= PropertyChanged;
        foreach (var collection in _collections) collection.CollectionChanged -= CollectionChanged;
        _objects.Clear();
        _collections.Clear();
    }

    public void Dispose() => Detach();
}
