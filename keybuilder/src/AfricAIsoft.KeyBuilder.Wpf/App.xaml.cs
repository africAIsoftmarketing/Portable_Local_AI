// ─────────────────────────────────────────────────────────────────────────────
// Rôle    : point d'entrée WPF, initialisation i18n (culture courante).
// Auteur  : AfricAIsoft — Licence : MIT — Date : 2026-08-24
// ─────────────────────────────────────────────────────────────────────────────
using System.Globalization;
using System.Threading;
using System.Windows;

namespace AfricAIsoft.KeyBuilder.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Force la culture française par défaut, sauf argument --lang en.
        var lang = "fr-FR";
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
                lang = arg["--lang=".Length..];
        }
        var ci = new CultureInfo(lang);
        Thread.CurrentThread.CurrentCulture = ci;
        Thread.CurrentThread.CurrentUICulture = ci;
        base.OnStartup(e);
    }
}
