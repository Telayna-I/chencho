using System;
using System.Globalization;
using Chencho.Core;

namespace Chencho.Overlay;

/// The language the overlay speaks: the one picked in the notification-area menu, or Windows' display language.
internal static class Locale
{
    public static Language Current { get; private set; } = Resolve();

    /// After the language changes; menus are built when they open, so only the strip and the icon listen.
    public static event Action? Changed;

    /// "auto", "en" or "es", as kept in overlay.json.
    public static string Preference
    {
        get => Preferences.Language;
        set
        {
            Preferences.Language = value;
            Current = Resolve();
            Changed?.Invoke();
        }
    }

    public static string T(string english, string spanish) => Current.Text(english, spanish);

    private static Language Resolve() =>
        Localization.Resolve(Preferences.Language, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
}
