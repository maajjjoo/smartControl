using System.Drawing;
using System.Windows.Forms;

namespace SmartControl.Desktop.Ui;

/// <summary>
/// Menús emergentes con los colores de <see cref="Palette"/>, para que en modo oscuro no aparezca un
/// menú blanco. Con contraste alto se usa el dibujo del sistema, que ya respeta sus colores.
/// </summary>
internal static class Menus
{
    public static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip { Font = Typography.Body, ShowImageMargin = false, ShowCheckMargin = true };
        if (Palette.HighContrast)
        {
            menu.RenderMode = ToolStripRenderMode.System;
            return menu;
        }
        menu.Renderer = new PaletteRenderer();
        menu.BackColor = Palette.Surface;
        menu.ForeColor = Palette.Text;
        return menu;
    }

    /// <summary>Título de una sección del menú (no se puede pulsar).</summary>
    public static ToolStripLabel Header(string text) => new(text)
    {
        Font = Typography.Label,
        Padding = new Padding(4, 6, 0, 2),
        ForeColor = Palette.HighContrast ? SystemColors.GrayText : Palette.TextMuted,
    };

    private sealed class PaletteRenderer : ToolStripProfessionalRenderer
    {
        public PaletteRenderer() : base(new PaletteColors()) => RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is not ToolStripLabel)
                e.TextColor = e.Item.Enabled ? Palette.Text : Palette.Idle;
            base.OnRenderItemText(e);
        }

        /// <summary>La marca de la opción elegida, con el icono de Windows en el color principal.</summary>
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using var font = Icons.Create(9f);
            TextRenderer.DrawText(e.Graphics, Icons.Check, font, e.ImageRectangle, Palette.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private sealed class PaletteColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Palette.Surface;
        public override Color MenuBorder => Palette.OutlineStrong;
        public override Color MenuItemBorder => Palette.AccentSoft;
        public override Color MenuItemSelected => Palette.AccentSoft;
        public override Color ImageMarginGradientBegin => Palette.Surface;
        public override Color ImageMarginGradientMiddle => Palette.Surface;
        public override Color ImageMarginGradientEnd => Palette.Surface;
        public override Color CheckBackground => Palette.Surface;
        public override Color CheckSelectedBackground => Palette.AccentSoft;
        public override Color CheckPressedBackground => Palette.AccentSoft;
        public override Color SeparatorDark => Palette.Outline;
        public override Color SeparatorLight => Palette.Outline;
    }
}
