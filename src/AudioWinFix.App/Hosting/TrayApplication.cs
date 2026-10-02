using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioWinFix.App.Resources;
using AudioWinFix.App.Settings;
using AudioWinFix.Core.Audio;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using Wpf.Ui.Controls;
// Wpf.Ui.Controls redefines both of these. The menu wants the plain WPF
// MenuItem — the Fluent one is for NavigationView — and the message box wants
// the Fluent one.
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace AudioWinFix.App.Hosting;

/// <summary>
/// The notification-area icon and its menu — the app's only persistent UI.
/// </summary>
public sealed class TrayApplication : IDisposable
{
    private const int TooltipMaxLength = 127;
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger<TrayApplication> logger;
    private readonly IHostApplicationLifetime lifetime;
    private readonly IAudioMonitor monitor;
    private readonly VolumeGuard volumeGuard;
    private readonly AudioController controller;
    private readonly AutoStartManager autoStart;
    private readonly AppUpdater updater;

    private readonly TaskbarIcon trayIcon;
    private readonly DispatcherTimer statusTimer;
    private readonly MenuItem pauseItem;
    private readonly MenuItem autoStartItem;

    private SettingsWindow? settingsWindow;
    private bool disposed;

    public TrayApplication(
        ILogger<TrayApplication> logger,
        IHostApplicationLifetime lifetime,
        IAudioMonitor monitor,
        VolumeGuard volumeGuard,
        AudioController controller,
        AutoStartManager autoStart,
        AppUpdater updater)
    {
        this.logger = logger;
        this.lifetime = lifetime;
        this.monitor = monitor;
        this.volumeGuard = volumeGuard;
        this.controller = controller;
        this.autoStart = autoStart;
        this.updater = updater;

        pauseItem = Item(Strings.MenuPause, OnPauseToggled);
        autoStartItem = new MenuItem
        {
            Header = Strings.MenuStartWithWindows,
            IsCheckable = true,
            IsChecked = autoStart.IsEnabled,
        };
        autoStartItem.Click += OnAutoStartToggled;

        var menu = new ContextMenu();
        menu.Items.Add(pauseItem);
        menu.Items.Add(BuildDevicesMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.MenuSettings, OnSettingsClicked));
        menu.Items.Add(autoStartItem);
        menu.Items.Add(Item(Strings.MenuCheckUpdates, OnCheckForUpdatesClicked));
        menu.Items.Add(Item(Strings.MenuOpenLogFolder, OnOpenLogFolderClicked));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.MenuQuit, OnQuitClicked));

        trayIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(
                new Uri("pack://application:,,,/AudioWinFix;component/AudioWinFix.ico", UriKind.Absolute)),
            ToolTipText = Strings.AppName,
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
        };
        trayIcon.TrayMouseDoubleClick += (_, _) => ShowSettings();
        trayIcon.ForceCreate();

        statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        statusTimer.Tick += (_, _) => RefreshTooltip();
        statusTimer.Start();
        RefreshTooltip();

        lifetime.ApplicationStopping.Register(OnHostStopping);
    }

    private static MenuItem Item(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    private void RefreshTooltip()
    {
        var text = monitor.Paused
            ? Strings.TrayPaused
            : $"{Strings.TrayTooltipHeader}\n{monitor.DescribePins()}";
        trayIcon.ToolTipText = text.Length > TooltipMaxLength
            ? text[..(TooltipMaxLength - 1)] + "…"
            : text;
    }

    private void OnPauseToggled(object? sender, RoutedEventArgs e)
    {
        var paused = !monitor.Paused;
        monitor.Paused = paused;
        volumeGuard.Paused = paused;
        pauseItem.Header = paused ? Strings.MenuResume : Strings.MenuPause;
        logger.LogInformation("Guards {State}", paused ? "paused" : "resumed");
        RefreshTooltip();
    }

    private MenuItem BuildDevicesMenu()
    {
        var root = new MenuItem { Header = Strings.MenuDefaultDevices };
        root.Items.Add(BuildDeviceGroup(Strings.MenuOutputDefault, DataFlow.Render, communications: false));
        root.Items.Add(BuildDeviceGroup(Strings.MenuOutputComm, DataFlow.Render, communications: true));
        root.Items.Add(BuildDeviceGroup(Strings.MenuInputDefault, DataFlow.Capture, communications: false));
        root.Items.Add(BuildDeviceGroup(Strings.MenuInputComm, DataFlow.Capture, communications: true));
        return root;
    }

    private MenuItem BuildDeviceGroup(string label, DataFlow flow, bool communications)
    {
        var group = new MenuItem { Header = label };
        // Placeholder so the submenu arrow shows; repopulated live each time it opens.
        group.Items.Add(new MenuItem { Header = Strings.MenuNoDevices, IsEnabled = false });
        group.SubmenuOpened += (_, e) =>
        {
            // SubmenuOpened bubbles: only react to this group's own submenu.
            if (ReferenceEquals(e.OriginalSource, group)) PopulateDeviceGroup(group, flow, communications);
        };
        return group;
    }

    private void PopulateDeviceGroup(MenuItem group, DataFlow flow, bool communications)
    {
        group.Items.Clear();
        IReadOnlyList<AudioDeviceInfo> devices;
        try
        {
            devices = controller.List(flow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Listing {Flow} devices failed", flow);
            devices = [];
        }

        if (devices.Count == 0)
        {
            group.Items.Add(new MenuItem { Header = Strings.MenuNoDevices, IsEnabled = false });
            return;
        }

        foreach (var d in devices)
        {
            var id = d.Id;
            var isCurrent = communications ? d.IsDefaultComm : d.IsDefault;
            var item = new MenuItem
            {
                Header = d.Name,
                // WPF-UI only draws IsChecked on checkable items, and a checkbox
                // reads as a toggle; a checkmark icon marks the current device.
                Icon = isCurrent ? new SymbolIcon(SymbolRegular.Checkmark24) : null,
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
            };
            item.Click += async (_, _) => await OnPickDeviceAsync(id, flow, communications).ConfigureAwait(true);
            group.Items.Add(item);
        }
    }

    private async Task OnPickDeviceAsync(string id, DataFlow flow, bool communications)
    {
        try
        {
            controller.SetDefault(id, flow, communications);
            RefreshTooltip();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Setting default device failed");
            await ShowMessageAsync(Strings.AppName, Strings.DeviceSwitchFailed(ex.Message)).ConfigureAwait(true);
        }
    }

    private void OnAutoStartToggled(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (autoStart.IsEnabled) autoStart.Disable();
            else autoStart.Enable();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to toggle auto-start");
        }
        // IsCheckable flips the tick on click; re-sync it with what actually happened.
        autoStartItem.IsChecked = autoStart.IsEnabled;
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e) => ShowSettings();

    private void ShowSettings()
    {
        // A tray app has no owner window to be modal against, and ShowDialog on
        // a repeat click would deadlock behind the first one. Keep a single
        // instance and raise it instead.
        if (settingsWindow is { IsLoaded: true })
        {
            settingsWindow.Activate();
            return;
        }

        settingsWindow = new SettingsWindow(controller, logger);
        settingsWindow.Closed += (_, _) =>
        {
            var restart = settingsWindow?.LanguageChanged ?? false;
            settingsWindow = null;
            if (restart) RestartForLanguage();
        };
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    /// <summary>
    /// WPF has no Application.Restart. Start a fresh copy of this exe, then stop
    /// the host behind it; the new instance reads the language before any
    /// string is shown.
    /// </summary>
    private void RestartForLanguage()
    {
        logger.LogInformation("Language changed; restarting");
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Relaunch after language change failed");
        }
        lifetime.StopApplication();
    }

    private async void OnCheckForUpdatesClicked(object? sender, RoutedEventArgs e)
    {
        if (!updater.IsInstalled)
        {
            await ShowMessageAsync(Strings.AppName, Strings.UpdatesNotInstalledMessage).ConfigureAwait(true);
            return;
        }

        trayIcon.ShowNotification(Strings.AppName, Strings.UpdatesCheckingBalloon, NotificationIcon.Info);
        try
        {
            var update = await updater.CheckForUpdatesAsync().ConfigureAwait(true);
            if (update is null)
            {
                trayIcon.ShowNotification(
                    Strings.AppName,
                    Strings.UpdatesUpToDateBalloon(updater.CurrentVersion ?? "?"),
                    NotificationIcon.Info);
                return;
            }

            var confirmed = await ConfirmAsync(
                Strings.UpdatesAvailableTitle,
                Strings.UpdatesAvailablePrompt(update.TargetFullRelease.Version.ToString())).ConfigureAwait(true);
            if (confirmed)
            {
                await updater.DownloadAndApplyAsync(update).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update check failed");
            await ShowMessageAsync(Strings.AppName, Strings.UpdatesCheckFailed(ex.Message)).ConfigureAwait(true);
        }
    }

    private static async Task ShowMessageAsync(string title, string content)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = content,
            CloseButtonText = Strings.DialogClose,
        };
        await box.ShowDialogAsync().ConfigureAwait(true);
    }

    private static async Task<bool> ConfirmAsync(string title, string content)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = content,
            PrimaryButtonText = Strings.DialogYes,
            CloseButtonText = Strings.DialogNo,
        };
        var result = await box.ShowDialogAsync().ConfigureAwait(true);
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private void OnOpenLogFolderClicked(object? sender, RoutedEventArgs e)
    {
        var logs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioWinFix", "logs");
        try
        {
            Directory.CreateDirectory(logs);
            Process.Start(new ProcessStartInfo(logs) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open log folder at {Path}", logs);
        }
    }

    private void OnQuitClicked(object? sender, RoutedEventArgs e)
    {
        logger.LogInformation("Quit requested from tray");
        lifetime.StopApplication();
    }

    private void OnHostStopping()
    {
        Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        statusTimer.Stop();
        trayIcon.Dispose();
    }
}
