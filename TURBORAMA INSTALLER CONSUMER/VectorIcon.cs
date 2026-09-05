using System.Drawing;
using System.Drawing.Drawing2D;

namespace TurboRama.Next
{
    internal enum Glyph { None, Home, Scan, Grid, CheckList, ArrowRight, ArrowLeft, Monitor, Gamepad, Refresh, Close, Spark }

    // Original 24-unit paths. No icon font, raster scaling or external asset required.
    internal static class VectorIcon
    {
        public static void Draw(Graphics graphics, Glyph icon, RectangleF bounds, Color color)
        {
            if (icon == Glyph.None || bounds.Width <= 0 || bounds.Height <= 0) return;
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            using (Pen pen = new Pen(color, 1.65f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (icon)
                {
                    case Glyph.Home:
                        graphics.DrawLines(pen, new[] { new Point(3, 11), new Point(12, 4), new Point(21, 11) });
                        graphics.DrawLines(pen, new[] { new Point(6, 10), new Point(6, 20), new Point(10, 20), new Point(10, 14), new Point(14, 14), new Point(14, 20), new Point(18, 20), new Point(18, 10) }); break;
                    case Glyph.Scan:
                        graphics.DrawEllipse(pen, 5, 5, 14, 14);
                        graphics.DrawEllipse(pen, 9, 9, 6, 6);
                        graphics.DrawLine(pen, 12, 1, 12, 5); graphics.DrawLine(pen, 19, 12, 23, 12);
                        graphics.DrawLine(pen, 12, 19, 12, 23); graphics.DrawLine(pen, 1, 12, 5, 12); break;
                    case Glyph.Grid:
                        foreach (int x in new[] { 4, 14 }) foreach (int y in new[] { 4, 14 })
                            using (GraphicsPath tile = Shape.Round(new Rectangle(x, y, 6, 6), 2)) graphics.DrawPath(pen, tile); break;
                    case Glyph.CheckList:
                        graphics.DrawLines(pen, new[] { new Point(3, 7), new Point(5, 9), new Point(8, 5) });
                        graphics.DrawLines(pen, new[] { new Point(3, 16), new Point(5, 18), new Point(8, 14) });
                        graphics.DrawLine(pen, 12, 7, 21, 7); graphics.DrawLine(pen, 12, 16, 21, 16); break;
                    case Glyph.ArrowRight:
                        graphics.DrawLine(pen, 4, 12, 20, 12); graphics.DrawLines(pen, new[] { new Point(14, 6), new Point(20, 12), new Point(14, 18) }); break;
                    case Glyph.ArrowLeft:
                        graphics.DrawLine(pen, 4, 12, 20, 12); graphics.DrawLines(pen, new[] { new Point(10, 6), new Point(4, 12), new Point(10, 18) }); break;
                    case Glyph.Monitor:
                        using (GraphicsPath screen = Shape.Round(new Rectangle(3, 4, 18, 13), 2)) graphics.DrawPath(pen, screen);
                        graphics.DrawLine(pen, 12, 17, 12, 21); graphics.DrawLine(pen, 8, 21, 16, 21); break;
                    case Glyph.Gamepad:
                        using (GraphicsPath pad = new GraphicsPath())
                        {
                            pad.AddBezier(7, 6, 3, 5, -1, 21, 4, 19); pad.AddLine(4, 19, 8, 15);
                            pad.AddLine(8, 15, 16, 15); pad.AddBezier(16, 15, 21, 23, 25, 21, 20, 8);
                            pad.AddBezier(20, 8, 19, 5, 18, 6, 7, 6); pad.CloseFigure(); graphics.DrawPath(pen, pad);
                        }
                        graphics.DrawLine(pen, 5, 10, 10, 10); graphics.DrawLine(pen, 7.5f, 7.5f, 7.5f, 12.5f);
                        graphics.DrawEllipse(pen, 16, 8, 1, 1); graphics.DrawEllipse(pen, 18, 11, 1, 1); break;
                    case Glyph.Refresh:
                        graphics.DrawArc(pen, 4, 4, 16, 16, 35, 290);
                        graphics.DrawLines(pen, new[] { new Point(19, 3), new Point(19, 9), new Point(13, 9) }); break;
                    case Glyph.Close:
                        graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 18, 6, 6, 18); break;
                    case Glyph.Spark:
                        graphics.DrawPolygon(pen, new[] { new Point(12, 2), new Point(15, 9), new Point(22, 12), new Point(15, 15), new Point(12, 22), new Point(9, 15), new Point(2, 12), new Point(9, 9) }); break;
                }
            }
            graphics.Restore(state);
        }
    }
}
