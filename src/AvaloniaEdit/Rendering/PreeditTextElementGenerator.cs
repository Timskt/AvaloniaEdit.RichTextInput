using System;
using System.Collections.Generic;
using Avalonia;
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

        public void SetPreedit(string text, int? cursorOffset, IReadOnlyList<ImePreeditClause> clauses = null)
        {
            text = string.IsNullOrEmpty(text) ? null : text;
            if (_text == text && CursorOffset == cursorOffset && ReferenceEquals(Clauses, clauses))
                return;

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
        public PreeditTextElement(string text, int? cursorOffset, IReadOnlyList<ImePreeditClause> clauses)
            : base(1, 0)
        {
            Text = string.IsNullOrEmpty(text) ? " " : text;
            CursorOffset = Math.Max(0, Math.Min(cursorOffset ?? Text.Length, Text.Length));
            Clauses = clauses;
        }

        public string Text { get; }

        public int CursorOffset { get; }

        public IReadOnlyList<ImePreeditClause> Clauses { get; }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            if (startVisualColumn != VisualColumn)
                throw new ArgumentOutOfRangeException(nameof(startVisualColumn));

            return new PreeditTextRun(Text, CursorOffset, Clauses, TextRunProperties);
        }

        public override ReadOnlyMemory<char> GetPrecedingText(int visualColumnLimit, ITextRunConstructionContext context)
        {
            return visualColumnLimit > VisualColumn ? Text.AsMemory(0, 1) : ReadOnlyMemory<char>.Empty;
        }
    }

    internal sealed class PreeditTextRun : DrawableTextRun
    {
        private readonly TextLayout _layout;
        private readonly TextLayout _cursorPrefixLayout;
        private readonly int _cursorOffset;
        private readonly IReadOnlyList<ImePreeditClause> _clauses;

        public PreeditTextRun(string text, int cursorOffset, IReadOnlyList<ImePreeditClause> clauses, TextRunProperties properties)
        {
            Text = text.AsMemory();
            Length = 1;
            Properties = properties ?? throw new ArgumentNullException(nameof(properties));
            _cursorOffset = Math.Max(0, Math.Min(cursorOffset, text.Length));
            _clauses = clauses;

            var foreground = Properties.ForegroundBrush ?? Brushes.Black;
            _layout = new TextLayout(
                text,
                Properties.Typeface,
                Properties.FontRenderingEmSize,
                foreground,
                textWrapping: TextWrapping.NoWrap);

            _cursorPrefixLayout = new TextLayout(
                text.Substring(0, _cursorOffset),
                Properties.Typeface,
                Properties.FontRenderingEmSize,
                foreground,
                textWrapping: TextWrapping.NoWrap);
        }

        public override ReadOnlyMemory<char> Text { get; }

        public override TextRunProperties Properties { get; }

        public override int Length { get; }

        public override double Baseline => _layout.Baseline;

        public override Size Size => new Size(
            Math.Max(1, _layout.WidthIncludingTrailingWhitespace),
            Math.Max(1, _layout.Height));

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            _layout.Draw(drawingContext, origin);

            var foreground = Properties.ForegroundBrush ?? Brushes.Black;
            PreeditDecorationRenderer.DrawUnderlines(
                drawingContext, origin, Text.ToString(), _layout, Properties.Typeface,
                Properties.FontRenderingEmSize, foreground, _clauses);

            var cursorX = origin.X + _cursorPrefixLayout.WidthIncludingTrailingWhitespace;
            drawingContext.DrawLine(
                new ImmutablePen(foreground.ToImmutable(), 1),
                new Point(cursorX, origin.Y),
                new Point(cursorX, origin.Y + Size.Height));
        }
    }
}
