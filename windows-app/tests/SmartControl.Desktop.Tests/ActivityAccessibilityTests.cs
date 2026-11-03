using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;
using SmartControl.Desktop.Ui;

namespace SmartControl.Desktop.Tests;

/// <summary>Lo que oye un lector de pantalla en cada fila de la actividad.</summary>
internal static class ActivityAccessibilityTests
{
    private static readonly DateTimeOffset Received = new(2026, 11, 3, 14, 32, 5, TimeSpan.Zero);

    public static IEnumerable<(string, Action)> All()
    {
        yield return ("actividad: una acción hecha se lee con resultado, modo y hora", AcceptedEntryReadsAsSentence);
        yield return ("actividad: un bloqueo se lee con su motivo", RejectedEntryIncludesReason);
        yield return ("actividad: ningún motivo de bloqueo se lee como datos internos", EveryRejectionReadsAsText);
        yield return ("actividad: la lista expone ese texto como nombre de cada fila", ListItemsExposeEntryText);
    }

    private static ActivityEntry Entry(ValidationResult result) =>
        ActivityEntry.From(new CommandProcessedEventArgs(result, null, Received));

    private static string LocalTime => Received.ToLocalTime().ToString("HH:mm:ss");

    private static void AcceptedEntryReadsAsSentence()
    {
        var entry = Entry(new ValidationResult(CommandStatus.Accepted, ReasonCodes.Ok, AllowedAction.SlideNext, ControlMode.Presentation));
        Check.Equal(
            $"Hecho: Siguiente diapositiva. Presentación, simulado, no se controló ninguna aplicación. A las {LocalTime}.",
            entry.ToString());
    }

    private static void RejectedEntryIncludesReason()
    {
        var entry = Entry(ValidationResult.Reject(CommandStatus.RejectedLowConfidence, ReasonCodes.BelowThreshold));
        Check.Equal(
            $"Bloqueado: Gesto poco claro. El movimiento no se reconoció con seguridad. No se hizo nada. A las {LocalTime}.",
            entry.ToString());
    }

    private static void EveryRejectionReadsAsText()
    {
        var codes = typeof(ReasonCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);
        foreach (var status in Enum.GetValues<CommandStatus>().Where(s => s != CommandStatus.Accepted))
        foreach (var code in codes)
        {
            var entry = Entry(ValidationResult.Reject(status, code));
            var text = entry.ToString();
            Check.True(text.StartsWith("Bloqueado: " + entry.Title, StringComparison.Ordinal), $"{status}/{code}: «{text}»");
            Check.True(text.Contains(entry.Detail, StringComparison.Ordinal), $"{status}/{code}: falta el motivo en «{text}»");
            Check.True(!text.Any(c => c >= '' && c <= ''), $"{status}/{code}: contiene el carácter del icono");
            Check.True(!text.Contains(nameof(ActivityEntry), StringComparison.Ordinal) && !text.Contains(code, StringComparison.Ordinal),
                $"{status}/{code}: deja ver datos internos en «{text}»");
        }
    }

    private static void ListItemsExposeEntryText()
    {
        using var list = new ActivityList();
        list.CreateControl();
        var older = Entry(ValidationResult.Reject(CommandStatus.RejectedNotAllowed, ReasonCodes.ActionNotInAllowlist));
        var newer = Entry(new ValidationResult(CommandStatus.Accepted, ReasonCodes.Ok, AllowedAction.MediaPause, ControlMode.Multimedia));
        list.Add(older);
        list.Add(newer);

        var names = Enumerable.Range(0, list.AccessibilityObject.GetChildCount())
            .Select(i => list.AccessibilityObject.GetChild(i))
            .Where(child => child?.Role == AccessibleRole.ListItem)
            .Select(child => child!.Name)
            .ToList();
        Check.Equal(2, names.Count, "filas expuestas");
        Check.Equal(newer.ToString(), names[0], "la más reciente primero");
        Check.Equal(older.ToString(), names[1]);
    }
}
