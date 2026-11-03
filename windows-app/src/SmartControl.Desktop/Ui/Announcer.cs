using System;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace SmartControl.Desktop.Ui;

/// <summary>
/// Avisos para los lectores de pantalla (Narrador, NVDA, JAWS) mediante notificaciones de
/// UI Automation. Son para lo que cambia sin que el usuario mueva el foco: el estado del
/// receptor y la respuesta a un comando de prueba.
/// </summary>
internal static class Announcer
{
    /// <summary>Cada aviso enviado, con el control que lo envía. Lo usan las pruebas.</summary>
    internal static event Action<Control, string>? Announced;

    /// <summary>
    /// Envía <paramref name="message"/> desde <paramref name="source"/>. Si llegan varios
    /// seguidos, el lector lee el último en lugar de acumularlos.
    /// </summary>
    public static void Announce(Control source, string message, AutomationNotificationKind kind)
    {
        if (source.IsDisposed || !source.IsHandleCreated || string.IsNullOrWhiteSpace(message))
            return;
        source.AccessibilityObject.RaiseAutomationNotification(kind, AutomationNotificationProcessing.MostRecent, message);
        Announced?.Invoke(source, message);
    }

    /// <summary>Termina el texto en punto para que el lector haga una pausa entre frases.</summary>
    public static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";
}

/// <summary>
/// Decide cuándo avisar del estado del receptor. El receptor también avisa cuando se abre o
/// se cierra una conexión, y eso no cambia lo que dice la tarjeta: solo se avisa si cambia.
/// </summary>
internal sealed class StatusAnnouncement
{
    private string? _last;

    /// <summary>Texto que hay que leer, o nulo si es el mismo que ya se leyó.</summary>
    public string? Next(string title, string detail)
    {
        var text = $"{Announcer.Sentence(title)} {Announcer.Sentence(detail)}";
        if (text == _last)
            return null;
        _last = text;
        return text;
    }
}
