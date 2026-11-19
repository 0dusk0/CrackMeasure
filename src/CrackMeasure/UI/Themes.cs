using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CrackMeasure
{
    internal enum ButtonIconPlacement
    {
        Left,
        Top
    }

    internal static class UiTheme
    {
        // Precision Metrology Dark Studio Surfaces
        public static readonly Color AppBg = Color.FromArgb(10, 15, 26);              // #0A0F1A
        public static readonly Color Surface = Color.FromArgb(17, 24, 39);            // #111827
        public static readonly Color SurfaceHeader = Color.FromArgb(20, 29, 46);      // #141D2E
        public static readonly Color SurfaceSubtle = Color.FromArgb(23, 33, 51);      // #172133
        public static readonly Color SurfaceHover = Color.FromArgb(30, 43, 66);       // #1E2B42
        public static readonly Color SurfacePressed = Color.FromArgb(38, 55, 84);     // #263754
        public static readonly Color Border = Color.FromArgb(35, 48, 70);             // #233046
        public static readonly Color BorderHover = Color.FromArgb(59, 79, 112);       // #3B4F70
        public static readonly Color InputBorder = Color.FromArgb(55, 75, 107);       // #374B6B (>=3:1 on Surface)

        // Typography Tokens (>= 4.5:1 WCAG AA on Surface & SurfaceSubtle)
        public static readonly Color TextPrimary = Color.FromArgb(241, 245, 249);     // #F1F5F9
        public static readonly Color TextSecondary = Color.FromArgb(203, 213, 225);   // #CBD5E1
        public static readonly Color TextMuted = Color.FromArgb(142, 160, 184);       // #8EA0B8 (~4.8:1 on #172133)

        // Semantic Accents
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);           // Cobalt-600
        public static readonly Color PrimaryHover = Color.FromArgb(59, 130, 246);     // Cobalt-500
        public static readonly Color PrimaryPressed = Color.FromArgb(29, 78, 216);    // Cobalt-700
        public static readonly Color PrimarySoftBg = Color.FromArgb(23, 42, 74);
        public static readonly Color PrimarySoftBorder = Color.FromArgb(46, 88, 148);
        public static readonly Color PrimarySoftText = Color.FromArgb(147, 197, 253); // Blue-300

        public static readonly Color Success = Color.FromArgb(16, 185, 129);          // Emerald-500
        public static readonly Color SuccessFill = Color.FromArgb(5, 150, 105);       // Emerald-600
        public static readonly Color SuccessFillHover = Color.FromArgb(16, 185, 129); // Emerald-500
        public static readonly Color SuccessText = Color.FromArgb(52, 211, 153);      // Emerald-400
        public static readonly Color SuccessSoftBg = Color.FromArgb(15, 45, 39);
        public static readonly Color SuccessSoftBorder = Color.FromArgb(30, 96, 80);

        public static readonly Color Warning = Color.FromArgb(245, 158, 11);          // Amber-500
        public static readonly Color WarningText = Color.FromArgb(251, 191, 36);      // Amber-400
        public static readonly Color WarningSoftBg = Color.FromArgb(46, 34, 13);
        public static readonly Color WarningSoftBorder = Color.FromArgb(110, 78, 22);

        public static readonly Color Danger = Color.FromArgb(220, 38, 38);            // Red-600
        public static readonly Color DangerText = Color.FromArgb(251, 113, 133);      // Rose-400
        public static readonly Color DangerHover = Color.FromArgb(239, 68, 68);       // Red-500
        public static readonly Color DangerSoftBg = Color.FromArgb(51, 22, 30);
        public static readonly Color DangerSoftBorder = Color.FromArgb(122, 40, 58);

        public static readonly Color SkyFill = Color.FromArgb(14, 165, 233);          // Sky-500
        public static readonly Color Teal = Color.FromArgb(45, 212, 191);             // Teal-400
        public static readonly Color TealFill = Color.FromArgb(15, 118, 110);         // Teal-700
        public static readonly Color Cyan = Color.FromArgb(34, 211, 238);             // Cyan-400
        public static readonly Color Violet = Color.FromArgb(167, 139, 250);          // Violet-400

        // Optical Canvas Tokens
        public static readonly Color CanvasBg = Color.FromArgb(7, 11, 19);            // #070B13
        public static readonly Color CanvasGrid = Color.FromArgb(15, 23, 38);         // Subtle 32px technical grid
        public static readonly Color CanvasBarBg = Color.FromArgb(15, 22, 36);        // #0F1624
        public static readonly Color CanvasBarAlt = Color.FromArgb(13, 19, 32);       // #0D1320
        public static readonly Color CanvasPillGroup = Color.FromArgb(9, 14, 24);
        public static readonly Color CanvasBtnBg = Color.FromArgb(26, 37, 56);
        public static readonly Color CanvasBtnHover = Color.FromArgb(36, 51, 76);
        public static readonly Color CanvasBtnBorder = Color.FromArgb(44, 62, 92);
        public static readonly Color CanvasText = Color.FromArgb(241, 245, 249);
        public static readonly Color CanvasMuted = Color.FromArgb(142, 160, 184);

        // Unified Corner Radius Tokens (Crisp Industrial Precision Progression)
        public const int RadiusSmall = 4;
        public const int RadiusControl = 5;
        public const int RadiusSubCard = 6;
        public const int RadiusCard = 8;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void EnableDarkTitleBar(Form form)
        {
            if (form.IsDisposed) return;
            void Apply()
            {
                if (!form.IsHandleCreated) return;
                try
                {
                    int useDark = 1;
                    if (DwmSetWindowAttribute(form.Handle, 20, ref useDark, sizeof(int)) != 0)
                        DwmSetWindowAttribute(form.Handle, 19, ref useDark, sizeof(int));
                }
                catch { }
            }
            if (form.IsHandleCreated) Apply();
            else form.HandleCreated += (_, _) => Apply();
        }

        public static int ScaleRadius(int radius, SizeF factor, int min = 3, int max = 18) =>
            Math.Clamp((int)Math.Round(radius * Math.Min(factor.Width, factor.Height)), min, max);

        public static float ScaleRadius(float radius, SizeF factor, float min = 3f, float max = 18f) =>
            Math.Clamp(radius * Math.Min(factor.Width, factor.Height), min, max);

        public static GraphicsPath CreateRoundedRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0.01f || bounds.Height <= 0.01f) return path;
            float r = Math.Max(0f, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) * 0.5f));
            if (r <= 0.1f) { path.AddRectangle(bounds); return path; }
            float d = r * 2f;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath CreateTopRoundedRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0.01f || bounds.Height <= 0.01f) return path;
            float r = Math.Max(0f, Math.Min(radius, Math.Min(bounds.Width * 0.5f, bounds.Height)));
            if (r <= 0.1f) { path.AddRectangle(bounds); return path; }
            float d = r * 2f;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddLine(bounds.Right, bounds.Bottom, bounds.X, bounds.Bottom);
            path.CloseFigure();
            return path;
        }

        public static Color ResolveOpaqueParentBack(Control control)
        {
            Control? p = control.Parent;
            while (p != null && p.BackColor.A < 255) p = p.Parent;
            return p?.BackColor ?? Surface;
        }

        public static Color Blend(Color c1, Color c2, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(
                (int)Math.Round(c1.A + (c2.A - c1.A) * amount),
                (int)Math.Round(c1.R + (c2.R - c1.R) * amount),
                (int)Math.Round(c1.G + (c2.G - c1.G) * amount),
                (int)Math.Round(c1.B + (c2.B - c1.B) * amount));
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = UiTheme.RadiusControl;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color NormalBackColor { get; set; } = UiTheme.SurfaceSubtle;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverBackColor { get; set; } = UiTheme.SurfaceHover;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color PressedBackColor { get; set; } = UiTheme.SurfacePressed;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color NormalForeColor { get; set; } = UiTheme.TextPrimary;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color NormalBorderColor { get; set; } = UiTheme.Border;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverBorderColor { get; set; } = UiTheme.BorderHover;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color DisabledBackColor { get; set; } = Color.FromArgb(19, 27, 43);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color DisabledForeColor { get; set; } = Color.FromArgb(84, 98, 122);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color DisabledBorderColor { get; set; } = Color.FromArgb(30, 41, 61);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsDarkSurface { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IndustrialIconKind IconKind { get; set; } = IndustrialIconKind.None;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ButtonIconPlacement IconPlacement { get; set; } = ButtonIconPlacement.Left;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int IconSize { get; set; } = 14;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int IconGap { get; set; } = 5;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color IconColor { get; set; } = Color.Empty;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color LeftAccentColor { get; set; } = Color.Empty;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void ApplyLightSecondary() => ApplyStudioSecondary();

        public void ApplyStudioSecondary()
        {
            IsDarkSurface = true;
            LeftAccentColor = Color.Empty;
            NormalBackColor = UiTheme.SurfaceSubtle;
            HoverBackColor = UiTheme.SurfaceHover;
            PressedBackColor = UiTheme.SurfacePressed;
            NormalForeColor = UiTheme.TextPrimary;
            NormalBorderColor = UiTheme.Border;
            HoverBorderColor = UiTheme.BorderHover;
            Invalidate();
        }

        public void ApplyLightTinted(Color bg, Color fg, Color border) => ApplyStudioTinted(bg, fg, border);

        public void ApplyStudioTinted(Color bg, Color fg, Color border)
        {
            IsDarkSurface = true;
            LeftAccentColor = Color.Empty;
            NormalBackColor = bg;
            HoverBackColor = UiTheme.Blend(bg, fg, 0.14f);
            PressedBackColor = UiTheme.Blend(bg, fg, 0.24f);
            NormalForeColor = fg;
            NormalBorderColor = border;
            HoverBorderColor = UiTheme.Blend(border, fg, 0.35f);
            Invalidate();
        }

        public void ApplySolidAccent(Color bg, Color hoverBg, Color fg, bool darkSurface = true)
        {
            IsDarkSurface = darkSurface;
            LeftAccentColor = Color.Empty;
            NormalBackColor = bg;
            HoverBackColor = hoverBg;
            PressedBackColor = UiTheme.Blend(bg, Color.Black, 0.16f);
            NormalForeColor = fg;
            NormalBorderColor = UiTheme.Blend(bg, Color.White, 0.16f);
            HoverBorderColor = UiTheme.Blend(hoverBg, Color.White, 0.28f);
            Invalidate();
        }

        public void ApplyDarkTone(bool active, Color? activeBg = null, Color? activeFg = null)
        {
            IsDarkSurface = true;
            LeftAccentColor = Color.Empty;
            DisabledBackColor = Color.FromArgb(18, 25, 40);
            DisabledForeColor = Color.FromArgb(84, 98, 122);
            DisabledBorderColor = Color.FromArgb(30, 41, 61);

            if (active)
            {
                Color bg = activeBg ?? UiTheme.Primary;
                NormalBackColor = bg;
                HoverBackColor = UiTheme.Blend(bg, Color.White, 0.10f);
                PressedBackColor = UiTheme.Blend(bg, Color.Black, 0.14f);
                NormalForeColor = activeFg ?? Color.White;
                NormalBorderColor = UiTheme.Blend(bg, Color.White, 0.24f);
                HoverBorderColor = UiTheme.Blend(bg, Color.White, 0.40f);
            }
            else
            {
                NormalBackColor = UiTheme.CanvasBtnBg;
                HoverBackColor = UiTheme.CanvasBtnHover;
                PressedBackColor = UiTheme.SurfacePressed;
                NormalForeColor = UiTheme.CanvasText;
                NormalBorderColor = UiTheme.CanvasBtnBorder;
                HoverBorderColor = UiTheme.BorderHover;
            }
            Invalidate();
        }

        public void ApplyRailTool(bool active, Color accentColor)
        {
            IsDarkSurface = true;
            DisabledBackColor = UiTheme.CanvasBarAlt;
            DisabledForeColor = Color.FromArgb(75, 88, 112);
            DisabledBorderColor = Color.Transparent;

            if (active)
            {
                LeftAccentColor = accentColor;
                IconColor = UiTheme.Blend(accentColor, Color.White, 0.45f);
                NormalBackColor = UiTheme.Blend(UiTheme.CanvasBarAlt, accentColor, 0.22f);
                HoverBackColor = UiTheme.Blend(UiTheme.CanvasBarAlt, accentColor, 0.28f);
                PressedBackColor = UiTheme.Blend(UiTheme.CanvasBarAlt, accentColor, 0.34f);
                NormalForeColor = Color.White;
                NormalBorderColor = UiTheme.Blend(UiTheme.CanvasBtnBorder, accentColor, 0.55f);
                HoverBorderColor = UiTheme.Blend(UiTheme.CanvasBtnBorder, accentColor, 0.72f);
            }
            else
            {
                LeftAccentColor = Color.Empty;
                IconColor = Color.Empty;
                NormalBackColor = UiTheme.CanvasBarAlt;
                HoverBackColor = UiTheme.CanvasBtnBg;
                PressedBackColor = UiTheme.CanvasBtnHover;
                NormalForeColor = UiTheme.CanvasMuted;
                NormalBorderColor = UiTheme.CanvasBarAlt;
                HoverBorderColor = UiTheme.CanvasBtnBorder;
            }
            Invalidate();
        }

        public void ApplySegmentedTone(bool active)
        {
            IsDarkSurface = true;
            LeftAccentColor = Color.Empty;
            DisabledBackColor = UiTheme.CanvasPillGroup;
            DisabledForeColor = Color.FromArgb(84, 98, 122);
            DisabledBorderColor = UiTheme.CanvasPillGroup;

            if (active)
            {
                NormalBackColor = UiTheme.Primary;
                HoverBackColor = UiTheme.PrimaryHover;
                PressedBackColor = UiTheme.PrimaryPressed;
                NormalForeColor = Color.White;
                NormalBorderColor = UiTheme.Blend(UiTheme.Primary, Color.White, 0.22f);
                HoverBorderColor = UiTheme.Blend(UiTheme.Primary, Color.White, 0.36f);
            }
            else
            {
                NormalBackColor = UiTheme.CanvasPillGroup;
                HoverBackColor = UiTheme.CanvasBtnBg;
                PressedBackColor = UiTheme.CanvasBtnHover;
                NormalForeColor = UiTheme.CanvasMuted;
                NormalBorderColor = UiTheme.CanvasPillGroup;
                HoverBorderColor = UiTheme.CanvasBtnBorder;
            }
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            string txt = Text ?? "";
            Size txtSz = string.IsNullOrEmpty(txt)
                ? Size.Empty
                : TextRenderer.MeasureText(txt, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            if (IconKind == IndustrialIconKind.None)
            {
                int w0 = Math.Max(MinimumSize.Width, txtSz.Width + Padding.Horizontal + 6);
                int h0 = Math.Max(MinimumSize.Height, txtSz.Height + Padding.Vertical + 6);
                return new Size(w0, h0);
            }

            if (IconPlacement == ButtonIconPlacement.Top)
            {
                int contentW = Math.Max(IconSize, txtSz.Width);
                int contentH = IconSize + (string.IsNullOrEmpty(txt) ? 0 : IconGap + txtSz.Height);
                return new Size(Math.Max(MinimumSize.Width, contentW + Padding.Horizontal + 4), Math.Max(MinimumSize.Height, contentH + Padding.Vertical + 4));
            }
            else
            {
                int contentW = IconSize + (string.IsNullOrEmpty(txt) ? 0 : IconGap + txtSz.Width);
                int contentH = Math.Max(IconSize, txtSz.Height);
                return new Size(Math.Max(MinimumSize.Width, contentW + Padding.Horizontal + 6), Math.Max(MinimumSize.Height, contentH + Padding.Vertical + 6));
            }
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            CornerRadius = UiTheme.ScaleRadius(CornerRadius, factor);
            float s = Math.Min(factor.Width, factor.Height);
            IconSize = Math.Clamp((int)Math.Round(IconSize * s), 12, 28);
            IconGap = Math.Clamp((int)Math.Round(IconGap * s), 3, 12);
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { _hovered = false; _pressed = false; } Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = UiTheme.ResolveOpaqueParentBack(this);
            g.Clear(parentBg);

            Color bg, fg, bd;
            if (!Enabled)
            {
                bg = DisabledBackColor;
                fg = DisabledForeColor;
                bd = DisabledBorderColor;
            }
            else if (_pressed)
            {
                bg = PressedBackColor;
                fg = NormalForeColor == UiTheme.CanvasMuted ? UiTheme.CanvasText : NormalForeColor;
                bd = HoverBorderColor;
            }
            else if (_hovered)
            {
                bg = HoverBackColor;
                fg = NormalForeColor == UiTheme.CanvasMuted ? UiTheme.CanvasText : NormalForeColor;
                bd = HoverBorderColor;
            }
            else
            {
                bg = NormalBackColor;
                fg = NormalForeColor;
                bd = NormalBorderColor;
            }

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, CornerRadius);
            using (var fill = new SolidBrush(bg)) g.FillPath(fill, path);
            if (bd != Color.Transparent && bd != bg)
            {
                using var pen = new Pen(bd, 1f);
                g.DrawPath(pen, path);
            }

            if (LeftAccentColor != Color.Empty && Enabled)
            {
                float barH = Math.Max(12f, Height - 16f);
                RectangleF barRect = new(2.5f, (Height - barH) * 0.5f, 3f, barH);
                using GraphicsPath barPath = UiTheme.CreateRoundedRect(barRect, 1.5f);
                using var barBrush = new SolidBrush(LeftAccentColor);
                g.FillPath(barBrush, barPath);
            }

            if (Focused && ShowFocusCues && Enabled)
            {
                RectangleF focusRect = RectangleF.Inflate(rect, -2f, -2f);
                using GraphicsPath focusPath = UiTheme.CreateRoundedRect(focusRect, Math.Max(2f, CornerRadius - 2f));
                using var focusPen = new Pen(Color.FromArgb(195, 56, 189, 248), 1.25f) { DashStyle = DashStyle.Dot };
                g.DrawPath(focusPen, focusPath);
            }

            string txt = Text ?? "";
            Color icColor = !Enabled ? fg : (IconColor != Color.Empty ? IconColor : fg);
            Rectangle contentRect = new(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));

            if (IconKind == IndustrialIconKind.None)
            {
                TextRenderer.DrawText(g, txt, Font, contentRect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                return;
            }

            if (string.IsNullOrEmpty(txt))
            {
                float ix = contentRect.X + (contentRect.Width - IconSize) * 0.5f;
                float iy = contentRect.Y + (contentRect.Height - IconSize) * 0.5f;
                IndustrialIcons.DrawIcon(g, IconKind, new RectangleF(ix, iy, IconSize, IconSize), icColor);
                return;
            }

            Size txtSz = TextRenderer.MeasureText(txt, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (IconPlacement == ButtonIconPlacement.Top)
            {
                float totalH = IconSize + IconGap + txtSz.Height;
                float startY = contentRect.Y + (contentRect.Height - totalH) * 0.5f;
                float ix = contentRect.X + (contentRect.Width - IconSize) * 0.5f;
                IndustrialIcons.DrawIcon(g, IconKind, new RectangleF(ix, startY, IconSize, IconSize), icColor);
                Rectangle tr = new(contentRect.X, (int)Math.Round(startY + IconSize + IconGap), contentRect.Width, txtSz.Height + 2);
                TextRenderer.DrawText(g, txt, Font, tr, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            else
            {
                int availW = Math.Max(1, contentRect.Width);
                int totalW = Math.Min(availW, IconSize + IconGap + txtSz.Width);
                float startX = contentRect.X + (availW - totalW) * 0.5f;
                float iy = contentRect.Y + (contentRect.Height - IconSize) * 0.5f;
                IndustrialIcons.DrawIcon(g, IconKind, new RectangleF(startX, iy, IconSize, IconSize), icColor);
                int textX = (int)Math.Round(startX + IconSize + IconGap);
                int textW = Math.Max(1, contentRect.Right - textX);
                Rectangle tr = new(textX, contentRect.Y, textW, contentRect.Height);
                TextRenderer.DrawText(g, txt, Font, tr, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    internal sealed class CardPanel : Panel
    {
        private string _title = "";
        private string _badgeText = "";
        private Color _badgeBack = UiTheme.SurfaceSubtle;
        private Color _badgeFore = UiTheme.TextMuted;
        private int _headerHeight = 36;
        private Font? _titleFont;
        private Font? _badgeFont;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Title
        {
            get => _title;
            set { _title = value ?? ""; SyncPadding(); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string BadgeText
        {
            get => _badgeText;
            set { _badgeText = value ?? ""; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BadgeBackColor
        {
            get => _badgeBack;
            set { _badgeBack = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BadgeForeColor
        {
            get => _badgeFore;
            set { _badgeFore = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = UiTheme.RadiusCard;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ContentPadding { get; set; } = 10;

        public CardPanel()
        {
            BackColor = UiTheme.Surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            RebuildFonts();
            SyncPadding();
        }

        private int EffectiveHeaderHeight => Math.Max(_headerHeight, Font.Height + 16);

        private void RebuildFonts()
        {
            _titleFont?.Dispose();
            _badgeFont?.Dispose();
            _titleFont = new Font(Font.FontFamily, Font.Size + 0.2f, FontStyle.Bold);
            _badgeFont = new Font(Font.FontFamily, Math.Max(7.8f, Font.Size - 1f), FontStyle.Bold);
        }

        public void SetHeaderHeight(int h)
        {
            _headerHeight = Math.Max(28, h);
            SyncPadding();
            Invalidate();
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            _headerHeight = Math.Max(28, (int)Math.Round(_headerHeight * factor.Height));
            CornerRadius = UiTheme.ScaleRadius(CornerRadius, factor, 4, 20);
            SyncPadding();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            RebuildFonts();
            SyncPadding();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont?.Dispose();
                _badgeFont?.Dispose();
                _titleFont = null;
                _badgeFont = null;
            }
            base.Dispose(disposing);
        }

        private void SyncPadding()
        {
            int top = string.IsNullOrEmpty(_title) ? ContentPadding : EffectiveHeaderHeight + 6;
            Padding = new Padding(ContentPadding, top, ContentPadding, ContentPadding);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = UiTheme.ResolveOpaqueParentBack(this);
            g.Clear(parentBg);

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, CornerRadius);
            using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);

            if (!string.IsNullOrEmpty(_title))
            {
                int hh = EffectiveHeaderHeight;
                RectangleF hdrRect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), hh);
                using GraphicsPath hdrPath = UiTheme.CreateTopRoundedRect(hdrRect, CornerRadius);
                using (var hdrBrush = new SolidBrush(UiTheme.SurfaceHeader)) g.FillPath(hdrBrush, hdrPath);

                using var divPen = new Pen(UiTheme.Border, 1f);
                g.DrawLine(divPen, 1f, hh, Width - 2f, hh);

                int badgeUsedW = 0;
                if (!string.IsNullOrEmpty(_badgeText) && _badgeFont != null)
                {
                    Size bSz = TextRenderer.MeasureText(_badgeText, _badgeFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    int bw = Math.Min(Math.Max(28, bSz.Width + 16), Math.Max(52, Width / 2));
                    int bh = Math.Max(20, bSz.Height + 5);
                    int bx = Width - 12 - bw;
                    int by = (hh - bh) / 2;
                    RectangleF pillRect = new(bx, by, bw, bh);
                    using GraphicsPath pillPath = UiTheme.CreateRoundedRect(pillRect, bh * 0.5f);
                    using (var pillBrush = new SolidBrush(_badgeBack)) g.FillPath(pillBrush, pillPath);
                    using (var pillBorder = new Pen(UiTheme.Blend(_badgeBack, _badgeFore, 0.25f), 1f)) g.DrawPath(pillBorder, pillPath);
                    TextRenderer.DrawText(g, _badgeText, _badgeFont, new Rectangle(bx + 5, by, Math.Max(1, bw - 10), bh), _badgeFore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    badgeUsedW = bw + 10;
                }

                Rectangle titleRect = new(13, 0, Math.Max(20, Width - 26 - badgeUsedW), hh);
                TextRenderer.DrawText(g, _title, _titleFont ?? Font, titleRect, UiTheme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            using (var border = new Pen(UiTheme.Border, 1f)) g.DrawPath(border, path);
        }
    }

    internal sealed class KpiCard : Panel
    {
        private readonly Color _accentColor;
        private readonly Font _titleFont;
        private readonly Font _valueFont;
        private readonly Label _lblTitle;
        private readonly Label _lblValue;
        private float _cornerRadius = UiTheme.RadiusSubCard;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ValueText
        {
            get => _lblValue.Text;
            set => _lblValue.Text = value;
        }

        public KpiCard(string title, Color accentColor)
        {
            _accentColor = accentColor;
            BackColor = UiTheme.SurfaceSubtle;
            Margin = new Padding(3);
            Padding = new Padding(12, 7, 10, 7);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _titleFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular);
            _valueFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                BackColor = UiTheme.SurfaceSubtle
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));

            _lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                Font = _titleFont,
                ForeColor = UiTheme.TextMuted,
                AutoEllipsis = true,
                BackColor = UiTheme.SurfaceSubtle
            };
            _lblValue = new Label
            {
                Text = "—",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = _valueFont,
                ForeColor = UiTheme.TextPrimary,
                AutoEllipsis = true,
                BackColor = UiTheme.SurfaceSubtle
            };

            layout.Controls.Add(_lblTitle, 0, 0);
            layout.Controls.Add(_lblValue, 0, 1);
            Controls.Add(layout);
        }

        public void SetHighlightColor(Color color) => _lblValue.ForeColor = color;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _valueFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            _cornerRadius = UiTheme.ScaleRadius(_cornerRadius, factor, 4f, 18f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, _cornerRadius);
            using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
            using (var border = new Pen(UiTheme.Border, 1f)) g.DrawPath(border, path);

            RectangleF bar = new(4f, 9f, 3f, Math.Max(8f, Height - 18f));
            using GraphicsPath barPath = UiTheme.CreateRoundedRect(bar, 1.5f);
            using var accentBrush = new SolidBrush(_accentColor);
            g.FillPath(accentBrush, barPath);
        }
    }

    internal sealed class ModernToggle : Control
    {
        private bool _checked;
        private bool _hovered;

        public event EventHandler? CheckedChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ModernToggle()
        {
            Cursor = Cursors.Hand;
            ForeColor = UiTheme.TextPrimary;
            Height = 24;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return new Size(Math.Max(MinimumSize.Width, sz.Width + 46), Math.Max(24, sz.Height + 6));
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Checked = !Checked; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && e.KeyCode is Keys.Space or Keys.Enter) { Checked = !Checked; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            const float pillW = 32f;
            const float pillH = 18f;
            float py = (Height - pillH) * 0.5f;
            RectangleF pillRect = new(1f, py, pillW, pillH);

            Color trackCol = !Enabled
                ? Color.FromArgb(26, 36, 54)
                : (_checked
                    ? (_hovered ? UiTheme.PrimaryHover : UiTheme.Primary)
                    : (_hovered ? UiTheme.SurfacePressed : UiTheme.SurfaceHover));
            Color borderCol = !Enabled
                ? UiTheme.Border
                : (_checked ? UiTheme.Blend(UiTheme.Primary, Color.White, 0.2f) : UiTheme.InputBorder);

            using (GraphicsPath pp = UiTheme.CreateRoundedRect(pillRect, pillH * 0.5f))
            {
                using var tb = new SolidBrush(trackCol);
                g.FillPath(tb, pp);
                using var bp = new Pen(borderCol, 1f);
                g.DrawPath(bp, pp);
            }

            float knobSz = 12f;
            float knobX = _checked ? (pillRect.Right - knobSz - 3f) : (pillRect.X + 3f);
            float knobY = py + (pillH - knobSz) * 0.5f;
            using (var kb = new SolidBrush(Enabled ? Color.White : UiTheme.TextMuted))
                g.FillEllipse(kb, knobX, knobY, knobSz, knobSz);

            if (Focused && ShowFocusCues && Enabled)
            {
                using var fp = new Pen(Color.FromArgb(190, 56, 189, 248), 1.2f) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(fp, 0f, 0f, Width - 1.5f, Height - 1.5f);
            }

            Rectangle txtRect = new((int)(pillW + 9), 0, Math.Max(1, Width - (int)(pillW + 10)), Height);
            TextRenderer.DrawText(g, Text ?? "", Font, txtRect, Enabled ? ForeColor : UiTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class ModernCheckBox : Control
    {
        private bool _checked;
        private bool _hovered;

        public event EventHandler? CheckedChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ModernCheckBox()
        {
            Cursor = Cursors.Hand;
            ForeColor = UiTheme.TextSecondary;
            Height = 23;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return new Size(Math.Max(MinimumSize.Width, sz.Width + 28), Math.Max(22, sz.Height + 6));
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Checked = !Checked; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && e.KeyCode is Keys.Space or Keys.Enter) { Checked = !Checked; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            const float boxSz = 15f;
            float by = (Height - boxSz) * 0.5f;
            RectangleF boxRect = new(1f, by, boxSz, boxSz);

            Color fill = !Enabled
                ? Color.FromArgb(24, 34, 52)
                : (_checked
                    ? (_hovered ? UiTheme.PrimaryHover : UiTheme.Primary)
                    : (_hovered ? UiTheme.SurfaceHover : UiTheme.SurfaceSubtle));
            Color bd = !Enabled
                ? UiTheme.Border
                : (_checked ? UiTheme.PrimaryHover : (_hovered ? UiTheme.BorderHover : UiTheme.InputBorder));

            using (GraphicsPath bp = UiTheme.CreateRoundedRect(boxRect, UiTheme.RadiusSmall))
            {
                using var fb = new SolidBrush(fill);
                g.FillPath(fb, bp);
                using var pen = new Pen(bd, 1f);
                g.DrawPath(pen, bp);
            }

            if (_checked)
                IndustrialIcons.DrawIcon(g, IndustrialIconKind.Check, new RectangleF(boxRect.X + 1.5f, boxRect.Y + 1.5f, boxSz - 3f, boxSz - 3f), Enabled ? Color.White : UiTheme.TextMuted, 1.8f);

            if (Focused && ShowFocusCues && Enabled)
            {
                using var fp = new Pen(Color.FromArgb(190, 56, 189, 248), 1.2f) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(fp, 0f, 0f, Width - 1.5f, Height - 1.5f);
            }

            Rectangle txtRect = new(22, 0, Math.Max(1, Width - 23), Height);
            TextRenderer.DrawText(g, Text ?? "", Font, txtRect, Enabled ? ForeColor : UiTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class ModernRadioButton : Control
    {
        private bool _checked;
        private bool _hovered;

        public event EventHandler? CheckedChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                if (_checked && Parent != null)
                {
                    foreach (Control c in Parent.Controls)
                    {
                        if (!ReferenceEquals(c, this) && c is ModernRadioButton rb && rb.Checked)
                            rb.Checked = false;
                    }
                }
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ModernRadioButton()
        {
            Cursor = Cursors.Hand;
            ForeColor = UiTheme.TextPrimary;
            Height = 23;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return new Size(Math.Max(MinimumSize.Width, sz.Width + 28), Math.Max(22, sz.Height + 6));
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled && !Checked) Checked = true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && e.KeyCode is Keys.Space or Keys.Enter && !Checked) { Checked = true; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            const float sz = 15f;
            float y = (Height - sz) * 0.5f;
            RectangleF ring = new(1f, y, sz, sz);

            Color bg = !Enabled ? Color.FromArgb(24, 34, 52) : (_hovered ? UiTheme.SurfaceHover : UiTheme.SurfaceSubtle);
            Color bd = !Enabled ? UiTheme.Border : (_checked ? UiTheme.PrimaryHover : (_hovered ? UiTheme.BorderHover : UiTheme.InputBorder));
            using (var fb = new SolidBrush(bg)) g.FillEllipse(fb, ring);
            using (var pen = new Pen(bd, _checked ? 1.6f : 1f)) g.DrawEllipse(pen, ring);

            if (_checked)
            {
                using var dot = new SolidBrush(Enabled ? UiTheme.PrimaryHover : UiTheme.TextMuted);
                g.FillEllipse(dot, ring.X + 4f, ring.Y + 4f, sz - 8f, sz - 8f);
            }

            if (Focused && ShowFocusCues && Enabled)
            {
                using var fp = new Pen(Color.FromArgb(190, 56, 189, 248), 1.2f) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(fp, 0f, 0f, Width - 1.5f, Height - 1.5f);
            }

            Rectangle txtRect = new(22, 0, Math.Max(1, Width - 23), Height);
            TextRenderer.DrawText(g, Text ?? "", Font, txtRect, Enabled ? ForeColor : UiTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    internal class RoundedFieldBase : Panel
    {
        private bool _focused;
        private Control? _inner;
        private float _cornerRadius = UiTheme.RadiusControl;
        private int _baseHeight;

        protected RoundedFieldBase(int height, Padding padding)
        {
            _baseHeight = height;
            BackColor = UiTheme.SurfaceSubtle;
            Size = new Size(96, height);
            Padding = padding;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected void AttachInner(Control inner)
        {
            _inner = inner;
            inner.Enter += (_, _) => SetFocused(true);
            inner.Leave += (_, _) => { if (!inner.ContainsFocus) SetFocused(false); };
            inner.GotFocus += (_, _) => SetFocused(true);
            inner.LostFocus += (_, _) => { if (!inner.ContainsFocus) SetFocused(false); };
            Controls.Add(inner);
            PerformLayout();
        }

        private void SetFocused(bool value)
        {
            if (_focused == value) return;
            _focused = value;
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int minH = _baseHeight;
            if (_inner != null)
                minH = Math.Max(minH, _inner.GetPreferredSize(Size.Empty).Height + Padding.Vertical);
            return new Size(Math.Max(MinimumSize.Width, 72), minH);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_inner == null) return;
            int availW = Math.Max(16, ClientSize.Width - Padding.Horizontal);
            int innerH = _inner.GetPreferredSize(new Size(availW, 0)).Height;
            int maxH = Math.Max(12, ClientSize.Height - 4);
            int h = Math.Min(innerH, maxH);
            int y = Math.Max(2, (ClientSize.Height - h) / 2);
            _inner.SetBounds(Padding.Left, y, availW, h);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            _baseHeight = Math.Max(24, (int)Math.Round(_baseHeight * factor.Height));
            _cornerRadius = UiTheme.ScaleRadius(_cornerRadius, factor);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _inner?.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, _cornerRadius);
            using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
            using (var border = new Pen(_focused ? UiTheme.PrimaryHover : UiTheme.InputBorder, _focused ? 1.5f : 1f))
                g.DrawPath(border, path);
        }
    }

    internal sealed class TextBoxField : RoundedFieldBase
    {
        public TextBox Inner { get; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text
        {
            get => Inner.Text;
            set => Inner.Text = value ?? string.Empty;
        }

        public TextBoxField(string initialText, Font font, int height = 28)
            : base(height, new Padding(8, 4, 8, 4))
        {
            Inner = new TextBox
            {
                Text = initialText,
                Font = font,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.SurfaceSubtle,
                ForeColor = UiTheme.TextPrimary
            };
            AttachInner(Inner);
        }
    }

    internal sealed class ModernStepper : Panel
    {
        private readonly TextBox _input;
        private readonly RoundedButton _btnMinus;
        private readonly RoundedButton _btnPlus;
        private decimal _minimum;
        private decimal _maximum;
        private decimal _increment;
        private decimal _value;
        private bool _focused;
        private float _cornerRadius = UiTheme.RadiusControl;
        private int _baseHeight;

        public event EventHandler? ValueChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Minimum
        {
            get => _minimum;
            set { _minimum = value; if (_maximum < _minimum) _maximum = _minimum; Value = _value; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Maximum
        {
            get => _maximum;
            set { _maximum = Math.Max(_minimum, value); Value = _value; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Increment
        {
            get => _increment;
            set => _increment = Math.Max(1m, value);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal Value
        {
            get => _value;
            set
            {
                decimal clamped = Math.Clamp(Math.Round(value), _minimum, _maximum);
                if (clamped == _value && _input.Text == clamped.ToString(CultureInfo.InvariantCulture)) return;
                bool changed = clamped != _value;
                _value = clamped;
                _input.Text = _value.ToString(CultureInfo.InvariantCulture);
                SyncButtonStates();
                if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ModernStepper(decimal min, decimal max, decimal increment, decimal value, Font font, int height = 28)
        {
            _minimum = min;
            _maximum = Math.Max(min, max);
            _increment = Math.Max(1m, increment);
            _value = Math.Clamp(value, _minimum, _maximum);
            _baseHeight = height;

            BackColor = UiTheme.SurfaceSubtle;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _btnMinus = new RoundedButton
            {
                Text = "",
                IconKind = IndustrialIconKind.Minus,
                IconSize = 11,
                CornerRadius = UiTheme.RadiusSmall,
                Margin = new Padding(0)
            };
            _btnMinus.ApplyDarkTone(false);
            _btnMinus.NormalBorderColor = Color.Transparent;
            _btnMinus.NormalBackColor = UiTheme.SurfaceSubtle;
            _btnMinus.HoverBackColor = UiTheme.SurfaceHover;
            _btnMinus.Click += (_, _) => Step(-_increment);

            _btnPlus = new RoundedButton
            {
                Text = "",
                IconKind = IndustrialIconKind.Plus,
                IconSize = 11,
                CornerRadius = UiTheme.RadiusSmall,
                Margin = new Padding(0)
            };
            _btnPlus.ApplyDarkTone(false);
            _btnPlus.NormalBorderColor = Color.Transparent;
            _btnPlus.NormalBackColor = UiTheme.SurfaceSubtle;
            _btnPlus.HoverBackColor = UiTheme.SurfaceHover;
            _btnPlus.Click += (_, _) => Step(_increment);

            _input = new TextBox
            {
                Text = _value.ToString(CultureInfo.InvariantCulture),
                Font = font,
                BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Center,
                BackColor = UiTheme.SurfaceSubtle,
                ForeColor = UiTheme.TextPrimary
            };
            _input.Enter += (_, _) => { _focused = true; Invalidate(); };
            _input.Leave += (_, _) => { _focused = false; CommitInput(); Invalidate(); };
            _input.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { CommitInput(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { Step(_increment); e.Handled = true; }
                else if (e.KeyCode == Keys.Down) { Step(-_increment); e.Handled = true; }
            };

            Controls.Add(_btnMinus);
            Controls.Add(_input);
            Controls.Add(_btnPlus);
            Size = new Size(104, height);
            MinimumSize = new Size(88, height);
            SyncButtonStates();
            PerformLayout();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int ih = _input != null ? _input.GetPreferredSize(Size.Empty).Height + 8 : _baseHeight;
            return new Size(Math.Max(MinimumSize.Width, 96), Math.Max(_baseHeight, ih));
        }

        private void Step(decimal delta)
        {
            CommitInput();
            Value = _value + delta;
        }

        private void CommitInput()
        {
            if (decimal.TryParse(_input.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
                Value = parsed;
            else
                _input.Text = _value.ToString(CultureInfo.InvariantCulture);
        }

        private void SyncButtonStates()
        {
            _btnMinus.Enabled = Enabled && _value > _minimum;
            _btnPlus.Enabled = Enabled && _value < _maximum;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _input.Enabled = Enabled;
            SyncButtonStates();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_btnMinus == null || _input == null || _btnPlus == null) return;
            int h = Math.Max(20, ClientSize.Height);
            int btnW = Math.Clamp(h - 4, 22, 28);
            _btnMinus.SetBounds(2, 2, btnW, h - 4);
            _btnPlus.SetBounds(ClientSize.Width - btnW - 2, 2, btnW, h - 4);

            int textW = Math.Max(20, ClientSize.Width - (btnW + 4) * 2);
            int ih = _input.GetPreferredSize(new Size(textW, 0)).Height;
            int iy = Math.Max(2, (h - ih) / 2);
            _input.SetBounds(btnW + 4, iy, textW, Math.Min(ih, h - 4));
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            _baseHeight = Math.Max(24, (int)Math.Round(_baseHeight * factor.Height));
            _cornerRadius = UiTheme.ScaleRadius(_cornerRadius, factor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, _cornerRadius);
            using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
            using (var border = new Pen(_focused ? UiTheme.PrimaryHover : UiTheme.InputBorder, _focused ? 1.5f : 1f))
                g.DrawPath(border, path);

            int btnW = Math.Clamp(ClientSize.Height - 4, 22, 28);
            using var sepPen = new Pen(UiTheme.Border, 1f);
            g.DrawLine(sepPen, btnW + 2.5f, 4f, btnW + 2.5f, Height - 5f);
            g.DrawLine(sepPen, Width - btnW - 3f, 4f, Width - btnW - 3f, Height - 5f);
        }
    }

    internal sealed class RoundedFlowPanel : FlowLayoutPanel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = UiTheme.RadiusControl;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.Empty;

        public RoundedFlowPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            CornerRadius = UiTheme.ScaleRadius(CornerRadius, factor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, CornerRadius);
            using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
            if (BorderColor != Color.Empty)
            {
                using var pen = new Pen(BorderColor, 1f);
                g.DrawPath(pen, path);
            }
        }
    }

    internal sealed class DarkPillLabel : Label
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = UiTheme.RadiusControl;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = UiTheme.CanvasBtnBorder;

        public DarkPillLabel()
        {
            AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            CornerRadius = UiTheme.ScaleRadius(CornerRadius, factor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, CornerRadius);
            using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
            if (BorderColor != Color.Empty)
            {
                using var pen = new Pen(BorderColor, 1f);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text ?? "", Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class ModernSlider : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 50;
        private bool _dragging;
        private bool _hovered;

        public event EventHandler? ValueChanged;
        public event EventHandler? ScrollChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get => _minimum;
            set { _minimum = value; if (_maximum < _minimum) _maximum = _minimum; Value = _value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => _maximum;
            set { _maximum = Math.Max(_minimum, value); Value = _value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Clamp(value, _minimum, _maximum);
                if (clamped == _value) return;
                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color TrackColor { get; set; } = UiTheme.SurfaceHover;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = UiTheme.PrimaryHover;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ThumbColor { get; set; } = UiTheme.TextPrimary;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ThumbBorderColor { get; set; } = UiTheme.PrimaryHover;

        public ModernSlider()
        {
            Cursor = Cursors.Hand;
            Height = 24;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public void ApplyDarkTheme()
        {
            TrackColor = Color.FromArgb(35, 49, 74);
            FillColor = Color.FromArgb(56, 189, 248);
            ThumbColor = Color.White;
            ThumbBorderColor = Color.FromArgb(56, 189, 248);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.Shift ? 5 : 1;
            if (e.KeyCode is Keys.Left or Keys.Down) { SetFromInput(_value - step); e.Handled = true; }
            else if (e.KeyCode is Keys.Right or Keys.Up) { SetFromInput(_value + step); e.Handled = true; }
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _dragging = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { _dragging = false; Invalidate(); base.OnMouseCaptureChanged(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _dragging = true;
            UpdateFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) UpdateFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging) { _dragging = false; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            SetFromInput(_value + (e.Delta > 0 ? 1 : -1));
        }

        private void UpdateFromX(int x)
        {
            const float pad = 10f;
            float trackW = Math.Max(1f, Width - pad * 2f);
            float t = Math.Clamp((x - pad) / trackW, 0f, 1f);
            int next = _minimum + (int)Math.Round(t * Math.Max(0, _maximum - _minimum));
            SetFromInput(next);
        }

        private void SetFromInput(int next)
        {
            int clamped = Math.Clamp(next, _minimum, _maximum);
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
            ScrollChanged?.Invoke(this, EventArgs.Empty);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            const float pad = 10f;
            float trackW = Math.Max(4f, Width - pad * 2f);
            float trackH = 4f;
            float cy = Height * 0.5f;
            float ty = cy - trackH * 0.5f;

            RectangleF trackRect = new(pad, ty, trackW, trackH);
            using (GraphicsPath tp = UiTheme.CreateRoundedRect(trackRect, trackH * 0.5f))
            using (var tb = new SolidBrush(TrackColor))
                g.FillPath(tb, tp);

            int range = Math.Max(1, _maximum - _minimum);
            float ratio = (_value - _minimum) / (float)range;
            float fillW = trackW * ratio;
            if (fillW > 1f)
            {
                RectangleF fillRect = new(pad, ty, fillW, trackH);
                using GraphicsPath fp = UiTheme.CreateRoundedRect(fillRect, trackH * 0.5f);
                using var fb = new SolidBrush(FillColor);
                g.FillPath(fb, fp);
            }

            float thumbX = pad + fillW;
            float r = (_dragging || _hovered) ? 6.5f : 5.5f;
            if (_dragging || _hovered || (Focused && ShowFocusCues))
            {
                using var halo = new SolidBrush(Color.FromArgb(64, FillColor));
                g.FillEllipse(halo, thumbX - (r + 3.5f), cy - (r + 3.5f), (r + 3.5f) * 2f, (r + 3.5f) * 2f);
            }
            using (var thumbBrush = new SolidBrush(ThumbColor))
                g.FillEllipse(thumbBrush, thumbX - r, cy - r, r * 2f, r * 2f);
            using (var thumbPen = new Pen(ThumbBorderColor, 2f))
                g.DrawEllipse(thumbPen, thumbX - r, cy - r, r * 2f, r * 2f);
        }
    }

    internal sealed class ModernProgressBar : Control
    {
        private int _maximum = 1000;
        private int _value;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => _maximum;
            set { _maximum = Math.Max(1, value); _value = Math.Clamp(_value, 0, _maximum); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set { _value = Math.Clamp(value, 0, _maximum); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color TrackColor { get; set; } = UiTheme.SurfaceSubtle;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = UiTheme.PrimaryHover;

        public ModernProgressBar()
        {
            Size = new Size(180, 12);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            float h = Math.Min(7f, Math.Max(4f, Height - 4f));
            float y = (Height - h) * 0.5f;
            RectangleF track = new(0f, y, Math.Max(4f, Width - 1f), h);
            using (GraphicsPath tp = UiTheme.CreateRoundedRect(track, h * 0.5f))
            {
                using var tb = new SolidBrush(TrackColor);
                g.FillPath(tb, tp);
                using var tbd = new Pen(UiTheme.Border, 1f);
                g.DrawPath(tbd, tp);
            }

            float ratio = _value / (float)_maximum;
            float fw = track.Width * ratio;
            if (fw > 1f)
            {
                RectangleF fill = new(0f, y, Math.Max(h, Math.Min(track.Width, fw)), h);
                using GraphicsPath fp = UiTheme.CreateRoundedRect(fill, h * 0.5f);
                using var grad = new LinearGradientBrush(fill, FillColor, UiTheme.Cyan, LinearGradientMode.Horizontal);
                g.FillPath(grad, fp);
            }
        }
    }

    internal sealed class StatusBadge : Control
    {
        private readonly Font _badgeFont;
        private Color _dotColor = UiTheme.Success;
        private Color _pillBack = UiTheme.SuccessSoftBg;
        private Color _pillBorder = UiTheme.SuccessSoftBorder;
        private Color _pillFore = UiTheme.SuccessText;

        public StatusBadge()
        {
            _badgeFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
            Font = _badgeFont;
            Padding = new Padding(9, 2, 9, 2);
            AutoSize = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetState(string text, Color dotColor, Color bg, Color border, Color fg)
        {
            Text = text;
            _dotColor = dotColor;
            _pillBack = bg;
            _pillBorder = border;
            _pillFore = fg;
            if (AutoSize) Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return new Size(sz.Width + Padding.Horizontal + 16, Math.Max(22, sz.Height + Padding.Vertical + 4));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _badgeFont.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, rect.Height * 0.5f);
            using (var bg = new SolidBrush(_pillBack)) g.FillPath(bg, path);
            using (var bd = new Pen(_pillBorder, 1f)) g.DrawPath(bd, path);

            float dotSz = 6.5f;
            float dotY = (Height - dotSz) * 0.5f;
            using (var glow = new SolidBrush(Color.FromArgb(70, _dotColor)))
                g.FillEllipse(glow, Padding.Left - 2f, dotY - 2f, dotSz + 4f, dotSz + 4f);
            using (var dot = new SolidBrush(_dotColor))
                g.FillEllipse(dot, Padding.Left, dotY, dotSz, dotSz);

            Rectangle txtRect = new(Padding.Left + 11, 0, Math.Max(1, Width - Padding.Horizontal - 11), Height);
            TextRenderer.DrawText(g, Text ?? "", Font, txtRect, _pillFore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class DarkSelectButton : Control
    {
        private readonly List<string> _items = new();
        private readonly Font _btnFont;
        private readonly Font _menuFont;
        private readonly ContextMenuStrip _menu;
        private int _selectedIndex = -1;
        private bool _hovered;

        public event EventHandler? SelectedIndexChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Prefix { get; set; } = "";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IndustrialIconKind IconKind { get; set; } = IndustrialIconKind.LabelTag;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = UiTheme.RadiusControl;

        public IReadOnlyList<string> Items => _items;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_items.Count == 0) { _selectedIndex = -1; return; }
                int next = Math.Clamp(value, 0, _items.Count - 1);
                if (next == _selectedIndex) return;
                _selectedIndex = next;
                SyncMenuChecks();
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public DarkSelectButton()
        {
            Cursor = Cursors.Hand;
            _btnFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold);
            _menuFont = new Font("Microsoft YaHei UI", 8.8f);
            Font = _btnFont;
            Size = new Size(124, 28);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

            _menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Padding = new Padding(2, 4, 2, 4),
                Renderer = new DarkMenuRenderer(() => CornerRadius),
                BackColor = UiTheme.SurfaceSubtle,
                ForeColor = UiTheme.CanvasText,
                Font = _menuFont
            };
            _menu.Opening += (_, _) => UpdateMenuRegion();
            _menu.SizeChanged += (_, _) => UpdateMenuRegion();
        }

        private void UpdateMenuRegion()
        {
            if (_menu.Width <= 0 || _menu.Height <= 0) return;
            using GraphicsPath path = UiTheme.CreateRoundedRect(new RectangleF(0f, 0f, _menu.Width, _menu.Height), CornerRadius + 0.5f);
            Region next = new(path);
            Region? old = _menu.Region;
            _menu.Region = next;
            old?.Dispose();
        }

        public void SetItems(IEnumerable<string> items, int initialIndex = 0)
        {
            _items.Clear();
            _items.AddRange(items);
            _menu.Items.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                var mi = new ToolStripMenuItem(_items[i]) { BackColor = UiTheme.SurfaceSubtle, ForeColor = UiTheme.CanvasText };
                mi.Click += (_, _) => SelectedIndex = idx;
                _menu.Items.Add(mi);
            }
            _selectedIndex = _items.Count > 0 ? Math.Clamp(initialIndex, 0, _items.Count - 1) : -1;
            SyncMenuChecks();
            Invalidate();
        }

        public void FitWidth() => Size = GetPreferredSize(Size.Empty);

        public override Size GetPreferredSize(Size proposedSize)
        {
            int maxW = 64;
            int maxH = 18;
            foreach (string item in _items)
            {
                string label = string.IsNullOrEmpty(Prefix) ? item : $"{Prefix}{item}";
                Size sz = TextRenderer.MeasureText(label, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (sz.Width > maxW) maxW = sz.Width;
                if (sz.Height > maxH) maxH = sz.Height;
            }
            int iconSpace = IconKind == IndustrialIconKind.None ? 0 : 18;
            return new Size(Math.Max(MinimumSize.Width, maxW + 30 + iconSpace), Math.Max(Math.Max(MinimumSize.Height, 28), maxH + 10));
        }

        private void SyncMenuChecks()
        {
            for (int i = 0; i < _menu.Items.Count; i++)
                if (_menu.Items[i] is ToolStripMenuItem mi) mi.Checked = i == _selectedIndex;
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            CornerRadius = UiTheme.ScaleRadius(CornerRadius, factor);
            UpdateMenuRegion();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_items.Count > 0) FitWidth();
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (_items.Count > 0) _menu.Show(this, new Point(0, Height + 3)); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Space or Keys.Enter or Keys.Down) { if (_items.Count > 0) _menu.Show(this, new Point(0, Height + 3)); e.Handled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _menu.Dispose();
                _btnFont.Dispose();
                _menuFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, CornerRadius);
            using (var bg = new SolidBrush(_hovered ? UiTheme.CanvasBtnHover : UiTheme.CanvasBtnBg)) g.FillPath(bg, path);
            using (var bd = new Pen(_hovered ? UiTheme.BorderHover : UiTheme.CanvasBtnBorder, 1f)) g.DrawPath(bd, path);

            if (Focused && ShowFocusCues)
            {
                RectangleF focusRect = RectangleF.Inflate(rect, -2f, -2f);
                using GraphicsPath focusPath = UiTheme.CreateRoundedRect(focusRect, Math.Max(2f, CornerRadius - 2f));
                using var focusPen = new Pen(Color.FromArgb(185, 56, 189, 248), 1.2f) { DashStyle = DashStyle.Dot };
                g.DrawPath(focusPen, focusPath);
            }

            int textLeft = 8;
            if (IconKind != IndustrialIconKind.None)
            {
                IndustrialIcons.DrawIcon(g, IconKind, new RectangleF(8f, (Height - 13f) * 0.5f, 13f, 13f), UiTheme.Cyan, 1.4f);
                textLeft = 25;
            }

            string cur = (_selectedIndex >= 0 && _selectedIndex < _items.Count) ? _items[_selectedIndex] : "-";
            string label = string.IsNullOrEmpty(Prefix) ? cur : $"{Prefix}{cur}";
            Rectangle txtRect = new(textLeft, 0, Math.Max(10, Width - textLeft - 20), Height);
            TextRenderer.DrawText(g, label, Font, txtRect, UiTheme.CanvasText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            float cx = Width - 11f;
            float cy = Height * 0.5f - 1.5f;
            using var chevPen = new Pen(UiTheme.CanvasMuted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(chevPen, new[] { new PointF(cx - 3.2f, cy), new PointF(cx, cy + 3.2f), new PointF(cx + 3.2f, cy) });
        }

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly Func<int> _getRadius;
            public DarkMenuRenderer(Func<int> getRadius) : base(new DarkColors()) => _getRadius = getRadius;

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using var bg = new SolidBrush(UiTheme.SurfaceSubtle);
                e.Graphics.FillRectangle(bg, e.AffectedBounds);
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
            {
                using var bg = new SolidBrush(UiTheme.SurfaceSubtle);
                e.Graphics.FillRectangle(bg, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, e.AffectedBounds.Width - 1.5f), Math.Max(1f, e.AffectedBounds.Height - 1.5f));
                using GraphicsPath path = UiTheme.CreateRoundedRect(rect, _getRadius());
                using var pen = new Pen(UiTheme.CanvasBtnBorder, 1f);
                e.Graphics.DrawPath(pen, path);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float r = Math.Max(2f, _getRadius() - 2f);
                RectangleF rect = new(3f, 1f, Math.Max(1f, e.Item.Width - 6f), Math.Max(1f, e.Item.Height - 2f));
                using GraphicsPath path = UiTheme.CreateRoundedRect(rect, r);
                using var br = new SolidBrush(UiTheme.SurfaceHover);
                e.Graphics.FillPath(br, path);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = UiTheme.CanvasText;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = e.ImageRectangle;
                using var p = new Pen(UiTheme.Cyan, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                e.Graphics.DrawLines(p, new[] { new PointF(r.Left + 3, r.Top + 8), new PointF(r.Left + 6.5f, r.Top + 11.5f), new PointF(r.Left + 12, r.Top + 4.5f) });
            }
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color MenuBorder => UiTheme.CanvasBtnBorder;
            public override Color MenuItemBorder => UiTheme.Primary;
            public override Color MenuItemSelected => UiTheme.SurfaceHover;
            public override Color MenuStripGradientBegin => UiTheme.SurfaceSubtle;
            public override Color MenuStripGradientEnd => UiTheme.SurfaceSubtle;
            public override Color ToolStripDropDownBackground => UiTheme.SurfaceSubtle;
            public override Color ImageMarginGradientBegin => UiTheme.SurfaceSubtle;
            public override Color ImageMarginGradientMiddle => UiTheme.SurfaceSubtle;
            public override Color ImageMarginGradientEnd => UiTheme.SurfaceSubtle;
        }
    }

    internal sealed class DarkCheckPill : Control
    {
        private readonly Font _pillFont;
        private bool _checked = true;
        private bool _hovered;

        public event EventHandler? CheckedChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ActiveDotColor { get; set; } = Color.FromArgb(244, 114, 182);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IndustrialIconKind IconKind { get; set; } = IndustrialIconKind.TargetWmax;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = UiTheme.RadiusControl;

        public DarkCheckPill()
        {
            Cursor = Cursors.Hand;
            _pillFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold);
            Font = _pillFont;
            Size = new Size(96, 28);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return new Size(Math.Max(MinimumSize.Width, sz.Width + 32), Math.Max(Math.Max(MinimumSize.Height, 28), sz.Height + 10));
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            CornerRadius = UiTheme.ScaleRadius(CornerRadius, factor);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Space or Keys.Enter) { Checked = !Checked; e.Handled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _pillFont.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            Color bg = _checked
                ? (_hovered ? Color.FromArgb(72, 32, 62) : Color.FromArgb(52, 23, 47))
                : (_hovered ? UiTheme.CanvasBtnHover : UiTheme.CanvasBtnBg);
            Color bd = _checked ? Color.FromArgb(219, 39, 119) : UiTheme.CanvasBtnBorder;
            Color fg = _checked ? Color.White : (_hovered ? UiTheme.CanvasText : UiTheme.CanvasMuted);

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, CornerRadius);
            using (var fb = new SolidBrush(bg)) g.FillPath(fb, path);
            using (var bp = new Pen(bd, 1f)) g.DrawPath(bp, path);

            if (Focused && ShowFocusCues)
            {
                RectangleF focusRect = RectangleF.Inflate(rect, -2f, -2f);
                using GraphicsPath focusPath = UiTheme.CreateRoundedRect(focusRect, Math.Max(2f, CornerRadius - 2f));
                using var focusPen = new Pen(Color.FromArgb(185, 56, 189, 248), 1.2f) { DashStyle = DashStyle.Dot };
                g.DrawPath(focusPen, focusPath);
            }

            if (IconKind != IndustrialIconKind.None)
            {
                Color icCol = _checked ? ActiveDotColor : UiTheme.CanvasMuted;
                IndustrialIcons.DrawIcon(g, IconKind, new RectangleF(7f, (Height - 13f) * 0.5f, 13f, 13f), icCol, 1.45f);
            }

            Rectangle txtRect = new(24, 0, Math.Max(1, Width - 28), Height);
            TextRenderer.DrawText(g, Text ?? "", Font, txtRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class StudioSplitter : Splitter
    {
        private bool _hovered;

        public StudioSplitter()
        {
            BackColor = UiTheme.AppBg;
            Width = 6;
            MinExtra = 380;
            MinSize = 230;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.AppBg);

            float cx = Width * 0.5f;
            float cy = Height * 0.5f;
            Color dotCol = _hovered ? UiTheme.PrimaryHover : UiTheme.Border;
            if (_hovered)
            {
                using var linePen = new Pen(Color.FromArgb(80, UiTheme.PrimaryHover), 1f);
                g.DrawLine(linePen, cx, 12f, cx, Math.Max(12f, Height - 12f));
            }
            using var br = new SolidBrush(dotCol);
            for (int i = -2; i <= 2; i++)
                g.FillEllipse(br, cx - 1.2f, cy + i * 6f - 1.2f, 2.4f, 2.4f);
        }
    }

    internal sealed class RoundedViewportHost : Panel
    {
        private float _cornerRadius = UiTheme.RadiusCard;

        public Panel Content { get; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius
        {
            get => (int)Math.Round(_cornerRadius);
            set { _cornerRadius = Math.Max(2f, value); SyncContentRegion(); Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = UiTheme.Border;

        public RoundedViewportHost()
        {
            BackColor = UiTheme.AppBg;
            Padding = new Padding(1);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            Content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.CanvasBg
            };
            Content.SizeChanged += (_, _) => SyncContentRegion();
            Controls.Add(Content);
        }

        private void SyncContentRegion()
        {
            if (Content.Width <= 2 || Content.Height <= 2) return;
            float r = Math.Max(2f, _cornerRadius - 1f);
            using GraphicsPath path = UiTheme.CreateRoundedRect(new RectangleF(0f, 0f, Content.Width, Content.Height), r);
            Region next = new(path);
            Region? old = Content.Region;
            Content.Region = next;
            old?.Dispose();
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            _cornerRadius = UiTheme.ScaleRadius(_cornerRadius, factor, 3f, 16f);
            SyncContentRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.ResolveOpaqueParentBack(this));

            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1.5f), Math.Max(1f, Height - 1.5f));
            using GraphicsPath path = UiTheme.CreateRoundedRect(rect, _cornerRadius);
            using (var fill = new SolidBrush(UiTheme.CanvasBg)) g.FillPath(fill, path);
            using (var border = new Pen(BorderColor, 1f)) g.DrawPath(border, path);
        }
    }
}
