namespace QuietIsland.Core;

public sealed record WindowsSettingsPage(string Name, string Target, string EnglishName, string Keywords = "");

/// <summary>Common Windows 10/11 pages from the documented ms-settings URI scheme.</summary>
public static class WindowsSettingsCatalog
{
    // Keep these pages usable on both supported Windows versions. Hardware-specific
    // subpages and legacy pages removed in Windows 11 are intentionally omitted.
    private static readonly WindowsSettingsPage[] Pages =
    [
        new("Параметры Windows", "ms-settings:", "Windows Settings", "настройки параметры"),
        new("Экран", "ms-settings:display", "Display", "экран монитор разрешение масштаб яркость monitor resolution scale brightness"),
        new("Звук", "ms-settings:sound", "Sound", "звук громкость аудио динамики микрофон volume audio speakers microphone"),
        new("Bluetooth и устройства", "ms-settings:bluetooth", "Bluetooth and devices", "блютуз устройства devices"),
        new("Wi-Fi", "ms-settings:network-wifi", "Wi-Fi", "вайфай беспроводная сеть wifi wireless network"),
        new("Сеть и Интернет", "ms-settings:network-status", "Network and Internet", "сеть интернет подключение ethernet connection"),
        new("VPN", "ms-settings:network-vpn", "VPN", "впн виртуальная сеть virtual private network"),
        new("Прокси", "ms-settings:network-proxy", "Proxy", "прокси сервер proxy server"),
        new("Уведомления", "ms-settings:notifications", "Notifications", "уведомления оповещения alerts"),
        new("Питание и спящий режим", "ms-settings:powersleep", "Power and sleep", "питание сон батарея аккумулятор battery energy"),
        new("Память устройства", "ms-settings:storagesense", "Storage", "память диск место хранилище disk space drive"),
        new("Установленные приложения", "ms-settings:appsfeatures", "Installed apps", "приложения программы удаление apps features uninstall programs"),
        new("Приложения по умолчанию", "ms-settings:defaultapps", "Default apps", "приложения браузер по умолчанию browser associations"),
        new("Автозагрузка", "ms-settings:startupapps", "Startup apps", "автозапуск запуск startup autorun"),
        new("Фон рабочего стола", "ms-settings:personalization-background", "Background", "рабочий стол фон обои desktop wallpaper"),
        new("Цвета и тема оформления", "ms-settings:colors", "Colors", "цвет тема светлая тёмная темная dark light theme accent"),
        new("Панель задач", "ms-settings:taskbar", "Taskbar", "панель задач значки icons"),
        new("Учётная запись", "ms-settings:yourinfo", "Your info", "учетная учётная запись аккаунт account profile"),
        new("Варианты входа", "ms-settings:signinoptions", "Sign-in options", "пароль вход пин password pin hello login"),
        new("Дата и время", "ms-settings:dateandtime", "Date and time", "дата время часы часовой пояс clock timezone"),
        new("Язык и регион", "ms-settings:regionlanguage", "Language and region", "язык регион раскладка keyboard layout"),
        new("Разрешения микрофона", "ms-settings:privacy-microphone", "Microphone privacy", "разрешение доступ приватность конфиденциальность permission access privacy"),
        new("Разрешения камеры", "ms-settings:privacy-webcam", "Camera privacy", "камера вебкамера разрешение доступ webcam permission access privacy"),
        new("Буфер обмена", "ms-settings:clipboard", "Clipboard", "буфер обмена история копирование history copy paste"),
        new("Центр специальных возможностей", "ms-settings:easeofaccess", "Accessibility", "доступность специальные возможности ease of access"),
        new("Восстановление", "ms-settings:recovery", "Recovery", "восстановление сброс reset restore"),
        new("Безопасность Windows", "ms-settings:windowsdefender", "Windows Security", "защита антивирус defender antivirus firewall брандмауэр"),
        new("Обновление Windows", "ms-settings:windowsupdate", "Windows Update", "обновления обновление центр update updates"),
        new("О системе", "ms-settings:about", "About", "система версия характеристики system version information"),
    ];

    public static IReadOnlyList<WindowsSettingsPage> Search(string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return Pages;
        return Pages.Select(page => (Page: page, Score: Score(page, query.Trim(), words)))
            .Where(match => match.Score > 0).OrderByDescending(match => match.Score)
            .ThenBy(match => match.Page.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(match => match.Page).ToArray();
    }

    private static int Score(WindowsSettingsPage page, string query, string[] words)
    {
        var terms = page.Name + " " + page.EnglishName + " " + page.Keywords + " " + page.Target["ms-settings:".Length..];
        if (!words.All(word => terms.Contains(word, StringComparison.OrdinalIgnoreCase))) return 0;
        return Math.Max(ApplicationMatcher.Score(page.Name, query), ApplicationMatcher.Score(page.EnglishName, query)) + 1;
    }
}
