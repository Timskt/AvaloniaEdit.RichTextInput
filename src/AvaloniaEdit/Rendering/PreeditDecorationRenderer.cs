using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Editing;

namespace AvaloniaEdit.Rendering
{
    internal static class PreeditDecorationRenderer
    {
        public static void DrawUnderlines(
            DrawingContext drawingContext,
            Point origin,
            string text,
            TextLayout layout,
            Typeface typeface,
            double fontRenderingEmSize,
            IBrush foreground,
            IReadOnlyList<ImePreeditClause> clauses)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (clauses == null || clauses.Count == 0)
            {
                DrawUnderline(drawingContext, origin.X, origin.X + layout.WidthIncludingTrailingWhitespace,
                    origin.Y + layout.Height - 1, ImePreeditUnderlineStyle.Solid, foreground);
                return;
            }

            foreach (var clause in clauses)
            {
                if (clause == null || clause.UnderlineStyle == ImePreeditUnderlineStyle.None)
                    continue;

                var start = Math.Max(0, Math.Min(clause.Start, text.Length));
                var end = Math.Max(start, Math.Min(clause.End, text.Length));
                if (end <= start)
                    continue;

                var startX = origin.X + MeasurePrefix(text, start, layout, typeface, fontRenderingEmSize, foreground);
                var endX = origin.X + MeasurePrefix(text, end, layout, typeface, fontRenderingEmSize, foreground);
                DrawUnderline(drawingContext, startX, endX, origin.Y + layout.Height - 1,
                    clause.UnderlineStyle, clause.UnderlineBrush ?? foreground);
            }
        }

        public static double MeasurePrefix(string text, int length, TextLayout fullLayout, Typeface typeface, double fontRenderingEmSize, IBrush foreground)
        {
            if (length <= 0)
                return 0;
            if (length >= text.Length)
                return fullLayout.WidthIncludingTrailingWhitespace;

            // TextLayout positions are UTF-16 offsets, matching clause ranges and IME cursor offsets.
            // Reuse the already measured layout instead of constructing a second layout per clause.
            return fullLayout.HitTestTextPosition(length).X;
        }

        private static void DrawUnderline(
            DrawingContext drawingContext,
            double startX,
            double endX,
            double y,
            ImePreeditUnderlineStyle style,
            IBrush brush)
        {
            if (endX <= startX || style == ImePreeditUnderlineStyle.None)
                return;

            var immutableBrush = (brush ?? Brushes.White).ToImmutable();
            switch (style)
            {
                case ImePreeditUnderlineStyle.Thick:
                    drawingContext.DrawLine(new ImmutablePen(immutableBrush, 2),
                        new Point(startX, y), new Point(endX, y));
                    break;
                case ImePreeditUnderlineStyle.Double:
                    drawingContext.DrawLine(new ImmutablePen(immutableBrush, 1),
                        new Point(startX, y - 2), new Point(endX, y - 2));
                    drawingContext.DrawLine(new ImmutablePen(immutableBrush, 1),
                        new Point(startX, y), new Point(endX, y));
                    break;
                case ImePreeditUnderlineStyle.Dotted:
                    DrawPattern(drawingContext, startX, endX, y, immutableBrush, 1, 2, 2);
                    break;
                case ImePreeditUnderlineStyle.Dashed:
                    DrawPattern(drawingContext, startX, endX, y, immutableBrush, 1, 5, 3);
                    break;
                default:
                    drawingContext.DrawLine(new ImmutablePen(immutableBrush, 1),
                        new Point(startX, y), new Point(endX, y));
                    break;
            }
        }

        private static void DrawPattern(
            DrawingContext drawingContext,
            double startX,
            double endX,
            double y,
            IImmutableBrush brush,
            double thickness,
            double dash,
            double gap)
        {
            var pen = new ImmutablePen(brush, thickness);
            var x = startX;
            while (x < endX)
            {
                var segmentEnd = Math.Min(endX, x + dash);
                drawingContext.DrawLine(pen, new Point(x, y), new Point(segmentEnd, y));
                x += dash + gap;
            }
        }
    }
}
