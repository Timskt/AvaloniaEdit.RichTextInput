using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Editing;

namespace AvaloniaEdit.Rendering
{
    internal sealed class PreeditTextElementGenerator : VisualLineElementGenerator
    {
        private readonly TextArea _textArea;
        private string _text;

        public PreeditTextElementGenerator(TextArea textArea)
        {
            _textArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
        }

        public bool HasPreedit => !string.IsNullOrEmpty(_text);

        public string Text => _text;

        public int? CursorOffset { get; private set; }

        public IReadOnlyList<ImePreeditClause> Clauses { get; private set; }

        public void SetPreedit(
            string text,
            int? cursorOffset,
            IReadOnlyList<ImePreeditClause> clauses = null,
            bool forceRedraw = false)
        {
            text = string.IsNullOrEmpty(text) ? null : text;
            if (_text == text && CursorOffset == cursorOffset && ReferenceEquals(Clauses, clauses))
            {
                // The preedit generator is positioned from the current caret. A caret move
                // can therefore require a new visual-line build even when the IME payload did
                // not change. This is especially important for keyboard/programmatic caret moves;
                // pointer clicks are handled separately by TextArea's preedit commit path.
                if (forceRedraw && _text != null)
                    _textArea.TextView.Redraw();
                return;
            }

            _text = text;
            CursorOffset = cursorOffset;
            Clauses = clauses;
            _textArea.TextView.Redraw();
        }

        public void Clear(bool redraw = true)
        {
            if (_text == null && CursorOffset == null && Clauses == null)
                return;

            _text = null;
            CursorOffset = null;
            Clauses = null;
            if (redraw)
                _textArea.TextView.Redraw();
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            if (string.IsNullOrEmpty(_text) || _textArea.Document == null)
                return -1;

            var offset = Math.Max(0, Math.Min(_textArea.Caret.Offset, _textArea.Document.TextLength));
            return offset >= startOffset ? offset : -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            return !string.IsNullOrEmpty(_text) && offset == _textArea.Caret.Offset
                ? new PreeditTextElement(_text, CursorOffset, Clauses)
                : null;
        }
    }

    internal sealed class PreeditTextElement : VisualLineElement
    {
        private readonly int _renderedCursorOffset;
        private readonly int _cursorRunStart;
        private readonly int _cursorRunLength;
        private readonly IReadOnlyList<PreeditTextSegment> _segments;

        public PreeditTextElement(string text, int? cursorOffset, IReadOnlyList<ImePreeditClause> clauses)
            : base(GetVisualLength(text), 0)
        {
            Text = string.IsNullOrEmpty(text) ? " " : text;
            CursorOffset = Math.Max(0, Math.Min(cursorOffset ?? Text.Length, Text.Length));
            Clauses = clauses;
            _renderedCursorOffset = NormalizeCursorOffset(Text, CursorOffset);
            (_cursorRunStart, _cursorRunLength) = GetCursorRunRange(Text, _renderedCursorOffset);
            _segments = CreateSegments(Text, Clauses);
        }

        public string Text { get; }

        public int CursorOffset { get; }

        public IReadOnlyList<ImePreeditClause> Clauses { get; }

        internal int RenderedCursorOffset => _renderedCursorOffset;

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            var textOffset = startVisualColumn - VisualColumn;
            if (textOffset < 0 || textOffset >= VisualLength)
                throw new ArgumentOutOfRangeException(nameof(startVisualColumn));

            if (textOffset == _cursorRunStart)
            {
                var clause = FindClause(Clauses, textOffset);
                return new PreeditCursorTextRun(
                    Text.AsMemory(textOffset, _cursorRunLength),
                    _renderedCursorOffset - textOffset,
                    clause,
                    TextRunProperties);
            }

            var segment = FindSegment(textOffset);
            var length = segment.End - textOffset;

            // The cursor host is one complete grapheme cluster rendered by a drawable run.
            // Keep ordinary TextCharacters from consuming that cluster so the cursor can be
            // painted exactly once without making the full composition an unbreakable object.
            if (textOffset < _cursorRunStart)
                length = Math.Min(length, _cursorRunStart - textOffset);

            var properties = CreateProperties(segment.Clause);
            return new TextCharacters(Text.AsMemory(textOffset, length), properties);
        }

        public override ReadOnlyMemory<char> GetPrecedingText(int visualColumnLimit, ITextRunConstructionContext context)
        {
            var length = Math.Max(0, Math.Min(visualColumnLimit - VisualColumn, Text.Length));
            return Text.AsMemory(0, length);
        }

        private static int GetVisualLength(string text)
            => Math.Max(1, string.IsNullOrEmpty(text) ? 1 : text.Length);

        private static int NormalizeCursorOffset(string text, int cursorOffset)
        {
            if (cursorOffset <= 0 || cursorOffset >= text.Length)
                return Math.Max(0, Math.Min(cursorOffset, text.Length));

            var starts = StringInfo.ParseCombiningCharacters(text);
            foreach (var start in starts)
            {
                if (start == cursorOffset)
                    return cursorOffset;
                if (start > cursorOffset)
                    return start;
            }

            return text.Length;
        }

        private static (int Start, int Length) GetCursorRunRange(string text, int cursorOffset)
        {
            var starts = StringInfo.ParseCombiningCharacters(text);
            if (starts.Length == 0)
                return (0, text.Length);

            var runIndex = starts.Length - 1;
            if (cursorOffset < text.Length)
            {
                for (var i = 0; i < starts.Length; i++)
                {
                    if (starts[i] == cursorOffset)
                    {
                        runIndex = i;
                        break;
                    }
                }
            }

            var start = starts[runIndex];
            var end = runIndex + 1 < starts.Length ? starts[runIndex + 1] : text.Length;
            return (start, end - start);
        }

        private static IReadOnlyList<PreeditTextSegment> CreateSegments(
            string text,
            IReadOnlyList<ImePreeditClause> clauses)
        {
            var starts = StringInfo.ParseCombiningCharacters(text);
            var segments = new List<PreeditTextSegment>(Math.Max(1, starts.Length));
            var segmentStart = 0;
            ImePreeditClause segmentClause = null;

            for (var i = 0; i < starts.Length; i++)
            {
                var textElementStart = starts[i];
                var clause = FindClause(clauses, textElementStart);
                if (i == 0)
                {
                    segmentStart = textElementStart;
                    segmentClause = clause;
                    continue;
                }

                if (HaveEquivalentDecoration(segmentClause, clause))
                    continue;

                segments.Add(new PreeditTextSegment(segmentStart, textElementStart - segmentStart, segmentClause));
                segmentStart = textElementStart;
                segmentClause = clause;
            }

            if (segmentStart < text.Length)
                segments.Add(new PreeditTextSegment(segmentStart, text.Length - segmentStart, segmentClause));

            if (segments.Count == 0)
                segments.Add(new PreeditTextSegment(0, text.Length, null));

            return segments;
        }

        private PreeditTextSegment FindSegment(int textOffset)
        {
            foreach (var segment in _segments)
            {
                if (textOffset >= segment.Start && textOffset < segment.End)
                    return segment;
            }

            throw new ArgumentOutOfRangeException(nameof(textOffset));
        }

        private static ImePreeditClause FindClause(IReadOnlyList<ImePreeditClause> clauses, int textOffset)
        {
            if (clauses == null || clauses.Count == 0)
                return null;

            foreach (var clause in clauses)
            {
                if (clause != null && textOffset >= clause.Start && textOffset < clause.End)
                    return clause;
            }

            return null;
        }

        private static bool HaveEquivalentDecoration(ImePreeditClause left, ImePreeditClause right)
        {
            var leftStyle = left?.UnderlineStyle ?? ImePreeditUnderlineStyle.Solid;
            var rightStyle = right?.UnderlineStyle ?? ImePreeditUnderlineStyle.Solid;
            return leftStyle == rightStyle
                && ReferenceEquals(left?.UnderlineBrush, right?.UnderlineBrush);
        }

        private TextRunProperties CreateProperties(ImePreeditClause clause)
        {
            var properties = TextRunProperties.Clone();
            var style = clause?.UnderlineStyle ?? ImePreeditUnderlineStyle.Solid;
            if (style == ImePreeditUnderlineStyle.None)
                return properties;

            var brush = clause?.UnderlineBrush ?? properties.ForegroundBrush ?? Brushes.Black;
            properties.SetTextDecorations(CreateTextDecorations(style, brush));
            return properties;
        }

        private static TextDecorationCollection CreateTextDecorations(
            ImePreeditUnderlineStyle style,
            IBrush brush)
        {
            var result = new TextDecorationCollection
            {
                CreateTextDecoration(style, brush, 0)
            };

            if (style == ImePreeditUnderlineStyle.Double)
                result.Add(CreateTextDecoration(ImePreeditUnderlineStyle.Solid, brush, -2));

            return result;
        }

        private static TextDecoration CreateTextDecoration(
            ImePreeditUnderlineStyle style,
            IBrush brush,
            double pixelOffset)
        {
            var decoration = new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Stroke = brush,
                StrokeThickness = style == ImePreeditUnderlineStyle.Thick ? 2 : 1,
                StrokeThicknessUnit = TextDecorationUnit.Pixel
            };

            if (pixelOffset != 0)
            {
                decoration.StrokeOffset = pixelOffset;
                decoration.StrokeOffsetUnit = TextDecorationUnit.Pixel;
            }

            if (style == ImePreeditUnderlineStyle.Dotted)
            {
                decoration.StrokeDashArray = new AvaloniaList<double> { 0, 2 };
                decoration.StrokeLineCap = PenLineCap.Round;
            }
            else if (style == ImePreeditUnderlineStyle.Dashed)
            {
                decoration.StrokeDashArray = new AvaloniaList<double> { 5, 3 };
            }

            return decoration;
        }

        private readonly struct PreeditTextSegment
        {
            public PreeditTextSegment(int start, int length, ImePreeditClause clause)
            {
                Start = start;
                Length = length;
                Clause = clause;
            }

            public int Start { get; }
            public int Length { get; }
            public int End => Start + Length;
            public ImePreeditClause Clause { get; }
        }
    }

    internal sealed class PreeditCursorTextRun : DrawableTextRun
    {
        private readonly TextLayout _layout;
        private readonly TextLayout _cursorPrefixLayout;
        private readonly int _cursorOffset;
        private readonly IReadOnlyList<ImePreeditClause> _clauses;

        public PreeditCursorTextRun(
            ReadOnlyMemory<char> text,
            int cursorOffset,
            ImePreeditClause clause,
            TextRunProperties properties)
        {
            Text = text;
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
            _cursorOffset = Math.Max(0, Math.Min(cursorOffset, text.Length));
            _clauses = CreateLocalClauses(clause, text.Length);

            var foreground = Properties.ForegroundBrush ?? Brushes.Black;
            var value = text.ToString();
            _layout = new TextLayout(
                value,
                Properties.Typeface,
                Properties.FontRenderingEmSize,
                foreground,
                textWrapping: TextWrapping.NoWrap);
            _cursorPrefixLayout = new TextLayout(
                value.Substring(0, _cursorOffset),
                Properties.Typeface,
                Properties.FontRenderingEmSize,
                foreground,
                textWrapping: TextWrapping.NoWrap);
        }

        public override ReadOnlyMemory<char> Text { get; }

        public override TextRunProperties Properties { get; }

        public override int Length => Text.Length;

        public override double Baseline => _layout.Baseline;

        public override Size Size => new Size(
            Math.Max(0, _layout.WidthIncludingTrailingWhitespace),
            Math.Max(1, _layout.Height));

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            _layout.Draw(drawingContext, origin);

            var foreground = Properties.ForegroundBrush ?? Brushes.Black;
            PreeditDecorationRenderer.DrawUnderlines(
                drawingContext,
                origin,
                Text.ToString(),
                _layout,
                Properties.Typeface,
                Properties.FontRenderingEmSize,
                foreground,
                _clauses);

            var cursorX = origin.X + _cursorPrefixLayout.WidthIncludingTrailingWhitespace;
            drawingContext.DrawLine(
                new ImmutablePen(foreground.ToImmutable(), 1),
                new Point(cursorX, origin.Y),
                new Point(cursorX, origin.Y + Size.Height));
        }

        private static IReadOnlyList<ImePreeditClause> CreateLocalClauses(
            ImePreeditClause clause,
            int textLength)
        {
            if (clause == null)
                return null;

            return new[]
            {
                new ImePreeditClause(
                    0,
                    textLength,
                    clause.UnderlineStyle,
                    clause.UnderlineBrush)
            };
        }
    }
}
