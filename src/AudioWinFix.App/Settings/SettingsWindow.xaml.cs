using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioWinFix.App.Resources;
using AudioWinFix.Core;
using AudioWinFix.Core.Audio;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AudioWinFix.App.Settings;

/// <summary>
/// Settings window: the auto-switch threshold, the UI language, favorite
/// devices, and per-device volume locks. Saving writes settings.json, which the host watches; the caller
/// restarts the app if <see cref="LanguageChanged"/> is set once it closes.
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    private readonly AudioController controller;
    private readonly ILogger logger;
    private readonly AppConfig loaded;
    private readonly List<(System.Windows.Controls.CheckBox Box, AudioDeviceInfo Device)> volumeRows = [];
    private readonly FavoritesEditor renderFavorites;
    private readonly FavoritesEditor renderCommFavorites;
    private readonly FavoritesEditor captureFavorites;
    private readonly FavoritesEditor captureCommFavorites;

    public SettingsWindow(AudioController controller, ILogger logger)
    {
        this.controller = controller;
        this.logger = logger;

        InitializeComponent();
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);

        Title = Strings.SettingsTitle;
        Titlebar.Title = Strings.SettingsTitle;

        GeneralHeader.Text = Strings.SettingsGeneralHeader;
        ThresholdLabel.Text = Strings.SettingsThresholdLabel;
        ThresholdHelp.Text = Strings.SettingsThresholdHelp;
        LanguageLabel.Text = Strings.SettingsLanguageLabel;
        FavoritesHeader.Text = Strings.SettingsFavoritesHeader;
        FavoritesHelp.Text = Strings.SettingsFavoritesHelp;
        VolumesHeader.Text = Strings.SettingsVolumesHeader;
        VolumesHelp.Text = Strings.SettingsVolumesHelp;
        NoDevicesText.Text = Strings.MenuNoDevices;
        SaveButton.Content = Strings.SettingsSave;
        CancelButton.Content = Strings.SettingsCancel;

        LanguageCombo.ItemsSource = new[]
        {
            new LanguageItem("auto", Strings.SettingsLanguageAuto),
            new LanguageItem("en", "English"),
            new LanguageItem("fr", "Français"),
        };

        // Opened from a click on the UI thread; the file is small and local.
        loaded = AppConfigStore.LoadAsync().GetAwaiter().GetResult();
        ThresholdBox.Value = Math.Clamp(loaded.Audio.ThresholdMs, 250, 15000);
        LanguageCombo.SelectedIndex = loaded.Language switch { "en" => 1, "fr" => 2, _ => 0 };

        var render = SafeList(DataFlow.Render);
        var capture = SafeList(DataFlow.Capture);
        // Same four groups, same labels and order as the tray's device menu.
        var favorites = loaded.Favorites;
        renderFavorites = new FavoritesEditor(Strings.MenuOutputDefault, favorites.Render, render, FavoriteSections);
        renderCommFavorites = new FavoritesEditor(Strings.MenuOutputComm, favorites.RenderCommunications, render, FavoriteSections);
        captureFavorites = new FavoritesEditor(Strings.MenuInputDefault, favorites.Capture, capture, FavoriteSections);
        captureCommFavorites = new FavoritesEditor(Strings.MenuInputComm, favorites.CaptureCommunications, capture, FavoriteSections);
        PopulateVolumeRows(loaded.Volume, render.Concat(capture));
    }

    /// <summary>True once a save changed the UI language; the caller restarts on close.</summary>
    public bool LanguageChanged { get; private set; }

    private IReadOnlyList<AudioDeviceInfo> SafeList(DataFlow flow)
    {
        try
        {
            return controller.List(flow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Listing {Flow} devices for the settings window failed", flow);
            return [];
        }
    }

    private void PopulateVolumeRows(VolumeOptions current, IEnumerable<AudioDeviceInfo> devices)
    {
        var locked = current.Locks.Select(l => l.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in devices)
        {
            var box = new System.Windows.Controls.CheckBox
            {
                Content = Strings.SettingsVolumeRow(d.Name, SafePercent(d.Id)),
                IsChecked = locked.Contains(d.Id),
                Margin = new Thickness(0, 0, 0, 4),
            };
            volumeRows.Add((box, d));
            VolumeRows.Children.Add(box);
        }

        NoDevicesText.Visibility = volumeRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private int SafePercent(string id)
    {
        try { return (int)Math.Round(controller.GetVolume(id) * 100); }
        catch { return 0; }
    }

    private List<VolumeLockEntry> CollectLocks()
    {
        var shown = volumeRows.Select(r => r.Device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Locks on devices that are unplugged right now are not listed, so they
        // cannot have been unticked: keep them rather than silently dropping them.
        var locks = loaded.Volume.Locks.Where(l => !shown.Contains(l.DeviceId)).ToList();

        // Snapshot the current level/mute of every ticked device as its lock target.
        foreach (var (box, device) in volumeRows.Where(r => r.Box.IsChecked == true))
        {
            try
            {
                locks.Add(new VolumeLockEntry
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    Level = controller.GetVolume(device.Id),
                    Muted = controller.GetMute(device.Id),
                });
            }
            catch (Exception ex)
            {
                // Device vanished between listing and save.
                logger.LogWarning(ex, "Could not snapshot volume for {Id}", device.Id);
            }
        }
        return locks;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            // Load-then-write, so anything hand-edited that this window does
            // not show survives a save from here.
            var config = await AppConfigStore.LoadAsync().ConfigureAwait(true);
            config.Audio.ThresholdMs = (int)ThresholdBox.Value.GetValueOrDefault(config.Audio.ThresholdMs);
            config.Language = ((LanguageItem)LanguageCombo.SelectedItem).Code;
            config.Volume.Locks = CollectLocks();
            config.Favorites.Render = renderFavorites.Result;
            config.Favorites.RenderCommunications = renderCommFavorites.Result;
            config.Favorites.Capture = captureFavorites.Result;
            config.Favorites.CaptureCommunications = captureCommFavorites.Result;

            await AppConfigStore.SaveAsync(config).ConfigureAwait(true);
            logger.LogInformation("Settings saved (thresholdMs={Threshold}, language={Lang}, locks={Locks})",
                config.Audio.ThresholdMs, config.Language, config.Volume.Locks.Count);

            LanguageChanged = !string.Equals(config.Language, loaded.Language, StringComparison.Ordinal);
            if (LanguageChanged)
            {
                var box = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Strings.AppName,
                    Content = Strings.AppLanguageRestartMessage,
                    CloseButtonText = Strings.DialogClose,
                };
                await box.ShowDialogAsync().ConfigureAwait(true);
            }
            Close();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Saving settings failed");
            StatusText.Text = Strings.SettingsSaveFailed(ex.Message);
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xed, 0x42, 0x45));
            SaveButton.IsEnabled = true;
        }
    }

    private sealed record LanguageItem(string Code, string Display);

    /// <summary>
    /// One device group's favorites, as its own section appended to a panel: an
    /// ordered list (first = highest priority) with move/remove buttons per row,
    /// and a picker of plugged-in devices to add. Unplugged favorites stay
    /// listed, under their stored name.
    /// </summary>
    private sealed class FavoritesEditor
    {
        private readonly List<FavoriteDevice> items;
        private readonly IReadOnlyList<AudioDeviceInfo> active;
        private readonly StackPanel rows = new();
        private readonly ComboBox candidates = new() { DisplayMemberPath = nameof(AudioDeviceInfo.Name) };
        private readonly Wpf.Ui.Controls.Button add = new()
        {
            Content = Strings.SettingsFavoritesAdd,
            Icon = new SymbolIcon(SymbolRegular.Add24),
            Margin = new Thickness(8, 0, 0, 0),
        };

        public FavoritesEditor(
            string label,
            IEnumerable<FavoriteDevice> current,
            IReadOnlyList<AudioDeviceInfo> active,
            Panel parent)
        {
            this.active = active;

            var picker = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(add, 1);
            picker.Children.Add(candidates);
            picker.Children.Add(add);

            var section = new StackPanel { Margin = new Thickness(0, parent.Children.Count == 0 ? 0 : 16, 0, 0) };
            section.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            section.Children.Add(rows);
            section.Children.Add(picker);
            parent.Children.Add(section);

            // Copies, with names refreshed from the live device where there is one.
            items = current
                .Where(f => !string.IsNullOrEmpty(f.DeviceId))
                .DistinctBy(f => f.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FavoriteDevice
                {
                    DeviceId = f.DeviceId,
                    DeviceName = Find(f.DeviceId)?.Name ?? f.DeviceName,
                })
                .ToList();

            add.Click += (_, _) =>
            {
                if (candidates.SelectedItem is not AudioDeviceInfo d) return;
                items.Add(new FavoriteDevice { DeviceId = d.Id, DeviceName = d.Name });
                Render();
            };
            Render();
        }

        public List<FavoriteDevice> Result => items.ToList();

        private AudioDeviceInfo? Find(string id) =>
            active.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        private void Render()
        {
            rows.Children.Clear();
            if (items.Count == 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = Strings.SettingsFavoritesNone,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.65,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }

            for (var i = 0; i < items.Count; i++)
            {
                rows.Children.Add(Row(i));
            }

            var available = active
                .Where(d => !items.Any(f => string.Equals(f.DeviceId, d.Id, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            candidates.ItemsSource = available;
            candidates.SelectedIndex = available.Count > 0 ? 0 : -1;
            candidates.IsEnabled = add.IsEnabled = available.Count > 0;
        }

        private Grid Row(int index)
        {
            var favorite = items[index];
            var online = Find(favorite.DeviceId) is not null;

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rank = new TextBlock
            {
                Text = $"{index + 1}.",
                Width = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.65,
            };
            var name = new TextBlock
            {
                Text = online ? favorite.DeviceName : Strings.SettingsFavoriteOffline(favorite.DeviceName),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = online ? 1 : 0.65,
            };
            Grid.SetColumn(name, 1);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(IconButton(SymbolRegular.ArrowUp24, Strings.SettingsFavoriteMoveUp, index > 0,
                () => Swap(index, index - 1)));
            buttons.Children.Add(IconButton(SymbolRegular.ArrowDown24, Strings.SettingsFavoriteMoveDown, index < items.Count - 1,
                () => Swap(index, index + 1)));
            buttons.Children.Add(IconButton(SymbolRegular.Delete24, Strings.SettingsFavoriteRemove, enabled: true,
                () => { items.RemoveAt(index); Render(); }));
            Grid.SetColumn(buttons, 2);

            grid.Children.Add(rank);
            grid.Children.Add(name);
            grid.Children.Add(buttons);
            return grid;
        }

        private void Swap(int a, int b)
        {
            (items[a], items[b]) = (items[b], items[a]);
            Render();
        }

        private static Wpf.Ui.Controls.Button IconButton(SymbolRegular symbol, string tooltip, bool enabled, Action onClick)
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Icon = new SymbolIcon(symbol),
                Appearance = ControlAppearance.Transparent,
                ToolTip = tooltip,
                IsEnabled = enabled,
                Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(6),
            };
            button.Click += (_, _) => onClick();
            return button;
        }
    }
}
