// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

#nullable enable

namespace AvaloniaEdit.Rendering
{
    public enum InlineObjectVerticalAlignment
    {
        Baseline,
        Top,
        Center,
        Bottom
    }

    /// <summary>
	/// A inline UIElement in the document.
	/// </summary>
	public class InlineObjectElement : VisualLineElement
	{
		/// <summary>
		/// Gets the inline element that is displayed.
		/// </summary>
		public Control Element { get; }

		public InlineObjectVerticalAlignment VerticalAlignment { get; }

		/// <summary>
		/// Creates a new InlineObjectElement.
		/// </summary>
		/// <param name="documentLength">The length of the element in the document. Must be non-negative.</param>
		/// <param name="element">The element to display.</param>
		public InlineObjectElement(int documentLength, Control element)
			: this(documentLength, element, InlineObjectVerticalAlignment.Baseline)
		{
		}

		public InlineObjectElement(int documentLength, Control element, InlineObjectVerticalAlignment verticalAlignment)
			: base(1, documentLength)
		{
			Element = element ?? throw new ArgumentNullException(nameof(element));
			VerticalAlignment = verticalAlignment;
		}

		/// <inheritdoc/>
		public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));

			return new InlineObjectRun(1, TextRunProperties, Element, VerticalAlignment);
		}
	}

	/// <summary>
	/// A text run with an embedded UIElement.
	/// </summary>
	public class InlineObjectRun : DrawableTextRun
	{
		internal Size DesiredSize;

		/// <summary>
		/// Creates a new InlineObjectRun instance.
		/// </summary>
		/// <param name="length">The length of the TextRun.</param>
		/// <param name="properties">The <see cref="TextRunProperties"/> to use.</param>
		/// <param name="element">The <see cref="Control"/> to display.</param>
		public InlineObjectRun(int length, TextRunProperties? properties, Control element)
			: this(length, properties, element, InlineObjectVerticalAlignment.Baseline)
		{
		}

		public InlineObjectRun(int length, TextRunProperties? properties, Control element, InlineObjectVerticalAlignment verticalAlignment)
		{
			if (length <= 0)
				throw new ArgumentOutOfRangeException(nameof(length), length, "Value must be positive");

			Length = length;
			Properties = properties ?? throw new ArgumentNullException(nameof(properties));
			Element = element ?? throw new ArgumentNullException(nameof(element));
			VerticalAlignment = verticalAlignment;

			DesiredSize = element.DesiredSize;
		}

		/// <summary>
		/// Gets the element displayed by the InlineObjectRun.
		/// </summary>
		public Control Element { get; }

		public InlineObjectVerticalAlignment VerticalAlignment { get; }

		/// <summary>
		/// Gets the VisualLine that contains this object. This property is only available after the object
		/// was added to the text view.
		/// </summary>
		public VisualLine? VisualLine { get; internal set; }

		public override TextRunProperties? Properties { get; }

		public override int Length { get; }

		public override double Baseline
		{
			get
			{
				var properties = Properties;
				if (ReferenceEquals(properties, null))
					return GetElementBaseline();

				var textBaseline = properties.FontRenderingEmSize * 0.8;
				return VerticalAlignment switch
				{
					InlineObjectVerticalAlignment.Top => textBaseline,
					InlineObjectVerticalAlignment.Center => Math.Max(0, textBaseline - properties.FontRenderingEmSize / 2 + DesiredSize.Height / 2),
					InlineObjectVerticalAlignment.Bottom => Math.Max(0, textBaseline - properties.FontRenderingEmSize + DesiredSize.Height),
					_ => GetElementBaseline()
				};
			}
		}

		private double GetElementBaseline()
		{
			double baseline = TextBlock.GetBaselineOffset(Element);
			if (double.IsNaN(baseline))
				baseline = DesiredSize.Height;
			return baseline;
		}

		/// <inheritdoc/>
		public override Size Size => DesiredSize;
		
		/// <inheritdoc/>
		public override void Draw(DrawingContext drawingContext, Point origin)
		{
			// noop
		}
	}
}
