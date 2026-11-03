using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;
using SmartControl.Desktop.Ui;

namespace SmartControl.Desktop.Tests;

/// <summary>
/// Contraste alto de Windows. Las pruebas activan el modo solo dentro de <see cref="Palette"/>
/// (sin tocar la configuración del equipo) y comparan con los colores de sistema actuales.
/// </summary>
internal static class HighContrastTests
{
    public static IEnumerable<(string, Action)> All()
    {
        yield return ("contraste alto: la paleta usa los colores del sistema", PaletteUsesSystemColors);
        yield return ("contraste alto: la ventana cambia de colores y vuelve sin reiniciarse", WindowSwitchesBothWays);
        yield return ("contraste alto: los botones se pintan con colores de botón y de resaltado", ButtonsPaintWithSystemColors);
        yield return ("contraste alto: la fila seleccionada usa el resaltado del sistema", SelectedRowUsesHighlight);
    }

    private static void WithHighContrast(bool enabled, Action body)
    {
        Palette.HighContrast = enabled;
        try { body(); }
        finally { Palette.Refresh(); }
    }

    private static void Same(Color expected, Color actual, string context) => Check.Equal(expected.ToArgb(), actual.ToArgb(), context);

    private static void PaletteUsesSystemColors()
    {
        WithHighContrast(true, () =>
        {
            Same(SystemColors.Window, Palette.Background, "fondo");
            Same(SystemColors.Window, Palette.Surface, "tarjetas");
            Same(SystemColors.WindowText, Palette.Text, "texto");
            Same(SystemColors.WindowText, Palette.TextMuted, "texto secundario");
            Same(SystemColors.WindowText, Palette.Outline, "bordes");
            Same(SystemColors.Highlight, Palette.AccentSoft, "seleccionado");
            Same(SystemColors.HighlightText, Palette.AccentOnSoft, "texto seleccionado");
            Same(SystemColors.WindowText, Palette.DangerOnSoft, "texto de bloqueo");
            Same(SystemColors.GrayText, Palette.Idle, "en pausa / deshabilitado");
        });
        WithHighContrast(false, () =>
        {
            Same(Color.FromArgb(0xF2, 0xF5, 0xFB), Palette.Background, "fondo normal");
            Same(Color.FromArgb(0x5E, 0x68, 0x7A), Palette.TextMuted, "texto secundario normal");
        });
    }

    private static void WindowSwitchesBothWays()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartcontrol-desktop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            WithHighContrast(false, () =>
            {
                var log = new FileEventLog(directory);
                var validator = new CommandValidator(new Dictionary<string, string> { ["d"] = new string('A', 64) });
                var receiver = new LocalCommandReceiver(validator, new SimulatedCommandExecutor(), log, port: 0);
                using var form = new MainForm(receiver, log, "d", new string('A', 64));
                var subtitle = Descendants(form).OfType<Label>().Single(l => l.Text.StartsWith("Controla tus presentaciones", StringComparison.Ordinal));
                Same(Color.FromArgb(0x5E, 0x68, 0x7A), subtitle.ForeColor, "antes: texto secundario de la paleta");

                Palette.HighContrast = true;
                form.ApplyTheme();
                Same(SystemColors.Window, form.BackColor, "fondo de la ventana");
                foreach (var label in Descendants(form).OfType<Label>())
                    Same(SystemColors.WindowText, label.ForeColor, $"texto «{label.Text}»");
                foreach (var container in Descendants(form).Where(c => c is Panel && c is not Card))
                {
                    var back = container.BackColor.ToArgb();
                    Check.True(back == SystemColors.Window.ToArgb() || (container.Height == 1 && back == SystemColors.WindowText.ToArgb()),
                        $"{container.GetType().Name} con fondo {container.BackColor} fuera de los colores del sistema");
                }

                Palette.HighContrast = false;
                form.ApplyTheme();
                Same(Color.FromArgb(0xF2, 0xF5, 0xFB), form.BackColor, "después: fondo normal");
                Same(Color.FromArgb(0x5E, 0x68, 0x7A), subtitle.ForeColor, "después: texto secundario normal");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ButtonsPaintWithSystemColors()
    {
        static Color Center(SmartButton button)
        {
            using var bitmap = new Bitmap(button.Width, button.Height);
            button.DrawToBitmap(bitmap, new Rectangle(Point.Empty, button.Size));
            return bitmap.GetPixel(button.Width / 2, button.Height / 2);
        }

        using var host = new Panel { Size = new Size(200, 60) };
        using var button = new SmartButton { Text = "", Kind = ButtonKind.Primary, Size = new Size(120, 40) };
        host.Controls.Add(button);

        WithHighContrast(false, () => Same(Color.FromArgb(0x25, 0x63, 0xEB), Center(button), "principal sin contraste alto"));
        WithHighContrast(true, () =>
        {
            Same(SystemColors.ButtonFace, Center(button), "normal");
            button.Selected = true;
            Same(SystemColors.Highlight, Center(button), "seleccionado");
            button.Selected = false;
            button.Enabled = false;
            Same(SystemColors.Window, Center(button), "deshabilitado");
            Same(SystemColors.GrayText, button.Colors().Fore, "texto deshabilitado");
        });
    }

    private static void SelectedRowUsesHighlight()
    {
        using var list = new ActivityList { Size = new Size(400, 200) };
        list.CreateControl();
        list.Add(ActivityEntry.From(new CommandProcessedEventArgs(
            new ValidationResult(CommandStatus.Accepted, ReasonCodes.Ok, AllowedAction.MediaPlay, ControlMode.Multimedia), null, DateTimeOffset.Now)));

        WithHighContrast(true, () =>
        {
            ThemeColors.Reapply(list);
            Same(SystemColors.Window, list.BackColor, "fondo de la lista");
            list.SelectedIndex = 0;
            using var bitmap = new Bitmap(list.Width, list.Height);
            list.DrawToBitmap(bitmap, new Rectangle(Point.Empty, list.Size));
            var row = list.GetItemRectangle(0);
            Same(SystemColors.Highlight, bitmap.GetPixel(row.Left + 4, row.Top + 4), "fondo de la fila seleccionada");
        });
    }

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
}
