using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CrackMeasure
{
    internal enum IndustrialIconKind
    {
        None = 0,
        BrandReticle,
        AddImage,
        Folder,
        PlayScan,
        BatchRun,
        StopSquare,
        ExportImage,
        CadLayer,
        ReportDoc,
        BatchExport,
        PanProbe,
        CalibrateRuler,
        FreeCaliper,
        BrushDraw,
        Eraser,
        Undo,
        TargetWmax,
        Reprocess,
        RemoveItem,
        TrashClear,
        FitWindow,
        ZoomOneToOne,
        LabelTag,
        PanelLeft,
        PanelRight,
        Plus,
        Minus,
        Check
    }

    internal static class IndustrialIcons
    {
        public static void DrawIcon(Graphics g, IndustrialIconKind kind, RectangleF bounds, Color color, float strokeWidth = 1.5f)
        {
            if (kind == IndustrialIconKind.None || bounds.Width <= 2f || bounds.Height <= 2f) return;

            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X, bounds.Y);
            float s = Math.Min(bounds.Width, bounds.Height) / 16f;
            g.ScaleTransform(s, s);

            float penW = Math.Max(1.1f, strokeWidth / Math.Max(0.75f, s));
            using var pen = new Pen(color, penW)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using var fill = new SolidBrush(color);

            switch (kind)
            {
                case IndustrialIconKind.BrandReticle:
                    g.DrawLine(pen, 2f, 5f, 2f, 2f); g.DrawLine(pen, 2f, 2f, 5f, 2f);
                    g.DrawLine(pen, 11f, 2f, 14f, 2f); g.DrawLine(pen, 14f, 2f, 14f, 5f);
                    g.DrawLine(pen, 2f, 11f, 2f, 14f); g.DrawLine(pen, 2f, 14f, 5f, 14f);
                    g.DrawLine(pen, 11f, 14f, 14f, 14f); g.DrawLine(pen, 14f, 14f, 14f, 11f);
                    g.DrawLines(pen, new[] { new PointF(3.5f, 12f), new PointF(7f, 7.5f), new PointF(9.5f, 9.5f), new PointF(12.5f, 4f) });
                    g.FillEllipse(fill, 7.8f, 7.8f, 3f, 3f);
                    break;

                case IndustrialIconKind.AddImage:
                    g.DrawRectangle(pen, 2f, 3.5f, 10f, 10f);
                    g.DrawLines(pen, new[] { new PointF(3.5f, 11.5f), new PointF(6f, 8.5f), new PointF(8f, 10.5f), new PointF(10.5f, 8f) });
                    g.DrawLine(pen, 13.5f, 1.8f, 13.5f, 6.2f);
                    g.DrawLine(pen, 11.3f, 4f, 15.7f, 4f);
                    break;

                case IndustrialIconKind.Folder:
                    g.DrawLines(pen, new[]
                    {
                        new PointF(2f, 4f), new PointF(6.2f, 4f), new PointF(7.6f, 5.6f),
                        new PointF(14f, 5.6f), new PointF(14f, 12.8f), new PointF(2f, 12.8f), new PointF(2f, 4f)
                    });
                    break;

                case IndustrialIconKind.PlayScan:
                    g.FillPolygon(fill, new[] { new PointF(4.2f, 2.8f), new PointF(13f, 8f), new PointF(4.2f, 13.2f) });
                    break;

                case IndustrialIconKind.BatchRun:
                    g.DrawLines(pen, new[] { new PointF(2.2f, 4.2f), new PointF(10.5f, 4.2f) });
                    g.DrawLines(pen, new[] { new PointF(2.2f, 8f), new PointF(8f, 8f) });
                    g.DrawLines(pen, new[] { new PointF(2.2f, 11.8f), new PointF(8f, 11.8f) });
                    g.FillPolygon(fill, new[] { new PointF(10f, 6.2f), new PointF(14.5f, 9.8f), new PointF(10f, 13.4f) });
                    break;

                case IndustrialIconKind.StopSquare:
                    g.FillRectangle(fill, 4f, 4f, 8f, 8f);
                    break;

                case IndustrialIconKind.ExportImage:
                    g.DrawLines(pen, new[] { new PointF(9.5f, 2.5f), new PointF(2.5f, 2.5f), new PointF(2.5f, 13.5f), new PointF(13.5f, 13.5f), new PointF(13.5f, 7f) });
                    g.DrawLine(pen, 8.5f, 7.5f, 14f, 2f);
                    g.DrawLines(pen, new[] { new PointF(10.5f, 2f), new PointF(14f, 2f), new PointF(14f, 5.5f) });
                    break;

                case IndustrialIconKind.CadLayer:
                    g.DrawPolygon(pen, new[] { new PointF(8f, 2f), new PointF(14f, 5.5f), new PointF(8f, 9f), new PointF(2f, 5.5f) });
                    g.DrawLines(pen, new[] { new PointF(2f, 8.8f), new PointF(8f, 12.2f), new PointF(14f, 8.8f) });
                    g.DrawLines(pen, new[] { new PointF(2f, 11.8f), new PointF(8f, 14.8f), new PointF(14f, 11.8f) });
                    break;

                case IndustrialIconKind.ReportDoc:
                    g.DrawLines(pen, new[]
                    {
                        new PointF(3.5f, 2f), new PointF(10f, 2f), new PointF(13f, 5f),
                        new PointF(13f, 14f), new PointF(3.5f, 14f), new PointF(3.5f, 2f)
                    });
                    g.DrawLine(pen, 5.8f, 7.5f, 10.7f, 7.5f);
                    g.DrawLine(pen, 5.8f, 10.6f, 10.7f, 10.6f);
                    break;

                case IndustrialIconKind.BatchExport:
                    g.DrawLines(pen, new[] { new PointF(2.5f, 10f), new PointF(2.5f, 13.5f), new PointF(13.5f, 13.5f), new PointF(13.5f, 10f) });
                    g.DrawLine(pen, 8f, 10.5f, 8f, 2.5f);
                    g.DrawLines(pen, new[] { new PointF(4.8f, 5.7f), new PointF(8f, 2.5f), new PointF(11.2f, 5.7f) });
                    break;

                case IndustrialIconKind.PanProbe:
                    g.DrawEllipse(pen, 3.5f, 3.5f, 9f, 9f);
                    g.DrawLine(pen, 8f, 1.5f, 8f, 5f);
                    g.DrawLine(pen, 8f, 11f, 8f, 14.5f);
                    g.DrawLine(pen, 1.5f, 8f, 5f, 8f);
                    g.DrawLine(pen, 11f, 8f, 14.5f, 8f);
                    g.FillEllipse(fill, 7f, 7f, 2f, 2f);
                    break;

                case IndustrialIconKind.CalibrateRuler:
                    g.DrawRectangle(pen, 2f, 5f, 12f, 6f);
                    g.DrawLine(pen, 5f, 5f, 5f, 8f);
                    g.DrawLine(pen, 8f, 5f, 8f, 9.2f);
                    g.DrawLine(pen, 11f, 5f, 11f, 8f);
                    break;

                case IndustrialIconKind.FreeCaliper:
                    g.DrawLine(pen, 3f, 13f, 13f, 3f);
                    g.DrawLine(pen, 2f, 10.5f, 5.5f, 14f);
                    g.DrawLine(pen, 10.5f, 2f, 14f, 5.5f);
                    break;

                case IndustrialIconKind.BrushDraw:
                    g.DrawLines(pen, new[] { new PointF(11f, 2.5f), new PointF(13.5f, 5f), new PointF(6f, 12.5f), new PointF(2.5f, 13.5f), new PointF(3.5f, 10f), new PointF(11f, 2.5f) });
                    g.DrawLine(pen, 9.2f, 4.3f, 11.7f, 6.8f);
                    break;

                case IndustrialIconKind.Eraser:
                    g.DrawPolygon(pen, new[] { new PointF(8.5f, 3f), new PointF(13.5f, 8f), new PointF(8f, 13.5f), new PointF(3f, 8.5f) });
                    g.DrawLine(pen, 5.8f, 5.8f, 10.8f, 10.8f);
                    g.DrawLine(pen, 3f, 13.5f, 13.5f, 13.5f);
                    break;

                case IndustrialIconKind.Undo:
                    g.DrawArc(pen, 3.5f, 5f, 9.5f, 8f, 235f, 210f);
                    g.DrawLines(pen, new[] { new PointF(3.2f, 3.8f), new PointF(3.2f, 7.6f), new PointF(7f, 7.6f) });
                    break;

                case IndustrialIconKind.TargetWmax:
                    g.DrawEllipse(pen, 3f, 3f, 10f, 10f);
                    g.DrawLine(pen, 8f, 1.5f, 8f, 14.5f);
                    g.DrawLine(pen, 1.5f, 8f, 14.5f, 8f);
                    break;

                case IndustrialIconKind.Reprocess:
                    g.DrawArc(pen, 2.5f, 2.5f, 11f, 11f, 200f, 140f);
                    g.DrawArc(pen, 2.5f, 2.5f, 11f, 11f, 20f, 140f);
                    g.DrawLines(pen, new[] { new PointF(11.5f, 3.5f), new PointF(13.5f, 5.5f), new PointF(10.5f, 6.2f) });
                    g.DrawLines(pen, new[] { new PointF(4.5f, 12.5f), new PointF(2.5f, 10.5f), new PointF(5.5f, 9.8f) });
                    break;

                case IndustrialIconKind.RemoveItem:
                    g.DrawRectangle(pen, 2.5f, 2.5f, 11f, 11f);
                    g.DrawLine(pen, 5.2f, 8f, 10.8f, 8f);
                    break;

                case IndustrialIconKind.TrashClear:
                    g.DrawLine(pen, 2.5f, 4.2f, 13.5f, 4.2f);
                    g.DrawLine(pen, 6f, 2.2f, 10f, 2.2f);
                    g.DrawLines(pen, new[] { new PointF(4f, 4.2f), new PointF(4.8f, 13.5f), new PointF(11.2f, 13.5f), new PointF(12f, 4.2f) });
                    g.DrawLine(pen, 6.8f, 6.8f, 6.8f, 11f);
                    g.DrawLine(pen, 9.2f, 6.8f, 9.2f, 11f);
                    break;

                case IndustrialIconKind.FitWindow:
                    g.DrawLines(pen, new[] { new PointF(2.5f, 5.5f), new PointF(2.5f, 2.5f), new PointF(5.5f, 2.5f) });
                    g.DrawLines(pen, new[] { new PointF(10.5f, 2.5f), new PointF(13.5f, 2.5f), new PointF(13.5f, 5.5f) });
                    g.DrawLines(pen, new[] { new PointF(2.5f, 10.5f), new PointF(2.5f, 13.5f), new PointF(5.5f, 13.5f) });
                    g.DrawLines(pen, new[] { new PointF(10.5f, 13.5f), new PointF(13.5f, 13.5f), new PointF(13.5f, 10.5f) });
                    g.DrawRectangle(pen, 5.5f, 5.5f, 5f, 5f);
                    break;

                case IndustrialIconKind.ZoomOneToOne:
                    g.DrawEllipse(pen, 2.5f, 2.5f, 8.5f, 8.5f);
                    g.DrawLine(pen, 9.8f, 9.8f, 13.8f, 13.8f);
                    g.DrawLine(pen, 6.8f, 5.2f, 6.8f, 8.3f);
                    g.DrawLine(pen, 5.2f, 6.8f, 8.3f, 6.8f);
                    break;

                case IndustrialIconKind.LabelTag:
                    g.DrawPolygon(pen, new[]
                    {
                        new PointF(2.5f, 2.5f), new PointF(8.2f, 2.5f),
                        new PointF(13.8f, 8.1f), new PointF(8.1f, 13.8f), new PointF(2.5f, 8.2f)
                    });
                    g.FillEllipse(fill, 4.7f, 4.7f, 2f, 2f);
                    break;

                case IndustrialIconKind.PanelLeft:
                    g.DrawRectangle(pen, 2f, 2.5f, 12f, 11f);
                    g.DrawLine(pen, 6f, 2.5f, 6f, 13.5f);
                    g.DrawLine(pen, 3.8f, 5.5f, 4.2f, 5.5f);
                    g.DrawLine(pen, 3.8f, 8f, 4.2f, 8f);
                    break;

                case IndustrialIconKind.PanelRight:
                    g.DrawRectangle(pen, 2f, 2.5f, 12f, 11f);
                    g.DrawLine(pen, 10f, 2.5f, 10f, 13.5f);
                    g.DrawLine(pen, 11.8f, 5.5f, 12.2f, 5.5f);
                    g.DrawLine(pen, 11.8f, 8f, 12.2f, 8f);
                    break;

                case IndustrialIconKind.Plus:
                    g.DrawLine(pen, 8f, 3.2f, 8f, 12.8f);
                    g.DrawLine(pen, 3.2f, 8f, 12.8f, 8f);
                    break;

                case IndustrialIconKind.Minus:
                    g.DrawLine(pen, 3.2f, 8f, 12.8f, 8f);
                    break;

                case IndustrialIconKind.Check:
                    g.DrawLines(pen, new[] { new PointF(3.2f, 8.4f), new PointF(6.5f, 11.6f), new PointF(12.8f, 4.6f) });
                    break;
            }

            g.Restore(st);
        }
    }
}
