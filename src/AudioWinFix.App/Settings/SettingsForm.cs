using AudioWinFix.App.Resources;
using AudioWinFix.Core;
using AudioWinFix.Core.Audio;
using NAudio.CoreAudioApi;

namespace AudioWinFix.App.Settings;

/// <summary>
/// Settings dialog: the auto-switch threshold, the UI language, and per-device
/// volume locks. Collects values into <see cref="Result"/>; the caller persists
/// and restarts if the language changed.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly NumericUpDown thresholdInput;
    private readonly ComboBox languageCombo;
    private readonly List<CheckBox> volumeChecks = new();
    private readonly AudioController controller;

    public AppConfig Result { get; }

    public SettingsForm(AppConfig current, AudioController controller)
    {
        ArgumentNullException.ThrowIfNull(current);
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Result = new AppConfig
        {
            Audio = new() { ThresholdMs = current.Audio.ThresholdMs },
            Language = current.Language,
        };

        Text = Strings.SettingsTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 540);
        Padding = new Padding(16);

        var thresholdLabel = new Label { Text = Strings.SettingsThresholdLabel, Location = new Point(16, 20), AutoSize = true };
        thresholdInput = new NumericUpDown
        {
            Location = new Point(320, 16),
            Width = 150,
            Minimum = 250,
            Maximum = 15000,
            Increment = 250,
            Value = Math.Clamp(current.Audio.ThresholdMs, 250, 15000),
        };
        var helpLabel = new Label
        {
            Text = Strings.SettingsThresholdHelp,
            Location = new Point(16, 48),
            Size = new Size(454, 56),
            ForeColor = SystemColors.GrayText,
        };

        var languageLabel = new Label { Text = Strings.SettingsLanguageLabel, Location = new Point(16, 116), AutoSize = true };
        languageCombo = new ComboBox { Location = new Point(320, 112), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        languageCombo.Items.Add(new LanguageItem("auto", Strings.SettingsLanguageAuto));
        languageCombo.Items.Add(new LanguageItem("en", "English"));
        languageCombo.Items.Add(new LanguageItem("fr", "Français"));
        languageCombo.DisplayMember = nameof(LanguageItem.Display);
        languageCombo.SelectedIndex = current.Language switch { "en" => 1, "fr" => 2, _ => 0 };

        var volumesHeader = new Label
        {
            Text = Strings.SettingsVolumesHeader,
            Location = new Point(16, 156),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
        };
        var volumesHelp = new Label
        {
            Text = Strings.SettingsVolumesHelp,
            Location = new Point(16, 180),
            Size = new Size(454, 56),
            ForeColor = SystemColors.GrayText,
        };
        var volumesPanel = new Panel
        {
            Location = new Point(16, 240),
            Size = new Size(454, 240),
            BorderStyle = BorderStyle.FixedSingle,
            AutoScroll = true,
        };
        PopulateVolumeRows(volumesPanel, current.Volume);

        var saveButton = new Button { Text = Strings.SettingsSave, DialogResult = DialogResult.OK, Location = new Point(294, 496), Width = 85 };
        saveButton.Click += (_, _) => CollectResult();
        var cancelButton = new Button { Text = Strings.SettingsCancel, DialogResult = DialogResult.Cancel, Location = new Point(385, 496), Width = 85 };

        Controls.AddRange([
            thresholdLabel, thresholdInput, helpLabel,
            languageLabel, languageCombo,
            volumesHeader, volumesHelp, volumesPanel,
            saveButton, cancelButton,
        ]);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void PopulateVolumeRows(Panel panel, VolumeOptions current)
    {
        var locked = current.Locks.Select(l => l.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<AudioDeviceInfo> devices;
        try
        {
            devices = controller.List(DataFlow.Render).Concat(controller.List(DataFlow.Capture));
        }
        catch
        {
            return; // no audio devices enumerable
        }

        var y = 8;
        foreach (var d in devices)
        {
            var percent = SafePercent(d.Id);
            var check = new CheckBox
            {
                Text = Strings.SettingsVolumeRow(d.Name, percent),
                Tag = d.Id,
                Location = new Point(8, y),
                AutoSize = true,
                Checked = locked.Contains(d.Id),
            };
            volumeChecks.Add(check);
            panel.Controls.Add(check);
            y += 28;
        }
    }

    private int SafePercent(string id)
    {
        try { return (int)Math.Round(controller.GetVolume(id) * 100); }
        catch { return 0; }
    }

    private void CollectResult()
    {
        Result.Audio.ThresholdMs = (int)thresholdInput.Value;
        Result.Language = ((LanguageItem)languageCombo.SelectedItem!).Code;

        // Snapshot the current level/mute of every ticked device as its lock target.
        Result.Volume.Locks = volumeChecks
            .Where(c => c.Checked)
            .Select(c => (string)c.Tag!)
            .Select(TrySnapshot)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    private VolumeLockEntry? TrySnapshot(string id)
    {
        try
        {
            return new VolumeLockEntry
            {
                DeviceId = id,
                DeviceName = volumeChecks.First(c => (string)c.Tag! == id).Text,
                Level = controller.GetVolume(id),
                Muted = controller.GetMute(id),
            };
        }
        catch
        {
            return null; // device vanished between listing and save
        }
    }

    private sealed record LanguageItem(string Code, string Display);
}
