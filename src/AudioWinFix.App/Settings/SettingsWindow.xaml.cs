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

namespace AudioWinFix.App.Settings;

/// <summary>
/// Settings window: the auto-switch threshold, the UI language, and per-device
/// volume locks. Saving writes settings.json, which the host watches; the caller
/// restarts the app if <see cref="LanguageChanged"/> is set once it closes.
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    private readonly AudioController controller;
    private readonly ILogger logger;
    private readonly AppConfig loaded;
    private readonly List<(System.Windows.Controls.CheckBox Box, AudioDeviceInfo Device)> volumeRows = [];

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
        PopulateVolumeRows(loaded.Volume);
    }

    /// <summary>True once a save changed the UI language; the caller restarts on close.</summary>
    public bool LanguageChanged { get; private set; }

    private void PopulateVolumeRows(VolumeOptions current)
    {
        var locked = current.Locks.Select(l => l.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<AudioDeviceInfo> devices;
        try
        {
            devices = controller.List(DataFlow.Render).Concat(controller.List(DataFlow.Capture)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Listing devices for the settings window failed");
            devices = [];
        }

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
}
