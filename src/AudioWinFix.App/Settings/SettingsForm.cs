using AudioWinFix.App.Resources;
using AudioWinFix.Core;

namespace AudioWinFix.App.Settings;

/// <summary>
/// Minimal settings dialog: the auto-switch threshold and the UI language.
/// Collects values into <see cref="Result"/>; the caller persists and restarts
/// if the language changed.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly NumericUpDown thresholdInput;
    private readonly ComboBox languageCombo;

    public AppConfig Result { get; }

    public SettingsForm(AppConfig current)
    {
        ArgumentNullException.ThrowIfNull(current);
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
        ClientSize = new Size(460, 250);
        Padding = new Padding(16);

        var thresholdLabel = new Label
        {
            Text = Strings.SettingsThresholdLabel,
            Location = new Point(16, 20),
            AutoSize = true,
        };
        thresholdInput = new NumericUpDown
        {
            Location = new Point(280, 16),
            Width = 150,
            Minimum = 250,
            Maximum = 15000,
            Increment = 250,
            Value = Math.Clamp(current.Audio.ThresholdMs, 250, 15000),
        };
        var helpLabel = new Label
        {
            Text = Strings.SettingsThresholdHelp,
            Location = new Point(16, 52),
            Size = new Size(414, 60),
            ForeColor = SystemColors.GrayText,
        };

        var languageLabel = new Label
        {
            Text = Strings.SettingsLanguageLabel,
            Location = new Point(16, 128),
            AutoSize = true,
        };
        languageCombo = new ComboBox
        {
            Location = new Point(280, 124),
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        languageCombo.Items.Add(new LanguageItem("auto", Strings.SettingsLanguageAuto));
        languageCombo.Items.Add(new LanguageItem("en", "English"));
        languageCombo.Items.Add(new LanguageItem("fr", "Français"));
        languageCombo.DisplayMember = nameof(LanguageItem.Display);
        languageCombo.SelectedIndex = current.Language switch { "en" => 1, "fr" => 2, _ => 0 };

        var saveButton = new Button
        {
            Text = Strings.SettingsSave,
            DialogResult = DialogResult.OK,
            Location = new Point(254, 196),
            Width = 85,
        };
        saveButton.Click += (_, _) =>
        {
            Result.Audio.ThresholdMs = (int)thresholdInput.Value;
            Result.Language = ((LanguageItem)languageCombo.SelectedItem!).Code;
        };
        var cancelButton = new Button
        {
            Text = Strings.SettingsCancel,
            DialogResult = DialogResult.Cancel,
            Location = new Point(345, 196),
            Width = 85,
        };

        Controls.AddRange([thresholdLabel, thresholdInput, helpLabel, languageLabel, languageCombo, saveButton, cancelButton]);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private sealed record LanguageItem(string Code, string Display);
}
