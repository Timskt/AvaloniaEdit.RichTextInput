using Avalonia;
using AvaloniaEdit.Document;
using Avalonia.Media;

namespace AvaloniaEdit.Rendering
{
    /// <summary>
    /// Provides context for current-line highlight styling.
    /// </summary>
    public sealed class CurrentLineHighlightContext
    {
        internal CurrentLineHighlightContext(
            TextView textView,
            VisualLine visualLine,
            int lineNumber,
            Rect lineRectangle,
            double textWidth)
        {
            TextView = textView;
            VisualLine = visualLine;
            LineNumber = lineNumber;
            Line = visualLine?.FirstDocumentLine;
            LineRectangle = lineRectangle;
            TextWidth = textWidth;
        }

        public TextView TextView { get; }

        public VisualLine VisualLine { get; }

        public DocumentLine Line { get; }

        public int LineNumber { get; }

        public Rect LineRectangle { get; }

        public double TextWidth { get; }
    }

    /// <summary>
    /// Describes how the current-line highlight rectangle should be drawn.
    /// </summary>
    public sealed class CurrentLineHighlightStyle
    {
        public IBrush BackgroundBrush { get; set; }

        public IPen BorderPen { get; set; }

        public Thickness Margin { get; set; }

        public CornerRadius CornerRadius { get; set; }

        public bool ExtendToViewportWidth { get; set; } = true;

        public double MinWidth { get; set; }
    }
}
