using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Utils;

namespace AvaloniaEdit.Rendering
{
    /// <summary>
    /// Renders overlay IME preedit text at the caret without modifying the document.
    /// </summary>
    internal sealed class PreeditLayer : Layer
    {
        private const double MinimumUsableWidth = 1;

        private readonly TextArea _textArea;
        private string _preeditText;
        private Rect _caretRect;
        private IBrush _foreground;
        private int? _cursorOffset;
        private IReadOnlyList<ImePreeditClause> _clauses;

        public PreeditLayer(TextArea textArea) : base(textArea.TextView, KnownLayer.Caret)
        {
            _textArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
            IsHitTestVisible = false;
        }

        internal int LastRenderedChunkCount { get; private set; }
        internal IReadOnlyList<Point> LastRenderedChunkOrigins { get; private set; } = Array.Empty<Point>();
        internal IReadOnlyList<int> LastRenderedChunkLengths { get; private set; } = Array.Empty<int>();
        internal int LastRenderedCursorCount { get; private set; }
        internal int LastRenderedBackgroundFillCount { get; private set; }
        internal double? LastBodyBaseline { get; private set; }

        public void SetPreedit(
            string text,
            Rect caretRect,
            IBrush foreground,
            int? cursorOffset = null,
            IReadOnlyList<ImePreeditClause> clauses = null)
        {
            _preeditText = text;
            _caretRect = caretRect;
            _foreground = foreground;
            _cursorOffset = cursorOffset;
            _clauses = clauses;
            InvalidateVisual();
        }

        public void Clear()
        {
            if (_preeditText == null && _clauses == null)
                return;

            _preeditText = null;
            _cursorOffset = null;
            _clauses = null;
            ResetRenderDiagnostics();
            InvalidateVisual();
        }

        public override void Render(DrawingContext drawingContext)
        {
            base.Render(drawingContext);
            ResetRenderDiagnostics();

            if (string.IsNullOrEmpty(_preeditText))
                return;

            var textView = TextView;
            if (textView?.Document == null)
                return;

            var viewportWidth = Math.Max(0, Bounds.Width);
            if (viewportWidth <= 0)
                viewportWidth = Math.Max(0, textView.Bounds.Width);
            if (viewportWidth <= 0)
                return;

            // The committed text starts at caret X. Starting at caret.Right shifts the preedit by one caret width.
            var startX = _caretRect.X - textView.HorizontalOffset;
            var fallbackLineTop = _caretRect.Y - textView.VerticalOffset;
            var metrics = TryGetCaretLineMetrics(textView);
            LastBodyBaseline = metrics?.Baseline;

            var typeface = textView.CreateTypeface();
            var fontRenderingEmSize = textView.GetValue(TemplatedControl.FontSizeProperty);
            var foreground = _foreground
                ?? textView.GetValue(TemplatedControl.ForegroundProperty) as IBrush
                ?? Brushes.White;
            var background = FindEditorBackground();
            var cursorPen = new ImmutablePen(foreground.ToImmutable(), 2);
            var effectiveCursorOffset = Math.Max(0,
                Math.Min(_cursorOffset ?? _preeditText.Length, _preeditText.Length));

            var chunkStart = 0;
            var originX = startX;
            var lineTop = metrics?.LineTop ?? fallbackLineTop;
            var lineHeight = Math.Max(1, metrics?.LineHeight ?? textView.DefaultLineHeight);
            var baseline = metrics?.Baseline;
            var origins = new List<Point>();
            var chunkLengths = new List<int>();
            var cursorCount = 0;
            var backgroundFillCount = 0;

            // A hard line break can consume text without producing a drawable chunk, and a
            // right-edge retry moves once before consuming text. Bound iterations independently
            // from the diagnostics count while still guaranteeing forward progress.
            var iterationCount = 0;
            var maxIterations = Math.Max(2, _preeditText.Length * 2 + 2);
            while (chunkStart < _preeditText.Length && iterationCount++ < maxIterations)
            {
                var rest = _preeditText.Substring(chunkStart);
                var hardBreakIndex = FindHardLineBreak(rest, out var hardBreakLength);
                var visibleLength = hardBreakIndex >= 0 ? hardBreakIndex : rest.Length;

                if (visibleLength == 0)
                {
                    // The cursor at the start of a newline belongs to the line before it. Cursor
                    // offsets inside CRLF are normalized to the first position after the pair.
                    if (cursorCount == 0 && effectiveCursorOffset == chunkStart)
                    {
                        drawingContext.DrawLine(cursorPen,
                            new Point(originX, lineTop),
                            new Point(originX, lineTop + lineHeight));
                        cursorCount++;
                    }

                    var breakEnd = chunkStart + hardBreakLength;
                    if (effectiveCursorOffset > chunkStart && effectiveCursorOffset < breakEnd)
                        effectiveCursorOffset = breakEnd;
                    chunkStart = breakEnd;
                    MoveToNextLine(ref originX, ref lineTop, ref baseline, lineHeight);
                    continue;
                }

                var available = viewportWidth - originX;
                if (available < MinimumUsableWidth && originX > 0)
                {
                    MoveToNextLine(ref originX, ref lineTop, ref baseline, lineHeight);
                    continue;
                }

                var visibleText = rest.Substring(0, visibleLength);
                if (originX > 0 && !FirstTextElementFits(
                    visibleText,
                    typeface,
                    fontRenderingEmSize,
                    foreground,
                    available))
                {
                    MoveToNextLine(ref originX, ref lineTop, ref baseline, lineHeight);
                    continue;
                }

                var measuredLength = MeasureFirstLineLength(
                    visibleText,
                    typeface,
                    fontRenderingEmSize,
                    foreground,
                    Math.Max(MinimumUsableWidth, available));
                var take = Math.Min(visibleLength, ClampToTextElementBoundary(visibleText, measuredLength));
                var chunk = visibleText.Substring(0, take);
                var chunkLayout = new TextLayout(
                    chunk,
                    typeface,
                    fontRenderingEmSize,
                    foreground,
                    textWrapping: TextWrapping.NoWrap);

                var originY = baseline.HasValue ? baseline.Value - chunkLayout.Baseline : lineTop;
                var origin = new Point(originX, originY);
                origins.Add(origin);
                chunkLengths.Add(take);

                var width = Math.Max(1, chunkLayout.WidthIncludingTrailingWhitespace);
                var fillTop = Math.Min(lineTop, origin.Y);
                var fillBottom = Math.Max(lineTop + lineHeight, origin.Y + chunkLayout.Height);
                if (background != null)
                {
                    drawingContext.FillRectangle(background,
                        new Rect(origin.X, fillTop, Math.Min(width, Math.Max(0, viewportWidth - origin.X)),
                            Math.Max(1, fillBottom - fillTop)));
                    backgroundFillCount++;
                }

                chunkLayout.Draw(drawingContext, origin);
                var localClauses = SliceClauses(_clauses, chunkStart, take);
                PreeditDecorationRenderer.DrawUnderlines(
                    drawingContext, origin, chunk, chunkLayout, typeface,
                    fontRenderingEmSize, foreground, localClauses);

                var chunkEnd = chunkStart + take;
                var reachesHardBreak = hardBreakIndex >= 0 && take == visibleLength;
                var isLastChunk = chunkEnd >= _preeditText.Length;
                if (cursorCount == 0
                    && effectiveCursorOffset >= chunkStart
                    && ((isLastChunk || reachesHardBreak)
                        ? effectiveCursorOffset <= chunkEnd
                        : effectiveCursorOffset < chunkEnd))
                {
                    var localCursorOffset = effectiveCursorOffset - chunkStart;
                    var cursorX = origin.X + PreeditDecorationRenderer.MeasurePrefix(
                        chunk, localCursorOffset, chunkLayout, typeface, fontRenderingEmSize, foreground);
                    drawingContext.DrawLine(cursorPen,
                        new Point(cursorX, origin.Y),
                        new Point(cursorX, origin.Y + chunkLayout.Height));
                    cursorCount++;
                }

                chunkStart = chunkEnd;
                if (reachesHardBreak)
                {
                    var breakEnd = chunkStart + hardBreakLength;
                    if (effectiveCursorOffset > chunkStart && effectiveCursorOffset < breakEnd)
                        effectiveCursorOffset = breakEnd;
                    chunkStart = breakEnd;
                }

                MoveToNextLine(ref originX, ref lineTop, ref baseline, lineHeight);
            }

            // A trailing hard break leaves the cursor on an empty row, so there is no text layout
            // from which to obtain a cursor rectangle. Draw it from the row metrics instead.
            if (cursorCount == 0
                && chunkStart == _preeditText.Length
                && effectiveCursorOffset == chunkStart)
            {
                drawingContext.DrawLine(cursorPen,
                    new Point(originX, lineTop),
                    new Point(originX, lineTop + lineHeight));
                cursorCount++;
            }

            LastRenderedChunkCount = origins.Count;
            LastRenderedChunkOrigins = origins;
            LastRenderedChunkLengths = chunkLengths;
            LastRenderedCursorCount = cursorCount;
            LastRenderedBackgroundFillCount = backgroundFillCount;
        }

        private void ResetRenderDiagnostics()
        {
            LastRenderedChunkCount = 0;
            LastRenderedChunkOrigins = Array.Empty<Point>();
            LastRenderedChunkLengths = Array.Empty<int>();
            LastRenderedCursorCount = 0;
            LastRenderedBackgroundFillCount = 0;
            LastBodyBaseline = null;
        }

        private static int FindHardLineBreak(string text, out int breakLength)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    breakLength = i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                    return i;
                }

                if (text[i] == '\n')
                {
                    breakLength = 1;
                    return i;
                }
            }

            breakLength = 0;
            return -1;
        }

        private static bool FirstTextElementFits(
            string text,
            Typeface typeface,
            double fontRenderingEmSize,
            IBrush foreground,
            double availableWidth)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            var firstElement = StringInfo.GetNextTextElement(text);
            var layout = new TextLayout(
                firstElement,
                typeface,
                fontRenderingEmSize,
                foreground,
                textWrapping: TextWrapping.NoWrap);
            return layout.WidthIncludingTrailingWhitespace <= Math.Max(0, availableWidth);
        }

        private static int MeasureFirstLineLength(
            string text,
            Typeface typeface,
            double fontRenderingEmSize,
            IBrush foreground,
            double maxWidth)
        {
            var measured = new TextLayout(
                text,
                typeface,
                fontRenderingEmSize,
                foreground,
                textWrapping: TextWrapping.Wrap,
                maxWidth: maxWidth);
            return measured.TextLines.Count > 0 ? measured.TextLines[0].Length : text.Length;
        }

        private static int ClampToTextElementBoundary(string text, int requestedLength)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            if (requestedLength >= text.Length)
                return text.Length;

            var elementStarts = StringInfo.ParseCombiningCharacters(text);
            if (elementStarts.Length == 0)
                return text.Length;

            var safeLength = 0;
            foreach (var start in elementStarts)
            {
                if (start > requestedLength)
                    break;
                safeLength = start;
            }

            return safeLength > 0
                ? safeLength
                : StringInfo.GetNextTextElement(text).Length;
        }

        private static void MoveToNextLine(
            ref double originX,
            ref double lineTop,
            ref double? baseline,
            double lineHeight)
        {
            originX = 0;
            lineTop += lineHeight;
            if (baseline.HasValue)
                baseline += lineHeight;
        }

        internal static IReadOnlyList<ImePreeditClause> SliceClauses(
            IReadOnlyList<ImePreeditClause> clauses,
            int chunkStart,
            int chunkLength)
        {
            if (clauses == null || clauses.Count == 0 || chunkLength <= 0)
                return null;

            var chunkEnd = chunkStart + chunkLength;
            List<ImePreeditClause> result = null;
            foreach (var clause in clauses)
            {
                var start = Math.Max(chunkStart, clause.Start);
                var end = Math.Min(chunkEnd, clause.End);
                if (end <= start)
                    continue;

                result ??= new List<ImePreeditClause>();
                result.Add(new ImePreeditClause(
                    start - chunkStart,
                    end - start,
                    clause.UnderlineStyle,
                    clause.UnderlineBrush));
            }

            return result;
        }

        private CaretLineMetrics? TryGetCaretLineMetrics(TextView textView)
        {
            try
            {
                var caret = _textArea?.Caret;
                if (caret == null)
                    return null;

                // Rendering must not construct visual lines: doing so can re-enter layout. If the caret
                // line is not available yet, the caller safely falls back to the caret rectangle.
                var visualLine = textView.GetVisualLine(caret.Line);
                if (visualLine == null)
                    return null;
                var textLine = visualLine.GetTextLine(caret.VisualColumn, caret.Position.IsAtEndOfLine);
                var lineTop = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineTop)
                    - textView.VerticalOffset;
                var lineBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineBottom)
                    - textView.VerticalOffset;
                var bodyBaseline = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.Baseline)
                    - textView.VerticalOffset;
                return new CaretLineMetrics(lineTop, Math.Max(1, lineBottom - lineTop), bodyBaseline);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private IBrush FindEditorBackground()
        {
            Visual visual = _textArea;
            while (visual != null)
            {
                var brush = (visual as TemplatedControl)?.Background
                    ?? (visual as Border)?.Background
                    ?? (visual as Panel)?.Background;
                if (brush != null && !IsFullyTransparent(brush))
                    return brush;

                visual = visual.GetVisualParent();
            }

            return null;
        }

        private static bool IsFullyTransparent(IBrush brush)
            => brush is ISolidColorBrush solid && solid.Color.A == 0;

        private readonly struct CaretLineMetrics
        {
            public CaretLineMetrics(double lineTop, double lineHeight, double baseline)
            {
                LineTop = lineTop;
                LineHeight = lineHeight;
                Baseline = baseline;
            }

            public double LineTop { get; }
            public double LineHeight { get; }
            public double Baseline { get; }
        }
    }
}
