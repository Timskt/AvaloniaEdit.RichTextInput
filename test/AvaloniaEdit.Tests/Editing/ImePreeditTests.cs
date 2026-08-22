using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NUnit.Framework;

namespace AvaloniaEdit.Tests.Editing
{
    [TestFixture]
    public class ImePreeditTests
    {
        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void SetImePreeditDoesNotChangeDocumentAndClearRemovesIt()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;

            textArea.SetImePreeditText("にほん", 2);

            Assert.AreEqual("ab", textArea.Document.Text);
            Assert.IsTrue(textArea.HasImePreedit);

            textArea.ClearImePreedit();

            Assert.AreEqual("ab", textArea.Document.Text);
            Assert.IsFalse(textArea.HasImePreedit);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditHasCursorAndNormalizedClauseDecorations()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            var brush = Brushes.Red;

            textArea.SetImePreeditText("abcdef", 3, new[]
            {
                new ImePreeditClause(1, 2, ImePreeditUnderlineStyle.Thick, brush),
                new ImePreeditClause(4, 2, ImePreeditUnderlineStyle.Double)
            });

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(1));
            var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();

            Assert.AreEqual(3, preedit.CursorOffset);
            Assert.AreEqual(4, preedit.Clauses.Count);
            Assert.AreEqual(0, preedit.Clauses[0].Start);
            Assert.AreEqual(1, preedit.Clauses[0].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Solid, preedit.Clauses[0].UnderlineStyle);
            Assert.AreEqual(1, preedit.Clauses[1].Start);
            Assert.AreEqual(2, preedit.Clauses[1].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Thick, preedit.Clauses[1].UnderlineStyle);
            Assert.AreSame(brush, preedit.Clauses[1].UnderlineBrush);
            Assert.AreEqual(3, preedit.Clauses[2].Start);
            Assert.AreEqual(1, preedit.Clauses[2].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Solid, preedit.Clauses[2].UnderlineStyle);
            Assert.AreEqual(4, preedit.Clauses[3].Start);
            Assert.AreEqual(2, preedit.Clauses[3].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Double, preedit.Clauses[3].UnderlineStyle);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditDefaultsCursorToEnd()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("abcdef");

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(1));
            var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();

            Assert.AreEqual(6, preedit.CursorOffset);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InvalidOrOverlappingClausesAreRejected()
        {
            var textArea = CreateTextArea("ab");
            Assert.Throws<ArgumentOutOfRangeException>(() => textArea.SetImePreeditText(
                "abc", clauses: new[] { new ImePreeditClause(2, 2) }));
            Assert.Throws<ArgumentException>(() => textArea.SetImePreeditText(
                "abcd", clauses: new[]
                {
                    new ImePreeditClause(0, 3),
                    new ImePreeditClause(2, 2)
                }));
        }

        [Test]
        public void SliceClausesKeepsLocalRangesAcrossWrappedChunks()
        {
            var red = Brushes.Red;
            var clauses = new[]
            {
                new ImePreeditClause(0, 4, ImePreeditUnderlineStyle.Solid),
                new ImePreeditClause(4, 4, ImePreeditUnderlineStyle.Dashed, red)
            };

            var sliced = PreeditLayer.SliceClauses(clauses, 3, 4);

            Assert.AreEqual(2, sliced.Count);
            Assert.AreEqual(0, sliced[0].Start);
            Assert.AreEqual(1, sliced[0].Length);
            Assert.AreEqual(1, sliced[1].Start);
            Assert.AreEqual(3, sliced[1].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Dashed, sliced[1].UnderlineStyle);
            Assert.AreSame(red, sliced[1].UnderlineBrush);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditWrapsUsesCaretXAndDrawsOneCursor()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 40;
            textArea.Height = 100;
            textArea.Background = Brushes.Black;
            textArea.Caret.Offset = 0;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 40, 100);
            var caretX = textArea.Caret.CalculateCaretRectangle().X;

            textArea.SetImePreeditText("日本語入力テスト", 4);
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 40, 100);

            Assert.Greater(layer.LastRenderedChunkCount, 1);
            Assert.AreEqual(layer.LastRenderedChunkCount, layer.LastRenderedChunkOrigins.Count);
            Assert.AreEqual(layer.LastRenderedChunkCount, layer.LastRenderedChunkLengths.Count);
            Assert.AreEqual(caretX - textArea.TextView.HorizontalOffset, layer.LastRenderedChunkOrigins[0].X, 0.01);
            Assert.AreEqual(1, layer.LastRenderedCursorCount);
            Assert.Greater(layer.LastRenderedBackgroundFillCount, 0);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditUsesBodyBaselineWhenVisualLineIsAvailable()
        {
            var textArea = CreateTextArea("日本語");
            textArea.Width = 300;
            textArea.Height = 100;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 300, 100);

            textArea.SetImePreeditText("abc");
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 300, 100);

            Assert.IsNotNull(layer.LastBodyBaseline);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditHandlesNarrowViewportAndGraphemeClusters()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 1;
            textArea.Height = 100;
            textArea.Caret.Offset = 0;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 1, 100);

            textArea.SetImePreeditText("😀e\u0301");
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 1, 100);

            Assert.AreEqual(2, layer.LastRenderedChunkCount);
            Assert.AreEqual(new[] { 2, 2 }, layer.LastRenderedChunkLengths.ToArray());
        }

        [Test]
        public void ImePreeditScrollReservationDefaultsToTenAndRejectsNegativeValues()
        {
            var options = new TextEditorOptions();
            Assert.AreEqual(10, options.ImePreeditHorizontalScrollCharCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => options.ImePreeditHorizontalScrollCharCount = -1);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void ImePreeditScrollReservationChangesExtent()
        {
            var textArea = CreateTextArea(new string('x', 80));
            textArea.Width = 100;
            textArea.Height = 80;
            textArea.Options.ImePreeditHorizontalScrollCharCount = 0;
            ArrangeTextView(textArea, 100, 80);
            var withoutReservation = ((IScrollable)textArea.TextView).Extent.Width;

            textArea.Options.ImePreeditHorizontalScrollCharCount = 10;
            ArrangeTextView(textArea, 100, 80);
            var withReservation = ((IScrollable)textArea.TextView).Extent.Width;

            Assert.Greater(withReservation, withoutReservation);
            Assert.Greater(withReservation - withoutReservation, 0);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void CommitOnClickFallbackInsertsAtOldCaretAndSuppressesOneDuplicate()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("かな");

            Assert.IsTrue(textArea.CommitImePreeditBeforeCaretMove());
            Assert.AreEqual("aかなb", textArea.Document.Text);
            Assert.AreEqual(3, textArea.Caret.Offset);

            RaiseTextInput(textArea, "かな");
            Assert.AreEqual("aかなb", textArea.Document.Text);

            RaiseTextInput(textArea, "かな");
            Assert.AreEqual("aかなかなb", textArea.Document.Text);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void CommitOnClickSyncResetDoesNotInsertTwice()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("かな");
            var client = GetImeClient(textArea);
            client.ResetRequested += (_, _) => RaiseTextInput(textArea, "かな");

            Assert.IsTrue(textArea.CommitImePreeditBeforeCaretMove());
            Assert.AreEqual("aかなb", textArea.Document.Text);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void CommitOnClickMismatchIsNotSwallowed()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("かな");

            textArea.CommitImePreeditBeforeCaretMove();
            RaiseTextInput(textArea, "別");

            Assert.AreEqual("aかな別b", textArea.Document.Text);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void ReadOnlyTextAreaDoesNotExposeItsImeClient()
        {
            var textArea = CreateTextArea("ab");
            textArea.ReadOnlySectionProvider = ReadOnlySectionDocument.Instance;
            var args = new TextInputMethodClientRequestedEventArgs
            {
                RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
                Source = textArea
            };

            textArea.RaiseEvent(args);

            Assert.IsNull(args.Client);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void DocumentReplacementClearsPreedit()
        {
            var textArea = CreateTextArea("ab");
            textArea.SetImePreeditText("かな");
            textArea.Document = new TextDocument("new");

            Assert.IsFalse(textArea.HasImePreedit);
        }

        private static TextArea CreateTextArea(string text)
            => new TextArea { Document = new TextDocument(text) };

        private static PreeditLayer GetPreeditLayer(TextArea textArea)
        {
            var field = typeof(TextArea).GetField("_preeditLayer", BindingFlags.Instance | BindingFlags.NonPublic);
            return (PreeditLayer)field.GetValue(textArea);
        }

        private static TextInputMethodClient GetImeClient(TextArea textArea)
        {
            var field = typeof(TextArea).GetField("_imClient", BindingFlags.Instance | BindingFlags.NonPublic);
            return (TextInputMethodClient)field.GetValue(textArea);
        }

        private static void ArrangeTextView(TextArea textArea, double width, double height)
        {
            textArea.TextView.Measure(new Size(width, height));
            textArea.TextView.Arrange(new Rect(0, 0, width, height));
            textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
        }

        private static void RenderLayer(PreeditLayer layer, int width, int height)
        {
            using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height)))
            using (var drawingContext = bitmap.CreateDrawingContext())
            {
                layer.Render(drawingContext);
            }
        }

        private static void RaiseTextInput(TextArea textArea, string text)
        {
            textArea.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Text = text,
                Source = textArea
            });
        }
    }
}
