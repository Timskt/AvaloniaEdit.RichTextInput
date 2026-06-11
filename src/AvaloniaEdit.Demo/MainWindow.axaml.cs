using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Demo.Resources;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.RichTextInput;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using Avalonia.Layout;
using AvaloniaEdit.Snippets;
using Snippet = AvaloniaEdit.Snippets.Snippet;
using AvaloniaEdit.Demo.ViewModels;
namespace AvaloniaEdit.Demo
{
    using Pair = KeyValuePair<int, Control>;

    public partial class MainWindow : Window
    {
        private FoldingManager _foldingManager;
        private readonly TextMate.TextMate.Installation _textMateInstallation;
        private CompletionWindow _completionWindow;
        private OverloadInsightWindow _insightWindow;
        private ElementGenerator _generator = new ElementGenerator();
        private RegistryOptions _registryOptions;
        private int _currentTheme = (int)ThemeName.DarkPlus;
        private CustomMargin _customMargin;
        private RichTextInputManager _richTextInputManager;

        public MainWindow()
        {
            InitializeComponent();

            Editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible;
            Editor.Background = Brushes.Transparent;
            Editor.ShowLineNumbers = true;
            Editor.TextArea.Background = this.Background;
            Editor.TextArea.TextEntered += textEditor_TextArea_TextEntered;
            Editor.TextArea.TextEntering += textEditor_TextArea_TextEntering;
            Editor.TextArea.KeyDown += TextArea_KeyDown;
            Editor.Options.AllowToggleOverstrikeMode = true;
            Editor.Options.EnableHyperlinks = true;
            Editor.Options.EnableEmailHyperlinks = true;
            Editor.Options.RequireControlModifierForHyperlinkClick = false;
            Editor.Options.EnableTextDragDrop = true;
            Editor.Options.ShowBoxForControlCharacters = true;
            Editor.Options.ColumnRulerPositions = new List<int>() { 80, 100 };
            Editor.TextArea.IndentationStrategy = new Indentation.CSharp.CSharpIndentationStrategy(Editor.Options);
            Editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
            Editor.TextArea.RightClickMovesCaret = true;
            Editor.Options.HighlightCurrentLine = true;
            Editor.Options.CompletionAcceptAction = CompletionAcceptAction.DoubleTapped;

            AddControlBtn.Click += AddControlButton_Click;
            ClearControlBtn.Click += ClearControlButton_Click;
            InsertEmojiBtn.Click += InsertEmojiButton_Click;
            InsertFileCardBtn.Click += InsertFileCardButton_Click;
            ShowRichPlainTextBtn.Click += ShowRichPlainTextButton_Click;
            InsertSnippetBtn.Click += InsertSnippetButton_Click;

            Editor.TextArea.TextView.ElementGenerators.Add(_generator);
            Editor.TextArea.TextView.AnimateInlineObjectPlacement = true;
            Editor.TextArea.TextView.InlineObjectPlacementAnimationDuration = TimeSpan.FromMilliseconds(260);
            Editor.TextArea.TextView.InlineObjectPlacementAnimationEasing = InlineObjectPlacementAnimationEasing.SmootherStep;
            Editor.TextArea.TextView.InlineObjectPlacementAnimationRetargetDurationMultiplier = 1.2;
            Editor.TextArea.TextView.CurrentLineHighlightStyleSelector = CreateCurrentLineHighlightStyle;
            Editor.TextArea.TextView.ElementGenerators.Add(LinkElementGenerator.CreateIpAddressGenerator());
            Editor.TextArea.TextView.LinkTextStyleSelector = CreateLinkTextStyle;
            Editor.TextArea.TextView.LinkTextClicked += TextView_LinkTextClicked;
            _richTextInputManager = RichTextInputManager.Install(Editor.TextArea);
            _richTextInputManager.MaxInlineElementWidth = 240;
            _richTextInputManager.MaxImageWidth = 190;
            _richTextInputManager.MaxImageHeight = 130;
            _richTextInputManager.EnterKeyBehavior = RichTextEnterKeyBehavior.PlainNewLine;
            _richTextInputManager.ElementFactory = CreateRichTextInputElement;
            _richTextInputManager.ContentPointerPressed += RichTextInputManager_ContentPointerPressed;
            _richTextInputManager.ContentDoubleTapped += RichTextInputManager_ContentDoubleTapped;
            _richTextInputManager.ContentContextRequested += RichTextInputManager_ContentContextRequested;
            _richTextInputManager.PasteHandler = RichTextInputManager_PasteHandler;

            LineContentAlignmentCombo.ItemsSource = Enum.GetValues<LineContentVerticalAlignment>();
            LineContentAlignmentCombo.SelectedItem = _richTextInputManager.LineContentAlignment;
            LineContentAlignmentCombo.SelectionChanged += LineContentAlignmentCombo_SelectionChanged;
            RichContentAlignmentCombo.ItemsSource = Enum.GetValues<InlineObjectVerticalAlignment>();
            RichContentAlignmentCombo.SelectedItem = _richTextInputManager.InlineObjectAlignment;
            RichContentAlignmentCombo.SelectionChanged += RichContentAlignmentCombo_SelectionChanged;

            _registryOptions = new RegistryOptions(
                (ThemeName)_currentTheme);

            _textMateInstallation = Editor.InstallTextMate(_registryOptions);

            _textMateInstallation.AppliedTheme += TextMateInstallationOnAppliedTheme;

            Language csharpLanguage = _registryOptions.GetLanguageByExtension(".cs");

            SyntaxModeCombo.ItemsSource = _registryOptions.GetAvailableLanguages();
            SyntaxModeCombo.SelectedItem = csharpLanguage;
            SyntaxModeCombo.SelectionChanged += SyntaxModeCombo_SelectionChanged;

            string scopeName = _registryOptions.GetScopeByLanguageId(csharpLanguage.Id);

            Editor.Document = new TextDocument(
                "// AvaloniaEdit supports displaying control chars: \a or \b or \v" + Environment.NewLine +
                "// AvaloniaEdit supports displaying underline and strikethrough" + Environment.NewLine +
                ResourceLoader.LoadSampleFile(scopeName));
            _textMateInstallation.SetGrammar(_registryOptions.GetScopeByLanguageId(csharpLanguage.Id));
            Editor.TextArea.TextView.LineTransformers.Add(new UnderlineAndStrikeThroughTransformer());

            this.AddHandler(PointerWheelChangedEvent, (o, i) =>
            {
                if (i.KeyModifiers != KeyModifiers.Control) return;
                if (i.Delta.Y > 0) Editor.FontSize++;
                else Editor.FontSize = Editor.FontSize > 1 ? Editor.FontSize - 1 : 1;
            }, RoutingStrategies.Bubble, true);

            // Add a custom margin at the left of the text area, which can be clicked.
            _customMargin = new CustomMargin();
            Editor.TextArea.LeftMargins.Insert(0, _customMargin);

            var mainWindowVM = new MainWindowViewModel(_textMateInstallation, _registryOptions);
            foreach (ThemeName themeName in Enum.GetValues<ThemeName>())
            {
                var themeViewModel = new ThemeViewModel(themeName);
                mainWindowVM.AllThemes.Add(themeViewModel);
                if (themeName == ThemeName.DarkPlus)
                {
                    mainWindowVM.SelectedTheme = themeViewModel;
                }
            }
            DataContext = mainWindowVM;
        }

        private void TextMateInstallationOnAppliedTheme(object sender, TextMate.TextMate.Installation e)
        {
            ApplyThemeColorsToEditor(e);
            ApplyThemeColorsToWindow(e);
        }

        void ApplyThemeColorsToEditor(TextMate.TextMate.Installation e)
        {
            ApplyBrushAction(e, "editor.background",brush => Editor.Background = brush);
            ApplyBrushAction(e, "editor.foreground",brush => Editor.Foreground = brush);

            if (!ApplyBrushAction(e, "editor.selectionBackground",
                    brush => Editor.TextArea.SelectionBrush = brush))
            {
                if (Application.Current!.TryGetResource("TextAreaSelectionBrush", out var resourceObject))
                {
                    if (resourceObject is IBrush brush)
                    {
                        Editor.TextArea.SelectionBrush = brush;
                    }
                }
            }

            if (!ApplyBrushAction(e, "editor.lineHighlightBackground",
                    brush =>
                    {
                        Editor.TextArea.TextView.CurrentLineBackground = brush;
                        Editor.TextArea.TextView.CurrentLineBorder = new Pen(brush); // Todo: VS Code didn't seem to have a border but it might be nice to have that option. For now just make it the same..
                    }))
            {
                Editor.TextArea.TextView.SetDefaultHighlightLineColors();
            }

            //Todo: looks like the margin doesn't have a active line highlight, would be a nice addition
            if (!ApplyBrushAction(e, "editorLineNumber.foreground",
                    brush => Editor.LineNumbersForeground = brush))
            {
                Editor.LineNumbersForeground = Editor.Foreground;
            }
        }

        private void ApplyThemeColorsToWindow(TextMate.TextMate.Installation e)
        {
            if (!ApplyBrushAction(e, "statusBar.background", brush => StatusBar.Background = brush))
            {
                StatusBar.Background = Brushes.Purple;
            }

            if (!ApplyBrushAction(e, "statusBar.foreground", brush => StatusText.Foreground = brush))
            {
                StatusText.Foreground = Brushes.White;
            }

            if (!ApplyBrushAction(e, "sideBar.background", brush => _customMargin.BackGroundBrush = brush))
            {
                _customMargin.SetDefaultBackgroundBrush();
            }

            //Applying the Editor background to the whole window for demo sake.
            ApplyBrushAction(e, "editor.background",brush => Background = brush);
            ApplyBrushAction(e, "editor.foreground",brush => Foreground = brush);
        }

        bool ApplyBrushAction(TextMate.TextMate.Installation e, string colorKeyNameFromJson, Action<IBrush> applyColorAction)
        {
            if (!e.TryGetThemeColor(colorKeyNameFromJson, out var colorString))
                return false;

            if (!Color.TryParse(colorString, out Color color))
                return false;

            var colorBrush = new SolidColorBrush(color);
            applyColorAction(colorBrush);
            return true;
        }

        private Control CreateRichTextInputElement(RichTextContentItem item)
        {
            var maxInlineWidth = GetRichContentMaxWidth();
            if (item.Content.Kind == RichTextContentKind.File
                || item.Content.Kind == RichTextContentKind.Folder
                || item.Content.Kind == RichTextContentKind.Custom)
            {
                return new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(235, 244, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4),
                    Margin = new Thickness(2, 1),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 7,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "ATTACH",
                                FontSize = 10,
                                FontWeight = FontWeight.Bold,
                                Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175)),
                                VerticalAlignment = VerticalAlignment.Center
                            },
                            new TextBlock
                            {
                                Text = item.Content.DisplayText,
                                FontSize = 12,
                                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                MaxWidth = maxInlineWidth,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    }
                };
            }

            if (item.Content.Kind == RichTextContentKind.Image && item.Content.Value is Bitmap bitmap)
            {
                return new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(3),
                    Margin = new Thickness(2, 1),
                    Child = new Image
                    {
                        Source = bitmap,
                        Width = Math.Min(_richTextInputManager.MaxImageWidth, Math.Min(maxInlineWidth, Math.Max(48, bitmap.Size.Width))),
                        Height = Math.Min(_richTextInputManager.MaxImageHeight, Math.Max(48, bitmap.Size.Height)),
                        Stretch = Stretch.Uniform
                    }
                };
            }

            return new TextBlock
            {
                Text = item.Content.DisplayText,
                FontSize = item.Content.Kind == RichTextContentKind.Emoji ? 18 : 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0)
            };
        }

        private double GetRichContentMaxWidth()
        {
            var width = Editor.TextArea.TextView.Bounds.Width;
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
                return _richTextInputManager.MaxInlineElementWidth;

            return Math.Min(_richTextInputManager.MaxInlineElementWidth, Math.Max(_richTextInputManager.MinInlineElementWidth, width - 40));
        }

        private LinkTextStyle CreateLinkTextStyle(LinkTextStyleContext context)
        {
            if (context.LinkKind == "ip")
            {
                return new LinkTextStyle
                {
                    ForegroundBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                    BackgroundBrush = Brushes.Transparent,
                    Underline = false
                };
            }

            if (context.LinkKind == "email")
            {
                return new LinkTextStyle
                {
                    ForegroundBrush = new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                    BackgroundBrush = Brushes.Transparent,
                    Underline = true
                };
            }

            return new LinkTextStyle
            {
                ForegroundBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                BackgroundBrush = Brushes.Transparent,
                Underline = true
            };
        }

        private CurrentLineHighlightStyle CreateCurrentLineHighlightStyle(CurrentLineHighlightContext context)
        {
            return new CurrentLineHighlightStyle
            {
                BackgroundBrush = new SolidColorBrush(Color.FromArgb(28, 59, 130, 246)),
                BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(72, 37, 99, 235)), 1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(1, 1),
                ExtendToViewportWidth = true
            };
        }

        private void TextView_LinkTextClicked(object sender, LinkTextClickedEventArgs e)
        {
            if (e.LinkKind == "ip")
            {
                StatusText.Text = $"IP clicked: {e.Text}";
                e.Handled = true;
                return;
            }

            StatusText.Text = $"Link clicked: {e.Text}";
        }

        private void Caret_PositionChanged(object sender, EventArgs e)
        {
            StatusText.Text = string.Format("Line {0} Column {1}",
                Editor.TextArea.Caret.Line,
                Editor.TextArea.Caret.Column);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            _textMateInstallation.Dispose();
        }

        private void SyntaxModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveUnderlineAndStrikethroughTransformer();

            Language language = (Language)SyntaxModeCombo.SelectedItem;

            if (_foldingManager != null)
            {
                _foldingManager.Clear();
                FoldingManager.Uninstall(_foldingManager);
            }

            string scopeName = _registryOptions.GetScopeByLanguageId(language.Id);

            _textMateInstallation.SetGrammar(null);
            Editor.Document = new TextDocument(ResourceLoader.LoadSampleFile(scopeName));
            _textMateInstallation.SetGrammar(scopeName);

            if (language.Id == "xml")
            {
                _foldingManager = FoldingManager.Install(Editor.TextArea);

                var strategy = new XmlFoldingStrategy();
                strategy.UpdateFoldings(_foldingManager, Editor.Document);
                return;
            }
        }

        private void RemoveUnderlineAndStrikethroughTransformer()
        {
            for (int i = Editor.TextArea.TextView.LineTransformers.Count - 1; i >= 0; i--)
            {
                if (Editor.TextArea.TextView.LineTransformers[i] is UnderlineAndStrikeThroughTransformer)
                {
                    Editor.TextArea.TextView.LineTransformers.RemoveAt(i);
                }
            }
        }

        private void AddControlButton_Click(object sender, RoutedEventArgs e)
        {
            var button = new Button() { Content = "Click me", Cursor = Cursor.Default };

            // The VerticalAlignment controls the alignment within a text line.
            button.VerticalAlignment = VerticalAlignment.Center;

            // Optionally, TextBlock.BaseLineProperty can be set. Avalonia will align the baselines
            // of all elements within a line.
            TextBlock.SetBaselineOffset(button, 22);

            _generator.controls.Add(new Pair(Editor.CaretOffset, button));
            _generator.controls.Sort(0, _generator.controls.Count, _generator);
            Editor.TextArea.TextView.Redraw();
        }

        private void ClearControlButton_Click(object sender, RoutedEventArgs e)
        {
            //TODO: delete elements using back key
            _generator.controls.Clear();
            Editor.TextArea.TextView.Redraw();
        }

        private void InsertEmojiButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.TextArea.PerformTextInput("😀");
            Editor.Focus();
        }

        private void InsertFileCardButton_Click(object sender, RoutedEventArgs e)
        {
            _richTextInputManager.InsertContent(RichTextContent.FromCustom("demo-report.pdf", new { Type = "DemoFile" }));
            Editor.Focus();
        }

        private void ShowRichPlainTextButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = _richTextInputManager.GetPlainText(null, item => $"[{item.Content.Kind}:{item.Content.DisplayText}]");
            Editor.Focus();
        }

        private void RichContentAlignmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RichContentAlignmentCombo.SelectedItem is InlineObjectVerticalAlignment alignment)
            {
                _richTextInputManager.InlineObjectAlignment = alignment;
                Editor.TextArea.TextView.Redraw();
                StatusText.Text = $"Inline alignment: {alignment}";
            }
        }

        private void LineContentAlignmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LineContentAlignmentCombo.SelectedItem is LineContentVerticalAlignment alignment)
            {
                _richTextInputManager.LineContentAlignment = alignment;
                StatusText.Text = $"Line content alignment: {alignment}";
            }
        }

        private void RichTextInputManager_ContentPointerPressed(object sender, RichTextContentPointerEventArgs e)
        {
            StatusText.Text = $"Selected {e.Item.Content.Kind}: {e.Item.Content.DisplayText}; selection={e.GetSelectedPlainText(item => item.Content.DisplayText)}";
        }

        private void RichTextInputManager_ContentDoubleTapped(object sender, RichTextContentPointerEventArgs e)
        {
            StatusText.Text = $"Double tapped {e.Item.Content.Kind}: {e.Item.Content.DisplayText}";
        }

        private void RichTextInputManager_ContentContextRequested(object sender, RichTextContentPointerEventArgs e)
        {
            var selected = e.GetSelectedItems().Count;
            StatusText.Text = $"Context requested {e.Item.Content.Kind}: {e.Item.Content.DisplayText}; selected items={selected}";
        }

        private void textEditor_TextArea_TextEntering(object sender, TextInputEventArgs e)
        {
            if (e.Text.Length > 0 && _completionWindow != null)
            {
                if (!char.IsLetterOrDigit(e.Text[0]))
                {
                    // Whenever a non-letter is typed while the completion window is open,
                    // insert the currently selected element.
                    _completionWindow.CompletionList.RequestInsertion(e);
                }
            }

            _insightWindow?.Hide();

            // Do not set e.Handled=true.
            // We still want to insert the character that was typed.
        }

        private void textEditor_TextArea_TextEntered(object sender, TextInputEventArgs e)
        {
            if (e.Text == ".")
            {

                _completionWindow = new CompletionWindow(Editor.TextArea);
                _completionWindow.Closed += (o, args) => _completionWindow = null;

                var data = _completionWindow.CompletionList.CompletionData;

                for (int i = 0; i < 500; i++)
                {
                    data.Add(new MyCompletionData("Item" + i.ToString()));
                }

                data.Insert(20, new MyCompletionData("long item to demosntrate dynamic poup resizing"));

                _completionWindow.Show();
            }
            else if (e.Text == "(")
            {
                _insightWindow = new OverloadInsightWindow(Editor.TextArea);
                _insightWindow.Closed += (o, args) => _insightWindow = null;

                _insightWindow.Provider = new MyOverloadProvider(new[]
                {
                    ("Method1(int, string)", "Method1 description"),
                    ("Method2(int)", "Method2 description"),
                    ("Method3(string)", "Method3 description"),
                });

                _insightWindow.Show();
            }

            UpdateMentionTriggerStatus();
        }

        private void TextArea_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeySymbol == "@")
                StatusText.Text = "Mention trigger key pressed";
        }

        private async Task RichTextInputManager_PasteHandler(RichTextPasteContext context)
        {
            if (context.DataTransfer.Contains(RichTextInputManager.RichTextClipboardFormat)
                || _richTextInputManager.CanInsert(context.DataTransfer))
            {
                context.UseDefault();
                return;
            }

            var text = await context.DataTransfer.TryGetTextAsync();
            if (text == null)
            {
                context.UseDefault();
                return;
            }

            context.InsertText(text);
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateMentionTriggerStatus);
        }

        private void UpdateMentionTriggerStatus()
        {
            if (_richTextInputManager.TryGetTextTrigger(
                new RichTextTextTriggerOptions
                {
                    Trigger = '@',
                    IsQueryCharacter = c => char.IsLetterOrDigit(c) || c == '_' || c == '-',
                    MaxQueryLength = 32
                },
                out var match))
            {
                var anchor = _richTextInputManager.GetCaretAnchorRect();
                StatusText.Text = $"Mention trigger: @{match.Query} at {anchor.X:0},{anchor.Bottom:0}";
            }
        }

        class UnderlineAndStrikeThroughTransformer : DocumentColorizingTransformer
        {
            protected override void ColorizeLine(DocumentLine line)
            {
                if (line.LineNumber == 2)
                {
                    string lineText = this.CurrentContext.Document.GetText(line);

                    int indexOfUnderline = lineText.IndexOf("underline");
                    int indexOfStrikeThrough = lineText.IndexOf("strikethrough");

                    if (indexOfUnderline != -1)
                    {
                        ChangeLinePart(
                            line.Offset + indexOfUnderline,
                            line.Offset + indexOfUnderline + "underline".Length,
                            visualLine =>
                            {
                                if (visualLine.TextRunProperties.TextDecorations != null)
                                {
                                    var textDecorations = new TextDecorationCollection(visualLine.TextRunProperties.TextDecorations) { TextDecorations.Underline[0] };

                                    visualLine.TextRunProperties.SetTextDecorations(textDecorations);
                                }
                                else
                                {
                                    visualLine.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                                }
                            }
                        );
                    }

                    if (indexOfStrikeThrough != -1)
                    {
                        ChangeLinePart(
                            line.Offset + indexOfStrikeThrough,
                            line.Offset + indexOfStrikeThrough + "strikethrough".Length,
                            visualLine =>
                            {
                                if (visualLine.TextRunProperties.TextDecorations != null)
                                {
                                    var textDecorations = new TextDecorationCollection(visualLine.TextRunProperties.TextDecorations) { TextDecorations.Strikethrough[0] };

                                    visualLine.TextRunProperties.SetTextDecorations(textDecorations);
                                }
                                else
                                {
                                    visualLine.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
                                }
                            }
                        );
                    }
                }
            }
        }

        private class MyOverloadProvider : IOverloadProvider
        {
            private readonly IList<(string header, string content)> _items;

            public MyOverloadProvider(IList<(string header, string content)> items)
            {
                _items = items;
                SelectedIndex = 0;
            }

            public int SelectedIndex
            {
                get;
                set
                {
                    field = value;
                    OnPropertyChanged();
                    // ReSharper disable ExplicitCallerInfoArgument
                    OnPropertyChanged(nameof(CurrentHeader));
                    OnPropertyChanged(nameof(CurrentContent));
                    // ReSharper restore ExplicitCallerInfoArgument
                }
            }

            public int Count => _items.Count;
            public string CurrentIndexText => $"{SelectedIndex + 1} of {Count}";
            public object CurrentHeader => _items[SelectedIndex].header;
            public object CurrentContent => _items[SelectedIndex].content;

            public event PropertyChangedEventHandler PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public class MyCompletionData : ICompletionData
        {
            public MyCompletionData(string text)
            {
                Text = text;
            }

            public IImage Image => null;

            public string Text { get; }

            // Use this property if you want to show a fancy UIElement in the list.
            public object Content => _contentControl ??= BuildContentControl();

            public object Description => "Description for " + Text;

            public double Priority { get; } = 0;

            public void Complete(TextArea textArea, ISegment completionSegment,
                EventArgs insertionRequestEventArgs)
            {
                textArea.Document.Replace(completionSegment, Text);
            }

            Control BuildContentControl()
            {
                TextBlock textBlock = new TextBlock();
                textBlock.Text = Text;
                textBlock.Margin = new Thickness(5);

                return textBlock;
            }

            Control _contentControl;
        }

        class ElementGenerator : VisualLineElementGenerator, IComparer<Pair>
        {
            public List<Pair> controls = new List<Pair>();

            /// <summary>
            /// Gets the first interested offset using binary search
            /// </summary>
            /// <returns>The first interested offset.</returns>
            /// <param name="startOffset">Start offset.</param>
            public override int GetFirstInterestedOffset(int startOffset)
            {
                int pos = controls.BinarySearch(new Pair(startOffset, null), this);
                if (pos < 0)
                    pos = ~pos;
                if (pos < controls.Count)
                    return controls[pos].Key;
                else
                    return -1;
            }

            public override VisualLineElement ConstructElement(int offset)
            {
                int pos = controls.BinarySearch(new Pair(offset, null), this);
                if (pos >= 0)
                    return new InlineObjectElement(0, controls[pos].Value);
                else
                    return null;
            }

            int IComparer<Pair>.Compare(Pair x, Pair y)
            {
                return x.Key.CompareTo(y.Key);
            }
        }

        private void InsertSnippetButton_Click(object sender, RoutedEventArgs e)
        {
            var className = new SnippetReplaceableTextElement { Text = "Name" };
            var snippet = new Snippet
            {
                Elements =
                {
                    new SnippetTextElement { Text = "public class " },
                    className,
                    new SnippetTextElement
                    {
                        Text = "\n{\n    public "
                    },
                    new SnippetBoundElement { TargetElement = className },
                    new SnippetTextElement { Text = "()\n    {\n        " },
                    new SnippetCaretElement(),
                    new SnippetTextElement { Text = "\n    }\n}" }
                }
            };

            snippet.Insert(Editor.TextArea);
            Editor.Focus();
        }
    }
}
