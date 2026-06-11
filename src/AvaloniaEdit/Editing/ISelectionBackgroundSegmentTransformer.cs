using System.Collections.Generic;
using AvaloniaEdit.Document;

namespace AvaloniaEdit.Editing
{
    /// <summary>
    /// Allows editor extensions to customize which selected document ranges receive
    /// the normal text selection background.
    /// </summary>
    public interface ISelectionBackgroundSegmentTransformer
    {
        /// <summary>
        /// Transforms the current selection segments before the selection background is rendered.
        /// </summary>
        IEnumerable<ISegment> TransformSelectionBackgroundSegments(IEnumerable<SelectionSegment> segments);
    }
}
