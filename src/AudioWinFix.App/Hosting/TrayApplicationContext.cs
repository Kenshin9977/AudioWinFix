using System.Diagnostics;
using AudioWinFix.App.Resources;
using AudioWinFix.App.Settings;
using AudioWinFix.Core;
using AudioWinFix.Core.Audio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AudioWinFix.App.Hosting;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const int TooltipMaxLength = 127; // NotifyIcon.Text hard limit
    private const int StatusRefreshIntervalMs = 1000;

    private readonly ILogger<TrayApplicationContext> logger;
    private readonly IHostApplicationLifetime lifetime;
    private readonly IAudioMonitor monitor;
    private readonly AutoStartManager autoStart;
    private readonly AppUpdater updater;

    private readonly NotifyIcon notifyIcon;
    private readonly System.Windows.Forms.Timer statusTimer;
    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem autoStartItem;

    public TrayApplicationContext(
        ILogger<TrayApplicationContext> logger,
        IHostApplicationLifetime lifetime,
        IAudioMonitor monitor,
        AutoStartManager autoStart,
        AppUpdater updater)
    {
        this.logger = logger;
        this.lifetime = lifetime;
        this.monitor = monitor;
        this.autoStart = autoStart;
        this.updater = updater;

        var menu = new ContextMenuStrip();
        pauseItem = new ToolStripMenuItem(Strings.MenuPause, null, OnPauseToggled);
        autoStartItem = new ToolStripMenuItem(Strings.MenuStartWithWindows, null, OnAutoStartToggled)
        {
            Checked = autoStart.IsEnabled,
        };
        menu.Items.Add(pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.MenuSettings, null, OnSettingsClicked);
        menu.Items.Add(autoStartItem);
        menu.Items.Add(Strings.MenuCheckUpdates, null, OnCheckForUpdatesClicked);
        menu.Items.Add(Strings.MenuOpenLogFolder, null, OnOpenLogFolderClicked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.MenuQuit, null, OnQuitClicked);

        notifyIcon = new NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = Strings.AppName,
            Visible = true,
            ContextMenuStrip = menu,
        };

        statusTimer = new System.Windows.Forms.Timer { Interval = StatusRefreshIntervalMs };
        statusTimer.Tick += (_, _) => RefreshTooltip();
        statusTimer.Start();
        RefreshTooltip();

        lifetime.ApplicationStopping.Register(OnHostStopping);
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
            return SystemIcons.Application;
        }
    }

    private void RefreshTooltip()
    {
        var text = monitor.Paused
            ? Strings.TrayPaused
            : $"{Strings.TrayTooltipHeader}\n{monitor.DescribePins()}";
        if (text.Length > TooltipMaxLength)
        {
            text = text[..(TooltipMaxLength - 1)] + "…";
        }
        notifyIcon.Text = text;
    }

    private void OnPauseToggled(object? sender, EventArgs e)
    {
        monitor.Paused = !monitor.Paused;
        pauseItem.Text = monitor.Paused ? Strings.MenuResume : Strings.MenuPause;
        logger.LogInformation("Monitor {State}", monitor.Paused ? "paused" : "resumed");
        RefreshTooltip();
    }

    private void OnAutoStartToggled(object? sender, EventArgs e)
    {
        try
        {
            if (autoStart.IsEnabled) autoStart.Disable();
            else autoStart.Enable();
            autoStartItem.Checked = autoStart.IsEnabled;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to toggle auto-start");
        }
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        var current = AppConfigStore.LoadAsync().GetAwaiter().GetResult();
        using var form = new SettingsForm(current);
        if (form.ShowDialog() != DialogResult.OK) return;

        AppConfigStore.SaveAsync(form.Result).GetAwaiter().GetResult();
        logger.LogInformation("Settings saved (thresholdMs={Threshold}, language={Lang})",
            form.Result.Audio.ThresholdMs, form.Result.Language);

        if (!string.Equals(form.Result.Language, current.Language, StringComparison.Ordinal))
        {
            MessageBox.Show(Strings.AppLanguageRestartMessage, Strings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Restart();
        }
    }

    private async void OnCheckForUpdatesClicked(object? sender, EventArgs e)
    {
        if (!updater.IsInstalled)
        {
            MessageBox.Show(Strings.UpdatesNotInstalledMessage, Strings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        notifyIcon.ShowBalloonTip(2000, Strings.AppName, Strings.UpdatesCheckingBalloon, ToolTipIcon.Info);
        try
        {
            var update = await updater.CheckForUpdatesAsync().ConfigureAwait(true);
            if (update is null)
            {
                notifyIcon.ShowBalloonTip(3000, Strings.AppName,
                    Strings.UpdatesUpToDateBalloon(updater.CurrentVersion ?? "?"), ToolTipIcon.Info);
                return;
            }

            var choice = MessageBox.Show(
                Strings.UpdatesAvailablePrompt(update.TargetFullRelease.Version.ToString()),
                Strings.UpdatesAvailableTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes)
            {
                await updater.DownloadAndApplyAsync(update).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update check failed");
            MessageBox.Show(Strings.UpdatesCheckFailed(ex.Message), Strings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnOpenLogFolderClicked(object? sender, EventArgs e)
    {
        var logs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioWinFix", "logs");
        Directory.CreateDirectory(logs);
        Process.Start(new ProcessStartInfo(logs) { UseShellExecute = true });
    }

    private void OnQuitClicked(object? sender, EventArgs e)
    {
        logger.LogInformation("Quit requested from tray");
        lifetime.StopApplication();
    }

    private void OnHostStopping()
    {
        statusTimer.Stop();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            statusTimer.Dispose();
            notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
