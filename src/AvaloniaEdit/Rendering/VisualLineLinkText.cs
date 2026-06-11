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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace AvaloniaEdit.Rendering
{
    /// <summary>
    /// VisualLineElement that represents a piece of text and is a clickable link.
    /// </summary>
    public class VisualLineLinkText : VisualLineText
    {
        static VisualLineLinkText()
        {
            OpenUriEvent.AddClassHandler<Window>(ExecuteOpenUriEventHandler);
        }

        /// <summary>
        /// Routed event that should navigate to uri when the link is clicked.
        /// </summary>
        public static RoutedEvent<OpenUriRoutedEventArgs> OpenUriEvent { get; } = RoutedEvent.Register<VisualLineText,OpenUriRoutedEventArgs>(nameof(OpenUriEvent), RoutingStrategies.Bubble);

        /// <summary>
        /// Gets/Sets the URL that is navigated to when the link is clicked.
        /// </summary>
        public Uri NavigateUri { get; set; }

        /// <summary>
        /// Gets/Sets the original text matched as a link.
        /// </summary>
        public string MatchedText { get; set; }

        /// <summary>
        /// Gets/Sets an application-defined link kind, for example "url", "email", "ip" or "internal".
        /// </summary>
        public string LinkKind { get; set; }

        /// <summary>
        /// Gets/Sets the window name where the URL will be opened.
        /// </summary>
        public string TargetName { get; set; }

        /// <summary>
        /// Gets/Sets whether the user needs to press Control to click the link.
        /// The default value is true.
        /// </summary>
        public bool RequireControlModifierForClick { get; set; }

        /// <summary>
        /// Creates a visual line text element with the specified length.
        /// It uses the <see cref="ITextRunConstructionContext.VisualLine"/> and its
        /// <see cref="VisualLineElement.RelativeTextOffset"/> to find the actual text string.
        /// </summary>
        public VisualLineLinkText(VisualLine parentVisualLine, int length) : base(parentVisualLine, length)
        {
            RequireControlModifierForClick = true;
        }

		/// <inheritdoc/>
		public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
		{
			var style = context.TextView.GetLinkTextStyle(new LinkTextStyleContext(
				context.TextView,
				NavigateUri,
				MatchedText,
				LinkKind,
				this));

			if (style.ForegroundBrush != null)
				this.TextRunProperties.SetForegroundBrush(style.ForegroundBrush);
			if (style.BackgroundBrush != null)
				this.TextRunProperties.SetBackgroundBrush(style.BackgroundBrush);
			if (style.Underline == true)
				this.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
			return base.CreateTextRun(startVisualColumn, context);
		}

        /// <summary>
        /// Gets whether the link is currently clickable.
        /// </summary>
        /// <remarks>Returns true when control is pressed; or when
        /// <see cref="RequireControlModifierForClick"/> is disabled.</remarks>
        protected virtual bool LinkIsClickable(KeyModifiers modifiers)
        {
            if (NavigateUri == null)
                return false;
            if (RequireControlModifierForClick)
                return modifiers.HasFlag(KeyModifiers.Control);
            return true;
        }

        /// <inheritdoc/>
        protected internal override void OnQueryCursor(PointerEventArgs e)
        {
            if (LinkIsClickable(e.KeyModifiers))
            {
                if(e.Source is InputElement inputElement)
                {
                    inputElement.Cursor = new Cursor(StandardCursorType.Hand);
                }
                e.Handled = true;
            }
        }

        /// <inheritdoc/>
        protected internal override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.Handled && LinkIsClickable(e.KeyModifiers))
            {
                var linkClickedArgs = new LinkTextClickedEventArgs(
                    NavigateUri,
                    MatchedText,
                    LinkKind,
                    TargetName,
                    e);

                ParentVisualLine.TextView.RaiseLinkTextClicked(linkClickedArgs);
                if (linkClickedArgs.Handled)
                {
                    e.Handled = true;
                    return;
                }

                var eventArgs = new OpenUriRoutedEventArgs(NavigateUri, MatchedText, LinkKind, TargetName) { RoutedEvent = OpenUriEvent };

                if(e.Source is Interactive interactive)
                {
                    interactive.RaiseEvent(eventArgs);
                }

                e.Handled = true;
            }
        }

        /// <inheritdoc/>
        protected override VisualLineText CreateInstance(int length)
        {
            return new VisualLineLinkText(ParentVisualLine, length)
            {
                NavigateUri = NavigateUri,
                MatchedText = MatchedText,
                LinkKind = LinkKind,
                TargetName = TargetName,
                RequireControlModifierForClick = RequireControlModifierForClick
            };
        }

        private static void ExecuteOpenUriEventHandler(Window window, OpenUriRoutedEventArgs arg)
        {
            var url = arg.Uri.ToString();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // Process.Start can throw several errors (not all of them documented),
                // just ignore all of them.
            }
        }
    }

    public sealed class OpenUriRoutedEventArgs : RoutedEventArgs
    {
        public Uri Uri { get; }

        public string Text { get; }

        public string LinkKind { get; }

        public string TargetName { get; }

        public OpenUriRoutedEventArgs(Uri uri)
            : this(uri, null, null, null)
        {
        }

        public OpenUriRoutedEventArgs(Uri uri, string text, string linkKind, string targetName)
        {
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            Text = text;
            LinkKind = linkKind;
            TargetName = targetName;
        }
    }

    public sealed class LinkTextStyle
    {
        public IBrush ForegroundBrush { get; set; }

        public IBrush BackgroundBrush { get; set; }

        public bool? Underline { get; set; }
    }

    public sealed class LinkTextStyleContext
    {
        public LinkTextStyleContext(TextView textView, Uri uri, string text, string linkKind, VisualLineLinkText linkText)
        {
            TextView = textView ?? throw new ArgumentNullException(nameof(textView));
            Uri = uri;
            Text = text;
            LinkKind = linkKind;
            LinkText = linkText ?? throw new ArgumentNullException(nameof(linkText));
        }

        public TextView TextView { get; }

        public Uri Uri { get; }

        public string Text { get; }

        public string LinkKind { get; }

        public VisualLineLinkText LinkText { get; }
    }

    public sealed class LinkTextClickedEventArgs : EventArgs
    {
        public LinkTextClickedEventArgs(Uri uri, string text, string linkKind, string targetName, PointerPressedEventArgs pointerEventArgs)
        {
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            Text = text;
            LinkKind = linkKind;
            TargetName = targetName;
            PointerEventArgs = pointerEventArgs;
        }

        public Uri Uri { get; }

        public string Text { get; }

        public string LinkKind { get; }

        public string TargetName { get; }

        public PointerPressedEventArgs PointerEventArgs { get; }

        public bool Handled { get; set; }
    }
}
