using System.Globalization;

namespace DeviceCheck.Agent.UI;

/// <summary>Tray UI texts, RU/EN by the Windows UI language (same rule as the site).</summary>
internal static class Strings
{
    private static readonly bool Ru = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk";

    private static string L(string ru, string en) => Ru ? ru : en;

    public static string Open => L("Открыть Device Check", "Open Device Check");
    public static string Rescan => L("Сканировать заново", "Scan again");
    public static string SaveJson => L("Сохранить отчёт JSON…", "Save JSON report…");
    public static string CopyJson => L("Копировать JSON", "Copy JSON");
    public static string Exit => L("Выход", "Exit");
    public static string Scanning => L("Сканирование…", "Scanning…");
    public static string ScanningStep(int done, int total) => L($"Сканирование… {done}/{total}", $"Scanning… {done}/{total}");
    public static string NoReport => L("Отчёта пока нет", "No report yet");
    public static string ReportAt(DateTimeOffset t, int ok, int total) =>
        L($"Отчёт {t.ToLocalTime():HH:mm} · категорий {ok}/{total}", $"Report {t.ToLocalTime():HH:mm} · {ok}/{total} categories");
    public static string ScanFailed => L("Сканирование не удалось", "Scan failed");
    public static string Copied => L("JSON скопирован в буфер обмена", "JSON copied to the clipboard");
    public static string Saved(string path) => L($"Сохранено: {path}", $"Saved: {path}");
    public static string RunningInTray => L("Агент работает в области уведомлений. Меню — правой кнопкой по значку.",
        "The agent is running in the notification area. Right-click the icon for the menu.");
    public static string ApiFailed(string msg) => L($"Локальный API не запущен: {msg}. Сайт не сможет получить отчёт.",
        $"Local API did not start: {msg}. The site cannot fetch the report.");
    public static string SaveFilter => L("Отчёт Device Check (*.json)|*.json", "Device Check report (*.json)|*.json");
    public static string SaveFailed(string msg) => L($"Не удалось сохранить: {msg}", $"Could not save: {msg}");
}
