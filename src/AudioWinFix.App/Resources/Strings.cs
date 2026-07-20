using System.Globalization;
using System.Runtime.CompilerServices;

namespace AudioWinFix.App.Resources;

/// <summary>
/// Localized user-facing strings. The active language is picked from
/// CultureInfo.CurrentUICulture (auto-set from the OS UI language, or forced
/// from the Language setting at startup). Unsupported cultures fall back to English.
/// </summary>
internal static class Strings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AppName"] = "AudioWinFix",
        ["AppLanguageRestartMessage"] = "Language changed. AudioWinFix will restart to apply it.",

        // Tray
        ["TrayTooltipHeader"] = "AudioWinFix — pinned devices:",
        ["TrayPaused"] = "AudioWinFix — paused",
        ["MenuPause"] = "Pause",
        ["MenuResume"] = "Resume",
        ["MenuSettings"] = "Settings…",
        ["MenuStartWithWindows"] = "Start with Windows",
        ["MenuCheckUpdates"] = "Check for updates",
        ["MenuOpenLogFolder"] = "Open log folder",
        ["MenuQuit"] = "Quit",

        // Settings
        ["SettingsTitle"] = "AudioWinFix — Settings",
        ["SettingsThresholdLabel"] = "Auto-switch grace window (ms):",
        ["SettingsThresholdHelp"] =
            "A default-device change within this delay of plugging a device is treated as " +
            "automatic and reverted. Manual changes made later are kept.",
        ["SettingsLanguageLabel"] = "Language:",
        ["SettingsLanguageAuto"] = "Auto (system)",
        ["SettingsSave"] = "Save",
        ["SettingsCancel"] = "Cancel",

        // Updates
        ["UpdatesNotInstalledMessage"] =
            "Updates are only available when AudioWinFix is installed via Setup.exe (Velopack).\n\n" +
            "You appear to be running an unpacked or developer build.",
        ["UpdatesCheckingBalloon"] = "Checking for updates",
        ["UpdatesUpToDateBalloon"] = "You're up to date (v{0}).",
        ["UpdatesAvailablePrompt"] = "A new version is available: v{0}.\n\nDownload and restart now?",
        ["UpdatesAvailableTitle"] = "AudioWinFix update",
        ["UpdatesCheckFailed"] = "Update check failed: {0}",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AppName"] = "AudioWinFix",
        ["AppLanguageRestartMessage"] = "Langue modifiée. AudioWinFix va redémarrer pour l'appliquer.",

        // Barre d'état
        ["TrayTooltipHeader"] = "AudioWinFix — appareils épinglés :",
        ["TrayPaused"] = "AudioWinFix — en pause",
        ["MenuPause"] = "Pause",
        ["MenuResume"] = "Reprendre",
        ["MenuSettings"] = "Paramètres…",
        ["MenuStartWithWindows"] = "Démarrer avec Windows",
        ["MenuCheckUpdates"] = "Rechercher des mises à jour",
        ["MenuOpenLogFolder"] = "Ouvrir le dossier des journaux",
        ["MenuQuit"] = "Quitter",

        // Paramètres
        ["SettingsTitle"] = "AudioWinFix — Paramètres",
        ["SettingsThresholdLabel"] = "Fenêtre anti-bascule auto (ms) :",
        ["SettingsThresholdHelp"] =
            "Un changement d'appareil par défaut survenant dans ce délai après un branchement " +
            "est considéré comme automatique et annulé. Les changements manuels ultérieurs sont conservés.",
        ["SettingsLanguageLabel"] = "Langue :",
        ["SettingsLanguageAuto"] = "Auto (système)",
        ["SettingsSave"] = "Enregistrer",
        ["SettingsCancel"] = "Annuler",

        // Mises à jour
        ["UpdatesNotInstalledMessage"] =
            "Les mises à jour ne sont disponibles que lorsque AudioWinFix est installé via Setup.exe (Velopack).\n\n" +
            "Vous semblez exécuter une build non installée ou de développement.",
        ["UpdatesCheckingBalloon"] = "Recherche de mises à jour",
        ["UpdatesUpToDateBalloon"] = "Vous êtes à jour (v{0}).",
        ["UpdatesAvailablePrompt"] = "Une nouvelle version est disponible : v{0}.\n\nTélécharger et redémarrer maintenant ?",
        ["UpdatesAvailableTitle"] = "Mise à jour AudioWinFix",
        ["UpdatesCheckFailed"] = "La recherche de mises à jour a échoué : {0}",
    };

    private static IReadOnlyDictionary<string, string> Active =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "fr" => Fr,
            _ => En,
        };

    private static string Get([CallerMemberName] string key = "") =>
        Active.TryGetValue(key, out var value) ? value : key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture,
            Active.TryGetValue(key, out var v) ? v : key, args);

    public static string AppName => Get();
    public static string AppLanguageRestartMessage => Get();

    public static string TrayTooltipHeader => Get();
    public static string TrayPaused => Get();
    public static string MenuPause => Get();
    public static string MenuResume => Get();
    public static string MenuSettings => Get();
    public static string MenuStartWithWindows => Get();
    public static string MenuCheckUpdates => Get();
    public static string MenuOpenLogFolder => Get();
    public static string MenuQuit => Get();

    public static string SettingsTitle => Get();
    public static string SettingsThresholdLabel => Get();
    public static string SettingsThresholdHelp => Get();
    public static string SettingsLanguageLabel => Get();
    public static string SettingsLanguageAuto => Get();
    public static string SettingsSave => Get();
    public static string SettingsCancel => Get();

    public static string UpdatesNotInstalledMessage => Get();
    public static string UpdatesCheckingBalloon => Get();
    public static string UpdatesAvailableTitle => Get();
    public static string UpdatesUpToDateBalloon(string version) => Format(nameof(UpdatesUpToDateBalloon), version);
    public static string UpdatesAvailablePrompt(string version) => Format(nameof(UpdatesAvailablePrompt), version);
    public static string UpdatesCheckFailed(string message) => Format(nameof(UpdatesCheckFailed), message);
}
