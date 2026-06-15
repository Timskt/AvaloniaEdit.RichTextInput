using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Utils;

namespace AvaloniaEdit.RichTextInput
{
    public enum RichTextContentKind
    {
        Text,
        Emoji,
        Image,
        File,
        Folder,
        Custom
    }

    public sealed class RichTextContent
    {
        public RichTextContent(
            RichTextContentKind kind,
            string displayText,
            object value = null,
            string source = null,
            string styleKey = null,
            IReadOnlyDictionary<string, object> metadata = null)
        {
            Kind = kind;
            DisplayText = displayText ?? string.Empty;
            Value = value;
            Source = source;
            StyleKey = styleKey;
            Metadata = metadata ?? new Dictionary<string, object>();
        }

        public RichTextContentKind Kind { get; }

        public string DisplayText { get; }

        public object Value { get; }

        public string Source { get; }

        public string StyleKey { get; }

        public IReadOnlyDictionary<string, object> Metadata { get; }

        public static RichTextContent FromText(string text)
        {
            return new RichTextContent(RichTextContentKind.Text, text, text);
        }

        public static RichTextContent FromEmoji(string emoji)
        {
            return new RichTextContent(RichTextContentKind.Emoji, emoji, emoji);
        }

        public static RichTextContent FromImage(Bitmap bitmap, string displayText = null, string source = null)
        {
            return new RichTextContent(RichTextContentKind.Image, displayText ?? "Image", bitmap, source);
        }

        public static RichTextContent FromFile(IStorageItem file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            return new RichTextContent(RichTextContentKind.File, file.Name, file, file.Path?.ToString());
        }

        public static RichTextContent FromFolder(IStorageItem folder)
        {
            if (folder == null)
                throw new ArgumentNullException(nameof(folder));

            return new RichTextContent(RichTextContentKind.Folder, folder.Name, folder, folder.Path?.ToString());
        }

        public static RichTextContent FromFileName(string fileName)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            return new RichTextContent(RichTextContentKind.File, Path.GetFileName(fileName), fileName, fileName);
        }

        public static RichTextContent FromFolderName(string folderName)
        {
            if (folderName == null)
                throw new ArgumentNullException(nameof(folderName));

            return new RichTextContent(RichTextContentKind.Folder, Path.GetFileName(folderName), folderName, folderName);
        }

        public static RichTextContent FromCustom(string displayText, object value, string styleKey = null, IReadOnlyDictionary<string, object> metadata = null)
        {
            return new RichTextContent(RichTextContentKind.Custom, displayText, value, null, styleKey, metadata);
        }
    }

    public sealed class RichTextContentItem
    {
        internal RichTextContentItem(RichTextContent content, AnchorSegment segment)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Segment = segment ?? throw new ArgumentNullException(nameof(segment));
        }

        public RichTextContent Content { get; }

        public int Offset => Segment.Offset;

        public int Length => Segment.Length;

        public int EndOffset => Segment.EndOffset;

        public object Tag { get; set; }

        internal AnchorSegment Segment { get; }
    }

    public sealed class RichTextInputSnapshot
    {
        public RichTextInputSnapshot()
        {
            Text = string.Empty;
            Items = Array.Empty<RichTextInputSnapshotItem>();
        }

        public RichTextInputSnapshot(string text, IReadOnlyList<RichTextInputSnapshotItem> items)
        {
            Text = text ?? string.Empty;
            Items = items ?? Array.Empty<RichTextInputSnapshotItem>();
        }

        public string Text { get; set; }

        public IReadOnlyList<RichTextInputSnapshotItem> Items { get; set; }
    }

    public sealed class RichTextInputSnapshotItem
    {
        public int Offset { get; set; }

        public RichTextContentKind Kind { get; set; }

        public string DisplayText { get; set; }

        public string Source { get; set; }

        public string StyleKey { get; set; }

        public string ImageMimeType { get; set; }

        public string ImageData { get; set; }

        public string LiveContentKey { get; set; }
    }

    public sealed class RichTextInputValue
    {
        public RichTextInputValue()
        {
            Text = string.Empty;
            Items = Array.Empty<RichTextInputValueItem>();
        }

        public RichTextInputValue(string text, IReadOnlyList<RichTextInputValueItem> items)
        {
            Text = text ?? string.Empty;
            Items = items ?? Array.Empty<RichTextInputValueItem>();
        }

        public string Text { get; set; }

        public IReadOnlyList<RichTextInputValueItem> Items { get; set; }
    }

    public sealed class RichTextInputValueItem
    {
        public int Offset { get; set; }

        public RichTextContent Content { get; set; }
    }

    public sealed class RichTextPasteContext
    {
        internal RichTextPasteContext(IDataObject dataObject, int offset, bool replaceSelection)
        {
            DataObject = dataObject ?? throw new ArgumentNullException(nameof(dataObject));
            Offset = offset;
            ReplaceSelection = replaceSelection;
            Contents = new List<RichTextContent>();
            Formats = GetFormats(dataObject);
        }

        public IDataObject DataObject { get; }

        public IReadOnlyList<string> Formats { get; }

        public int Offset { get; }

        public bool ReplaceSelection { get; }

        public IList<RichTextContent> Contents { get; }

        public string Text { get; private set; }

        public bool Handled { get; private set; }

        public void InsertContents(IEnumerable<RichTextContent> contents)
        {
            Contents.Clear();
            if (contents != null)
            {
                foreach (var content in contents)
                {
                    if (content != null)
                        Contents.Add(content);
                }
            }

            Text = null;
            Handled = true;
        }

        public void InsertText(string text)
        {
            Contents.Clear();
            Text = text ?? string.Empty;
            Handled = true;
        }

        public void UseDefault()
        {
            Contents.Clear();
            Text = null;
            Handled = false;
        }

        public bool ContainsFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
                return false;

            return Formats.Any(existing =>
                string.Equals(existing, format, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<string> GetFormats(IDataObject dataObject)
        {
            try
            {
                return (dataObject.GetDataFormats() ?? Array.Empty<string>()).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    public sealed class RichTextDropContext
    {
        internal RichTextDropContext(IDataObject dataObject, int offset, bool replaceSelection)
        {
            DataObject = dataObject ?? throw new ArgumentNullException(nameof(dataObject));
            Offset = offset;
            ReplaceSelection = replaceSelection;
            Contents = new List<RichTextContent>();
            Formats = GetFormats(dataObject);
        }

        public IDataObject DataObject { get; }

        public IReadOnlyList<string> Formats { get; }

        public int Offset { get; }

        public bool ReplaceSelection { get; }

        public IList<RichTextContent> Contents { get; }

        public string Text { get; private set; }

        public bool Handled { get; private set; }

        public void InsertContents(IEnumerable<RichTextContent> contents)
        {
            Contents.Clear();
            if (contents != null)
            {
                foreach (var content in contents)
                {
                    if (content != null)
                        Contents.Add(content);
                }
            }

            Text = null;
            Handled = true;
        }

        public void InsertText(string text)
        {
            Contents.Clear();
            Text = text ?? string.Empty;
            Handled = true;
        }

        public void UseDefault()
        {
            Contents.Clear();
            Text = null;
            Handled = false;
        }

        public bool ContainsFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
                return false;

            return Formats.Any(existing =>
                string.Equals(existing, format, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<string> GetFormats(IDataObject dataObject)
        {
            try
            {
                return (dataObject.GetDataFormats() ?? Array.Empty<string>()).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    public sealed class RichTextFileImportContext
    {
        private readonly Func<Task<RichTextContent>> _defaultFactory;

        internal RichTextFileImportContext(
            RichTextInputManager manager,
            IStorageItem storageItem,
            string fileName,
            bool isFolder,
            Func<Task<RichTextContent>> defaultFactory)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            StorageItem = storageItem;
            FileName = fileName;
            Name = storageItem?.Name ?? Path.GetFileName(fileName) ?? string.Empty;
            Source = storageItem?.Path?.ToString() ?? fileName;
            IsFolder = isFolder;
            IsImage = !isFolder && RichTextInputManager.IsImageFileName(Name);
            IsExecutable = !isFolder && RichTextInputManager.IsExecutableFileName(Name);
            IsArchive = !isFolder && RichTextInputManager.IsArchiveFileName(Name);
            IsDocument = !isFolder && RichTextInputManager.IsDocumentFileName(Name);
            _defaultFactory = defaultFactory ?? throw new ArgumentNullException(nameof(defaultFactory));
        }

        public RichTextInputManager Manager { get; }

        public IStorageItem StorageItem { get; }

        public string FileName { get; }

        public string Name { get; }

        public string Source { get; }

        public bool IsFolder { get; }

        public bool IsImage { get; }

        public bool IsExecutable { get; }

        public bool IsArchive { get; }

        public bool IsDocument { get; }

        public Task<RichTextContent> CreateDefaultContentAsync()
        {
            return _defaultFactory();
        }
    }

    public sealed class RichTextTextTriggerOptions
    {
        public RichTextTextTriggerOptions()
        {
            Trigger = '@';
            MaxQueryLength = 64;
            StopAtRichContent = true;
            AllowEmptyQuery = true;
        }

        public char Trigger { get; set; }

        public int MaxQueryLength { get; set; }

        public bool StopAtRichContent { get; set; }

        public bool AllowEmptyQuery { get; set; }

        public Func<char, bool> IsBoundary { get; set; }

        public Func<char, bool> IsQueryCharacter { get; set; }
    }

    public sealed class RichTextTextTriggerMatch
    {
        public RichTextTextTriggerMatch(char trigger, int triggerOffset, int caretOffset, string query)
        {
            Trigger = trigger;
            TriggerOffset = triggerOffset;
            CaretOffset = caretOffset;
            Query = query ?? string.Empty;
        }

        public char Trigger { get; }

        public int TriggerOffset { get; }

        public int CaretOffset { get; }

        public int Length => CaretOffset - TriggerOffset;

        public string Query { get; }
    }

    public sealed class RichTextContentChangedEventArgs : EventArgs
    {
        public RichTextContentChangedEventArgs(RichTextContentItem item)
        {
            Item = item;
        }

        public RichTextContentItem Item { get; }
    }

    public sealed class RichTextContentRemovingEventArgs : EventArgs
    {
        public RichTextContentRemovingEventArgs(RichTextContentItem item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }

        public RichTextContentItem Item { get; }

        public bool Cancel { get; set; }
    }

    public enum RichTextContentPointerEventKind
    {
        PointerPressed,
        PointerReleased,
        DoubleTapped,
        ContextRequested
    }

    public enum RichTextContentPointerSelectionBehavior
    {
        None,
        SelectContent,
        PreserveSelection,
        ExtendSelection
    }

    public enum RichTextSelectedContentEnterBehavior
    {
        KeepDefault,
        MoveCaretAfterContent,
        InsertNewLineBeforeContent,
        InsertNewLineAfterContent
    }

    public enum RichTextEnterKeyBehavior
    {
        KeepDefault,
        PlainNewLine,
        PlainNewLineWhenAdjacentToContent
    }

    public sealed class RichTextContentPointerEventArgs : EventArgs
    {
        public RichTextContentPointerEventArgs(RichTextContentItem item, RoutedEventArgs routedEventArgs)
            : this(null, item, routedEventArgs, RichTextContentPointerEventKind.PointerPressed)
        {
        }

        internal RichTextContentPointerEventArgs(
            RichTextInputManager manager,
            RichTextContentItem item,
            RoutedEventArgs routedEventArgs,
            RichTextContentPointerEventKind eventKind)
        {
            Manager = manager;
            Item = item ?? throw new ArgumentNullException(nameof(item));
            RoutedEventArgs = routedEventArgs;
            EventKind = eventKind;
        }

        public RichTextInputManager Manager { get; }

        public RichTextContentItem Item { get; }

        public RoutedEventArgs RoutedEventArgs { get; }

        public RichTextContentPointerEventKind EventKind { get; }

        public TextArea TextArea => Manager?.TextArea;

        public PointerEventArgs PointerEventArgs => RoutedEventArgs as PointerEventArgs;

        public TappedEventArgs TappedEventArgs => RoutedEventArgs as TappedEventArgs;

        public ContextRequestedEventArgs ContextRequestedEventArgs => RoutedEventArgs as ContextRequestedEventArgs;

        public bool IsSelected => Manager?.IsContentSelected(Item) == true;

        public KeyModifiers KeyModifiers => PointerEventArgs?.KeyModifiers ?? KeyModifiers.None;

        public bool IsLeftButtonPressed => PointerEventArgs?
            .GetCurrentPoint(TextArea)
            .Properties
            .IsLeftButtonPressed == true;

        public bool IsRightButtonPressed => PointerEventArgs?
            .GetCurrentPoint(TextArea)
            .Properties
            .IsRightButtonPressed == true;

        public bool Handled { get; set; }

        public bool TryGetPosition(Control relativeTo, out Point position)
        {
            if (PointerEventArgs == null)
            {
                position = default;
                return false;
            }

            position = PointerEventArgs.GetPosition(relativeTo ?? TextArea);
            return true;
        }

        public IReadOnlyList<RichTextContentItem> GetSelectedItems()
        {
            return Manager?.GetSelectedItems() ?? Array.Empty<RichTextContentItem>();
        }

        public RichTextInputValue GetSelectionValue()
        {
            return Manager?.GetSelectionValue() ?? new RichTextInputValue();
        }

        public string GetSelectedPlainText(Func<RichTextContentItem, string> contentTextFactory = null)
        {
            return Manager?.GetSelectedPlainText(contentTextFactory) ?? string.Empty;
        }
    }

    public sealed class RichTextInlineContentStyle
    {
        public IBrush Background { get; set; } = Brushes.Transparent;

        public IBrush BorderBrush { get; set; } = Brushes.Transparent;

        public Thickness BorderThickness { get; set; } = new Thickness(1);

        public CornerRadius CornerRadius { get; set; } = new CornerRadius(4);

        public Thickness Padding { get; set; } = new Thickness(0);
    }

    public sealed class RichTextElementFactoryContext
    {
        internal RichTextElementFactoryContext(
            RichTextInputManager manager,
            RichTextContentItem item,
            double availableWidth,
            bool isSelected)
        {
            Manager = manager;
            Item = item;
            Content = item.Content;
            TextArea = manager.TextArea;
            AvailableWidth = availableWidth;
            IsSelected = isSelected;
        }

        public RichTextInputManager Manager { get; }

        public RichTextContentItem Item { get; }

        public RichTextContent Content { get; }

        public TextArea TextArea { get; }

        public double AvailableWidth { get; }

        public double MaxImageWidth => Manager.MaxImageWidth;

        public double MaxImageHeight => Manager.MaxImageHeight;

        public bool IsSelected { get; }

        public string StyleKey => Content.StyleKey;

        public IReadOnlyDictionary<string, object> Metadata => Content.Metadata;
    }

    public interface IRichTextInputDataHandler
    {
        bool CanInsert(IDataObject dataObject);

        Task<bool> InsertDataAsync(IDataObject dataObject, int offset, bool replaceSelection);
    }

    public sealed class RichTextInputManager : IDisposable, IRichTextInputDataHandler, ISelectionBackgroundSegmentTransformer
    {
        public const char ObjectReplacementCharacter = '\uFFFC';
        public const string ObjectReplacementString = "\uFFFC";
        public const string RichTextClipboardFormat = "AvaloniaEdit.RichTextInput";

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".webp"
        };

        private static readonly HashSet<string> ExecutableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".app", ".bat", ".cmd", ".com", ".dll", ".dmg", ".exe", ".msi", ".ps1", ".sh"
        };

        private static readonly HashSet<string> ArchiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".7z", ".gz", ".rar", ".tar", ".tgz", ".zip"
        };

        private static readonly HashSet<string> DocumentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".csv", ".doc", ".docx", ".md", ".pdf", ".ppt", ".pptx", ".txt", ".xls", ".xlsx"
        };

        private static readonly string[] BitmapDataFormats =
        {
            "Bitmap",
            "image/png",
            "image/jpeg",
            "image/jpg",
            "image/bmp",
            "image/gif",
            "image/tiff",
            "image/webp",
            "image/x-png",
            "PNG",
            "JFIF",
            "JPEG",
            "TIFF",
            "BMP",
            "GIF",
            "WEBP",
            "jp2",
            "JPEG 2000",
            "TIFF picture",
            "NeXT TIFF v4.0 pasteboard type",
            "PNGf",
            "JPEG picture",
            "GIF picture",
            "BMP ",
            "TPIC",
            "jp2 ",
            "8BPS",
            "AVIF",
            "public.png",
            "public.jpeg",
            "public.tiff",
            "public.gif",
            "public.bmp",
            "public.webp",
            "public.heic",
            "public.heif",
            "public.avif",
            "public.jpeg-2000",
            "com.compuserve.gif",
            "com.microsoft.bmp",
            "com.adobe.photoshop-image",
            "System.Drawing.Bitmap",
            "DeviceIndependentBitmap",
            "CF_DIB",
            "CF_DIBV5",
            "Format17"
        };
        private const int MaxLiveClipboardContentCacheSize = 512;
        private static readonly object LiveClipboardContentCacheLock = new object();
        private static readonly Dictionary<string, RichTextContent> LiveClipboardContentCache =
            new Dictionary<string, RichTextContent>(StringComparer.Ordinal);
        private static readonly Queue<string> LiveClipboardContentCacheOrder = new Queue<string>();
        private static readonly object RichClipboardFallbackLock = new object();
        private static RichClipboardFallbackSnapshot _richClipboardFallbackSnapshot;

        private readonly TextArea _textArea;
        private readonly RichTextInlineObjectGenerator _generator;
        private readonly List<RichTextContentItem> _items = new List<RichTextContentItem>();
        private readonly List<PendingRichContentReanchor> _pendingReanchors = new List<PendingRichContentReanchor>();
        private readonly Dictionary<string, Func<RichTextElementFactoryContext, Control>> _elementFactories =
            new Dictionary<string, Func<RichTextElementFactoryContext, Control>>(StringComparer.Ordinal);
        private RichTextContentItem[] _orderedItemsCache;
        private bool _richContentChangedDuringDocumentChange;
        private int _deferRedrawCount;
        private int _suspendInvalidItemValidationCount;
        private bool _redrawPending;
        private bool _suppressTextSelectionBackgroundForRichContent = true;
        private bool _isDisposed;

        public RichTextInputManager(TextArea textArea)
        {
            _textArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
            _generator = new RichTextInlineObjectGenerator(this);
            ElementFactory = CreateDefaultElement;
            ConvertImageFilesToImages = true;

            _textArea.TextView.ElementGenerators.Add(_generator);
            _textArea.TextView.Services.AddService<IRichTextInputDataHandler>(this);
            _textArea.TextView.Services.AddService<ISelectionBackgroundSegmentTransformer>(this);
            _textArea.TextView.SizeChanged += TextView_SizeChanged;
            _textArea.DocumentChanged += TextArea_DocumentChanged;
            _textArea.SelectionChanged += TextArea_SelectionChanged;
            _textArea.AddHandler(InputElement.KeyDownEvent, TextArea_KeyDown, RoutingStrategies.Tunnel);
            AttachToDocument(_textArea.Document);
        }

        public IReadOnlyList<RichTextContentItem> Items => _items;

        public TextArea TextArea => _textArea;

        internal int InvalidItemValidationPassCount { get; private set; }

        internal int ItemsCacheBuildCount { get; private set; }

        public bool ConvertImageFilesToImages { get; set; }

        public Func<RichTextContentItem, Control> ElementFactory { get; set; }

        public Func<RichTextElementFactoryContext, Control> ElementFactoryWithContext { get; set; }

        public double MinInlineElementWidth { get; set; } = 48;

        public double MaxInlineElementWidth { get; set; } = 220;

        public double MaxImageWidth { get; set; } = 180;

        public double MaxImageHeight { get; set; } = 120;

        public int MaxEmbeddedClipboardImageBytes { get; set; } = 4 * 1024 * 1024;

        public Func<IDataObject, bool> CanImportDataObject { get; set; }

        public Func<IDataObject, Task<IEnumerable<RichTextContent>>> DataObjectImporter { get; set; }

        public Func<RichTextPasteContext, Task> PasteHandler { get; set; }

        public Func<RichTextDropContext, Task> DropHandler { get; set; }

        public Func<RichTextFileImportContext, Task<RichTextContent>> FileContentImporter { get; set; }

        public InlineObjectVerticalAlignment InlineObjectAlignment { get; set; } = InlineObjectVerticalAlignment.Bottom;

        public Func<RichTextContentItem, InlineObjectVerticalAlignment> InlineObjectAlignmentSelector { get; set; }

        public Func<RichTextContentItem, double> InlineObjectBaselineOffsetSelector { get; set; }

        public Func<RichTextContentItem, Vector> InlineObjectArrangeOffsetSelector { get; set; }

        public LineContentVerticalAlignment LineContentAlignment
        {
            get { return _textArea.Options.LineContentVerticalAlignment; }
            set { _textArea.Options.LineContentVerticalAlignment = value; }
        }

        public bool SelectContentOnPointerPressed { get; set; } = true;

        public bool EnableContentPointerInteractions { get; set; } = true;

        public bool HighlightSelectedContent { get; set; } = true;

        public bool SuppressTextSelectionBackgroundForRichContent
        {
            get => _suppressTextSelectionBackgroundForRichContent;
            set
            {
                if (_suppressTextSelectionBackgroundForRichContent == value)
                    return;

                _suppressTextSelectionBackgroundForRichContent = value;
                _textArea.TextView.InvalidateLayer(KnownLayer.Selection);
            }
        }

        public Func<RichTextContentItem, bool, RichTextInlineContentStyle> InlineContentStyleSelector { get; set; }

        public Func<RichTextContentItem, bool> CanRemoveContent { get; set; }

        public RichTextContentPointerSelectionBehavior ContentPointerSelectionBehavior { get; set; } =
            RichTextContentPointerSelectionBehavior.SelectContent;

        public Func<RichTextContentPointerEventArgs, RichTextContentPointerSelectionBehavior> ContentPointerSelectionBehaviorSelector { get; set; }

        public bool HandleContentPointerEvents { get; set; } = true;

        public Func<RichTextContentPointerEventArgs, bool> ContentPointerHandledSelector { get; set; }

        public RichTextSelectedContentEnterBehavior SelectedContentEnterBehavior { get; set; } =
            RichTextSelectedContentEnterBehavior.InsertNewLineAfterContent;

        public RichTextEnterKeyBehavior EnterKeyBehavior { get; set; } =
            RichTextEnterKeyBehavior.PlainNewLineWhenAdjacentToContent;

        public event EventHandler<RichTextContentChangedEventArgs> ContentInserted;

        public event EventHandler<RichTextContentRemovingEventArgs> ContentRemoving;

        public event EventHandler<RichTextContentChangedEventArgs> ContentRemoved;

        public event EventHandler<RichTextContentChangedEventArgs> ContentSelectionChanged;

        public event EventHandler<RichTextContentPointerEventArgs> ContentPointerPressed;

        public event EventHandler<RichTextContentPointerEventArgs> ContentPointerReleased;

        public event EventHandler<RichTextContentPointerEventArgs> ContentDoubleTapped;

        public event EventHandler<RichTextContentPointerEventArgs> ContentContextRequested;

        public static RichTextInputManager Install(TextArea textArea)
        {
            if (textArea == null)
                throw new ArgumentNullException(nameof(textArea));

            var existing = textArea.GetService(typeof(IRichTextInputDataHandler)) as RichTextInputManager;
            return existing ?? new RichTextInputManager(textArea);
        }

        public RichTextContentItem InsertContent(RichTextContent content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            RichTextContentItem item;
            using (document.RunUpdate())
            {
                if (!_textArea.Selection.IsEmpty)
                    _textArea.RemoveSelectedText();

                var offset = _textArea.Caret.Offset;
                item = InsertContent(offset, content);
                _textArea.Caret.Offset = offset + ObjectReplacementString.Length;
                _textArea.ClearSelection();
            }

            FinalizeCaretAfterInsertion();
            return item;
        }

        public RichTextContentItem InsertEmoji(string emoji)
        {
            return InsertContent(RichTextContent.FromEmoji(emoji));
        }

        public RichTextContentItem InsertImage(Bitmap bitmap, string displayText = null, string source = null)
        {
            return InsertContent(RichTextContent.FromImage(bitmap, displayText, source));
        }

        public RichTextContentItem InsertFile(IStorageItem file)
        {
            return InsertContent(RichTextContent.FromFile(file));
        }

        public RichTextContentItem InsertFileName(string fileName)
        {
            return InsertContent(RichTextContent.FromFileName(fileName));
        }

        public RichTextContentItem InsertFolder(IStorageItem folder)
        {
            return InsertContent(RichTextContent.FromFolder(folder));
        }

        public RichTextContentItem InsertFolderName(string folderName)
        {
            return InsertContent(RichTextContent.FromFolderName(folderName));
        }

        public RichTextContentItem InsertCustom(string displayText, object value)
        {
            return InsertContent(RichTextContent.FromCustom(displayText, value));
        }

        public RichTextContentItem InsertCustom(string displayText, object value, string styleKey, IReadOnlyDictionary<string, object> metadata = null)
        {
            return InsertContent(RichTextContent.FromCustom(displayText, value, styleKey, metadata));
        }

        public IReadOnlyList<RichTextContentItem> GetItemsInDocumentOrder()
        {
            RemoveInvalidItems();
            return GetOrderedItemsCache().ToArray();
        }

        public IReadOnlyList<RichTextContentItem> GetSelectedItems()
        {
            if (_textArea.Selection.IsEmpty)
                return Array.Empty<RichTextContentItem>();

            var segments = GetOrderedSelectionSegments();
            RemoveInvalidItems();
            var result = new List<RichTextContentItem>();
            foreach (var segment in segments)
            {
                foreach (var item in GetItemsInRange(segment.StartOffset, segment.EndOffset))
                {
                    if (item.EndOffset <= segment.EndOffset)
                        result.Add(item);
                }
            }

            return result;
        }

        public bool RemoveContent(RichTextContentItem item)
        {
            if (item == null || !_items.Contains(item) || _textArea.Document == null)
                return false;
            if (!CanRemove(item))
                return false;

            _textArea.Document.Remove(item.Offset, item.Length);
            return true;
        }

        public RichTextContentItem InsertContent(int offset, RichTextContent content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            using (document.RunUpdate())
            using (DeferRedraw())
            using (SuspendInvalidItemValidation())
            {
                return InsertContentCore(offset, content);
            }
        }

        public IReadOnlyList<RichTextContentItem> InsertContents(IEnumerable<RichTextContent> contents)
        {
            var contentList = NormalizeContents(contents);
            if (contentList.Count == 0)
                return Array.Empty<RichTextContentItem>();

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            IReadOnlyList<RichTextContentItem> items;
            var offset = _textArea.Caret.Offset;
            using (document.RunUpdate())
            using (DeferRedraw())
            using (SuspendInvalidItemValidation())
            {
                if (!_textArea.Selection.IsEmpty)
                    _textArea.RemoveSelectedText();

                offset = _textArea.Caret.Offset;
                items = InsertContentsCore(offset, contentList);
                _textArea.Caret.Offset = offset + items.Count;
                _textArea.ClearSelection();
            }

            FinalizeCaretAfterInsertion();
            return items;
        }

        public IReadOnlyList<RichTextContentItem> InsertContents(int offset, IEnumerable<RichTextContent> contents)
        {
            var contentList = NormalizeContents(contents);
            if (contentList.Count == 0)
                return Array.Empty<RichTextContentItem>();

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            using (document.RunUpdate())
            using (DeferRedraw())
            using (SuspendInvalidItemValidation())
            {
                return InsertContentsCore(offset, contentList);
            }
        }

        public RichTextContentItem ReplaceRangeWithContent(int offset, int length, RichTextContent content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            if (offset + length > document.TextLength)
                throw new ArgumentOutOfRangeException(nameof(length));

            using (document.RunUpdate())
            {
                document.Replace(offset, length, ObjectReplacementString, OffsetChangeMappingType.KeepAnchorBeforeInsertion);
                var item = AddItem(offset, content);
                if (document.UndoStack.AcceptChanges)
                    document.UndoStack.Push(new RichTextContentUndoOperation(this, content, offset, true, item));

                _textArea.Caret.Offset = offset + ObjectReplacementString.Length;
                _textArea.ClearSelection();
                FinalizeCaretAfterInsertion();
                return item;
            }
        }

        public Rect GetCaretAnchorRect()
        {
            var rect = _textArea.Caret.CalculateCaretRectangle();
            return rect.WithX(rect.X - _textArea.TextView.HorizontalOffset)
                .WithY(rect.Y - _textArea.TextView.VerticalOffset);
        }

        public bool TryGetTextTriggerRange(char trigger, out int triggerOffset, out string query)
        {
            if (TryGetTextTrigger(new RichTextTextTriggerOptions { Trigger = trigger }, out var match))
            {
                triggerOffset = match.TriggerOffset;
                query = match.Query;
                return true;
            }

            triggerOffset = -1;
            query = null;
            return false;
        }

        public bool TryGetTextTrigger(RichTextTextTriggerOptions options, out RichTextTextTriggerMatch match)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            match = null;

            var document = _textArea.Document;
            if (document == null)
                return false;

            var caretOffset = Math.Max(0, Math.Min(_textArea.Caret.Offset, document.TextLength));
            var minOffset = options.MaxQueryLength > 0
                ? Math.Max(0, caretOffset - options.MaxQueryLength - 1)
                : 0;

            for (var offset = caretOffset - 1; offset >= minOffset; offset--)
            {
                var c = document.GetCharAt(offset);
                if (c == options.Trigger)
                {
                    var query = document.GetText(offset + 1, caretOffset - offset - 1);
                    if (!options.AllowEmptyQuery && query.Length == 0)
                        return false;

                    match = new RichTextTextTriggerMatch(options.Trigger, offset, caretOffset, query);
                    return true;
                }

                if (options.StopAtRichContent && c == ObjectReplacementCharacter)
                    break;

                if (options.IsBoundary?.Invoke(c) == true || (options.IsBoundary == null && char.IsWhiteSpace(c)))
                    break;

                if (options.IsQueryCharacter != null && !options.IsQueryCharacter(c))
                    break;
            }

            return false;
        }

        public bool TryGetItem(int offset, out RichTextContentItem item)
        {
            var index = FindItemIndexAtOrAfter(offset);
            var items = GetOrderedItemsCache();
            if (index >= 0 && index < items.Length && items[index].Offset == offset)
            {
                item = items[index];
                return true;
            }

            item = null;
            return false;
        }

        public int GetFirstInterestedOffset(int startOffset)
        {
            var index = FindItemIndexAtOrAfter(startOffset);
            var items = GetOrderedItemsCache();
            return index >= 0 && index < items.Length ? items[index].Offset : -1;
        }

        public Control CreateElement(RichTextContentItem item)
        {
            var context = CreateElementFactoryContext(item);
            Control element = null;

            if (ElementFactoryWithContext != null)
                element = ElementFactoryWithContext(context);

            Func<RichTextElementFactoryContext, Control> keyedFactory;
            if (element == null
                && !string.IsNullOrEmpty(item.Content.StyleKey)
                && _elementFactories.TryGetValue(item.Content.StyleKey, out keyedFactory))
            {
                element = keyedFactory(context);
            }

            var factory = ElementFactory;
            if (element == null && factory != null)
                element = factory(item);
            if (element == null)
                element = CreateDefaultElement(item, context.AvailableWidth, MaxImageWidth, MaxImageHeight);
            return EnableContentPointerInteractions ? new RichTextInlineContentControl(this, item, element) : element;
        }

        public RichTextElementFactoryContext CreateElementFactoryContext(RichTextContentItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            return new RichTextElementFactoryContext(this, item, GetConstrainedInlineWidth(), IsContentSelected(item));
        }

        public void RegisterElementFactory(string styleKey, Func<RichTextElementFactoryContext, Control> factory)
        {
            if (string.IsNullOrEmpty(styleKey))
                throw new ArgumentException("A style key is required.", nameof(styleKey));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            _elementFactories[styleKey] = factory;
            _textArea.TextView.Redraw();
        }

        public bool UnregisterElementFactory(string styleKey)
        {
            if (string.IsNullOrEmpty(styleKey))
                return false;

            var removed = _elementFactories.Remove(styleKey);
            if (removed)
                _textArea.TextView.Redraw();
            return removed;
        }

        public void ClearElementFactories()
        {
            if (_elementFactories.Count == 0)
                return;

            _elementFactories.Clear();
            _textArea.TextView.Redraw();
        }

        public InlineObjectVerticalAlignment GetInlineObjectAlignment(RichTextContentItem item)
        {
            return InlineObjectAlignmentSelector?.Invoke(item) ?? InlineObjectAlignment;
        }

        public double GetInlineObjectBaselineOffset(RichTextContentItem item)
        {
            var offset = InlineObjectBaselineOffsetSelector?.Invoke(item) ?? 0;
            return double.IsNaN(offset) || double.IsInfinity(offset) ? 0 : offset;
        }

        public Vector GetInlineObjectArrangeOffset(RichTextContentItem item)
        {
            var offset = InlineObjectArrangeOffsetSelector?.Invoke(item) ?? default(Vector);
            return double.IsNaN(offset.X) || double.IsInfinity(offset.X) || double.IsNaN(offset.Y) || double.IsInfinity(offset.Y)
                ? default(Vector)
                : offset;
        }

        public RichTextInlineContentStyle GetInlineContentStyle(RichTextContentItem item, bool selected)
        {
            var style = InlineContentStyleSelector?.Invoke(item, selected);
            if (style != null)
                return style;

            if (selected && HighlightSelectedContent)
            {
                return new RichTextInlineContentStyle
                {
                    Background = Brushes.Transparent,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4)
                };
            }

            return new RichTextInlineContentStyle();
        }

        public bool CanInsert(IDataObject dataObject)
        {
            if (dataObject == null)
                return false;

            return CanImportDataObject?.Invoke(dataObject) == true
                || CanInsertDefaultData(dataObject);
        }

        public bool CanPaste(IDataObject dataObject)
        {
            return dataObject != null && (PasteHandler != null || CanInsert(dataObject));
        }

        public bool CanDrop(IDataObject dataObject)
        {
            return dataObject != null && (DropHandler != null || CanInsert(dataObject));
        }

        public bool CanInsertDefaultData(IDataObject dataObject)
        {
            return dataObject != null
                && (ContainsBitmapData(dataObject)
                    || dataObject.Contains(DataFormats.Files)
                    || dataObject.Contains(DataFormats.FileNames)
                    || dataObject.Contains(RichTextClipboardFormat)
                    || CanRestoreRichClipboardFallback(GetDataObjectText(dataObject)));
        }

        public Task<bool> InsertDataAsync(IDataObject dataObject, int offset, bool replaceSelection)
        {
            if (dataObject == null)
                throw new ArgumentNullException(nameof(dataObject));

            return InsertDataCoreAsync(dataObject, offset, replaceSelection);
        }

        public async Task<bool> InsertPasteDataAsync(IDataObject dataObject, int offset, bool replaceSelection)
        {
            if (dataObject == null)
                throw new ArgumentNullException(nameof(dataObject));

            if (PasteHandler != null)
            {
                var context = new RichTextPasteContext(dataObject, offset, replaceSelection);
                await PasteHandler(context);
                if (context.Handled)
                {
                    if (context.Contents.Count > 0)
                        return InsertContents(offset, replaceSelection, context.Contents);

                    return InsertText(offset, replaceSelection, context.Text);
                }
            }

            return await InsertDataCoreAsync(dataObject, offset, replaceSelection);
        }

        public async Task<bool> InsertDropDataAsync(IDataObject dataObject, int offset, bool replaceSelection)
        {
            if (dataObject == null)
                throw new ArgumentNullException(nameof(dataObject));

            if (DropHandler != null)
            {
                var context = new RichTextDropContext(dataObject, offset, replaceSelection);
                await DropHandler(context);
                if (context.Handled)
                {
                    if (context.Contents.Count > 0)
                        return InsertContents(offset, replaceSelection, context.Contents);

                    return InsertText(offset, replaceSelection, context.Text);
                }
            }

            return await InsertDataCoreAsync(dataObject, offset, replaceSelection);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            DetachFromDocument(_textArea.Document);
            _textArea.TextView.SizeChanged -= TextView_SizeChanged;
            _textArea.DocumentChanged -= TextArea_DocumentChanged;
            _textArea.SelectionChanged -= TextArea_SelectionChanged;
            _textArea.RemoveHandler(InputElement.KeyDownEvent, TextArea_KeyDown);
            _textArea.TextView.ElementGenerators.Remove(_generator);
            if (_textArea.GetService(typeof(IRichTextInputDataHandler)) == this)
                _textArea.TextView.Services.RemoveService<IRichTextInputDataHandler>();
            if (_textArea.GetService(typeof(ISelectionBackgroundSegmentTransformer)) == this)
                _textArea.TextView.Services.RemoveService<ISelectionBackgroundSegmentTransformer>();
        }

        public IEnumerable<ISegment> TransformSelectionBackgroundSegments(IEnumerable<SelectionSegment> segments)
        {
            if (segments == null)
                yield break;

            if (!SuppressTextSelectionBackgroundForRichContent || _items.Count == 0)
            {
                foreach (var segment in segments)
                    yield return segment;
                yield break;
            }

            RemoveInvalidItems();
            foreach (var segment in segments)
            {
                var currentOffset = segment.StartOffset;
                foreach (var item in GetItemsInRange(segment.StartOffset, segment.EndOffset))
                {
                    if (item.Offset > currentOffset)
                        yield return new SimpleSegment(currentOffset, item.Offset - currentOffset);

                    currentOffset = Math.Max(currentOffset, Math.Min(item.EndOffset, segment.EndOffset));
                    if (currentOffset >= segment.EndOffset)
                        break;
                }

                if (currentOffset < segment.EndOffset)
                    yield return new SimpleSegment(currentOffset, segment.EndOffset - currentOffset);
            }
        }

        private async Task<bool> InsertDataCoreAsync(IDataObject dataObject, int offset, bool replaceSelection)
        {
            var contents = new List<RichTextContent>();
            if (dataObject.Contains(RichTextClipboardFormat))
            {
                var richTextPayload = dataObject.Get(RichTextClipboardFormat) as string;
                if (!string.IsNullOrEmpty(richTextPayload))
                    return InsertSerializedSnapshot(richTextPayload, offset, replaceSelection);
            }

            if (TryGetRichClipboardFallbackPayload(GetDataObjectText(dataObject), out var fallbackPayload))
                return InsertSerializedSnapshot(fallbackPayload, offset, replaceSelection);

            if (DataObjectImporter != null && CanImportDataObject?.Invoke(dataObject) == true)
            {
                var imported = await DataObjectImporter(dataObject);
                if (imported != null)
                    contents.AddRange(imported.Where(content => content != null));
            }

            if (contents.Count > 0)
                return InsertContents(offset, replaceSelection, contents);

            var files = dataObject.GetFiles();
            if (files != null && files.Any())
            {
                foreach (var file in files)
                {
                    var content = await CreateContentForFileAsync(file);
                    if (content != null)
                        contents.Add(content);
                }
            }
            else
            {
                var fileNames = dataObject.GetFileNames();
                if (fileNames != null)
                {
                    foreach (var fileName in fileNames)
                    {
                        var content = await CreateContentForFileNameAsync(fileName);
                        if (content != null)
                            contents.Add(content);
                    }
                }

                if (contents.Count == 0)
                {
                    var bitmap = TryGetBitmap(dataObject);
                    if (bitmap != null)
                        contents.Add(RichTextContent.FromImage(bitmap));
                }
            }

            return InsertContents(offset, replaceSelection, contents);
        }

        public RichTextInputSnapshot CreateSnapshot(ISegment segment = null, bool removeObjectReplacementCharacters = false)
        {
            return CreateSnapshot(segment, removeObjectReplacementCharacters, false);
        }

        private RichTextInputSnapshot CreateSnapshot(ISegment segment, bool removeObjectReplacementCharacters, bool includeImageData)
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            segment ??= new SimpleSegment(0, document.TextLength);
            var text = document.GetText(segment);
            var rangeItems = GetItemsInRange(segment.Offset, segment.EndOffset, true);
            var items = new RichTextInputSnapshotItem[rangeItems.Count];
            for (var i = 0; i < rangeItems.Count; i++)
            {
                var item = rangeItems[i];
                items[i] = CreateSnapshotItem(item, item.Offset - segment.Offset, includeImageData);
            }

            if (!removeObjectReplacementCharacters)
                return new RichTextInputSnapshot(text, items);

            var adjusted = text;
            for (var i = items.Length - 1; i >= 0; i--)
            {
                var item = items[i];
                if (item.Offset >= 0 && item.Offset < adjusted.Length && adjusted[item.Offset] == ObjectReplacementCharacter)
                    adjusted = adjusted.Remove(item.Offset, 1);
            }

            var removedBefore = 0;
            foreach (var item in items)
            {
                item.Offset -= removedBefore;
                removedBefore++;
            }

            return new RichTextInputSnapshot(adjusted, items);
        }

        private RichTextInputSnapshotItem CreateSnapshotItem(RichTextContentItem item, int offset, bool includeImageData)
        {
            var snapshotItem = new RichTextInputSnapshotItem
            {
                Offset = offset,
                Kind = item.Content.Kind,
                DisplayText = item.Content.DisplayText,
                Source = item.Content.Source,
                StyleKey = item.Content.StyleKey,
                LiveContentKey = includeImageData ? RegisterLiveClipboardContent(item.Content) : null
            };

            if (includeImageData
                && item.Content.Kind == RichTextContentKind.Image
                && item.Content.Value is Bitmap bitmap
                && !HasReadableImageSource(item.Content.Source))
            {
                if (TryEncodeBitmap(bitmap, MaxEmbeddedClipboardImageBytes, out var imageData, out var imageMimeType))
                {
                    snapshotItem.ImageMimeType = imageMimeType;
                    snapshotItem.ImageData = imageData;
                }
            }

            return snapshotItem;
        }

        public string SerializeSnapshot(ISegment segment = null)
        {
            return SerializeSnapshot(CreateSnapshot(segment));
        }

        public RichTextInputSnapshot GetSnapshot(bool removeObjectReplacementCharacters = false)
        {
            return CreateSnapshot(null, removeObjectReplacementCharacters);
        }

        public string SerializeValue()
        {
            return SerializeSnapshot();
        }

        public RichTextInputValue GetValue(ISegment segment = null)
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            segment ??= new SimpleSegment(0, document.TextLength);
            var text = document.GetText(segment);
            var rangeItems = GetItemsInRange(segment.Offset, segment.EndOffset, true);
            var items = new RichTextInputValueItem[rangeItems.Count];
            for (var i = 0; i < rangeItems.Count; i++)
            {
                var item = rangeItems[i];
                items[i] = new RichTextInputValueItem
                {
                    Offset = item.Offset - segment.Offset,
                    Content = item.Content
                };
            }

            return new RichTextInputValue(text, items);
        }

        public RichTextInputValue GetSelectionValue()
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            if (_textArea.Selection.IsEmpty)
                return new RichTextInputValue();

            var text = new System.Text.StringBuilder();
            var items = new List<RichTextInputValueItem>();
            foreach (var segment in GetOrderedSelectionSegments())
            {
                var baseOffset = text.Length;
                var value = GetValue(new SimpleSegment(segment.StartOffset, segment.EndOffset - segment.StartOffset));
                text.Append(value.Text);
                foreach (var item in value.Items)
                {
                    items.Add(new RichTextInputValueItem
                    {
                        Offset = baseOffset + item.Offset,
                        Content = item.Content
                    });
                }
            }

            return new RichTextInputValue(text.ToString(), items);
        }

        public RichTextInputValue GetSelectedValue()
        {
            return GetSelectionValue();
        }

        public RichTextInputSnapshot GetSelectionSnapshot(bool removeObjectReplacementCharacters = false)
        {
            var value = GetSelectionValue();
            var snapshot = new RichTextInputSnapshot(
                value.Text,
                value.Items.Select(item => new RichTextInputSnapshotItem
                {
                    Offset = item.Offset,
                    Kind = item.Content.Kind,
                    DisplayText = item.Content.DisplayText,
                    Source = item.Content.Source,
                    StyleKey = item.Content.StyleKey
                }).ToArray());

            if (!removeObjectReplacementCharacters)
                return snapshot;

            var adjusted = snapshot.Text ?? string.Empty;
            var snapshotItems = snapshot.Items.ToArray();
            foreach (var item in snapshotItems.OrderByDescending(item => item.Offset))
            {
                if (item.Offset >= 0 && item.Offset < adjusted.Length && adjusted[item.Offset] == ObjectReplacementCharacter)
                    adjusted = adjusted.Remove(item.Offset, 1);
            }

            var removedBefore = 0;
            foreach (var item in snapshotItems.OrderBy(item => item.Offset))
            {
                item.Offset -= removedBefore;
                removedBefore++;
            }

            return new RichTextInputSnapshot(adjusted, snapshotItems);
        }

        public void SetValue(RichTextInputValue value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            using (document.RunUpdate())
            using (DeferRedraw())
            using (SuspendInvalidItemValidation())
            {
                _items.Clear();
                InvalidateItemsCache();
                document.Text = value.Text ?? string.Empty;
                foreach (var valueItem in value.Items.OrderBy(item => item.Offset))
                {
                    if (valueItem == null || valueItem.Content == null)
                        continue;

                    if (valueItem.Offset < 0 || valueItem.Offset >= document.TextLength)
                        continue;

                    if (document.GetCharAt(valueItem.Offset) == ObjectReplacementCharacter)
                        AddItem(valueItem.Offset, valueItem.Content);
                }

                _textArea.Caret.Offset = document.TextLength;
                _textArea.ClearSelection();
            }

            RequestRedraw();
        }

        public bool SetSnapshot(RichTextInputSnapshot snapshot)
        {
            if (snapshot == null)
                return false;

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            using (document.RunUpdate())
            using (DeferRedraw())
            using (SuspendInvalidItemValidation())
            {
                _items.Clear();
                InvalidateItemsCache();
                document.Text = snapshot.Text ?? string.Empty;
                foreach (var snapshotItem in (snapshot.Items ?? Array.Empty<RichTextInputSnapshotItem>()).OrderBy(item => item.Offset))
                {
                    if (snapshotItem.Offset < 0
                        || snapshotItem.Offset >= document.TextLength
                        || document.GetCharAt(snapshotItem.Offset) != ObjectReplacementCharacter)
                    {
                        continue;
                    }

                    AddItem(snapshotItem.Offset, CreateContentFromSnapshotItem(snapshotItem));
                }

                _textArea.Caret.Offset = document.TextLength;
                _textArea.ClearSelection();
            }

            RequestRedraw();
            FinalizeCaretAfterInsertion();
            return true;
        }

        public bool SetSerializedSnapshot(string payload)
        {
            RichTextInputSnapshot snapshot;
            return TryDeserializeSnapshot(payload, out snapshot) && SetSnapshot(snapshot);
        }

        public string GetPlainText(ISegment segment = null, Func<RichTextContentItem, string> contentTextFactory = null)
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            segment ??= new SimpleSegment(0, document.TextLength);
            var text = document.GetText(segment);
            var builder = new System.Text.StringBuilder(text);
            var items = GetItemsInRange(segment.Offset, segment.EndOffset, true);

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var relativeOffset = item.Offset - segment.Offset;
                var replacement = contentTextFactory?.Invoke(item) ?? item.Content.DisplayText ?? string.Empty;
                if (relativeOffset >= 0 && relativeOffset < builder.Length && builder[relativeOffset] == ObjectReplacementCharacter)
                {
                    builder.Remove(relativeOffset, 1);
                    builder.Insert(relativeOffset, replacement);
                }
            }

            return builder.ToString();
        }

        public string GetSelectedPlainText(Func<RichTextContentItem, string> contentTextFactory = null)
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            if (_textArea.Selection.IsEmpty)
                return string.Empty;

            var builder = new System.Text.StringBuilder();
            foreach (var segment in GetOrderedSelectionSegments())
            {
                builder.Append(GetPlainText(
                    new SimpleSegment(segment.StartOffset, segment.EndOffset - segment.StartOffset),
                    contentTextFactory));
            }

            return builder.ToString();
        }

        public bool TrySetRichClipboardData(DataObject dataObject, ISegment segment)
        {
            if (dataObject == null)
                throw new ArgumentNullException(nameof(dataObject));
            if (segment == null)
                return false;

            var snapshot = CreateSnapshot(segment, false, true);
            if (snapshot.Items.Count == 0)
                return false;

            var payload = SerializeSnapshot(snapshot);
            RegisterRichClipboardFallback(snapshot, payload);
            dataObject.Set(RichTextClipboardFormat, payload);
            return true;
        }

        private bool InsertSerializedSnapshot(string payload, int offset, bool replaceSelection)
        {
            RichTextInputSnapshot snapshot;
            if (!TryDeserializeSnapshot(payload, out snapshot))
                return false;

            if (snapshot == null)
                return false;

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            using (document.RunUpdate())
            using (DeferRedraw())
            using (SuspendInvalidItemValidation())
            {
                if (replaceSelection && !_textArea.Selection.IsEmpty)
                {
                    _textArea.RemoveSelectedText();
                    offset = _textArea.Caret.Offset;
                }

                document.Insert(offset, snapshot.Text ?? string.Empty, AnchorMovementType.BeforeInsertion);
                foreach (var snapshotItem in snapshot.Items.OrderBy(item => item.Offset))
                {
                    var itemOffset = offset + snapshotItem.Offset;
                    if (itemOffset >= 0
                        && itemOffset < document.TextLength
                        && document.GetCharAt(itemOffset) == ObjectReplacementCharacter)
                    {
                        var content = CreateContentFromSnapshotItem(snapshotItem);
                        var item = AddItem(itemOffset, content);
                        if (document.UndoStack.AcceptChanges)
                            document.UndoStack.Push(new RichTextContentUndoOperation(this, content, itemOffset, true, item));
                    }
                }

                _textArea.Caret.Offset = offset + (snapshot.Text?.Length ?? 0);
                _textArea.ClearSelection();
            }

            FinalizeCaretAfterInsertion();
            return true;
        }

        private static RichTextContent CreateContentFromSnapshotItem(RichTextInputSnapshotItem snapshotItem)
        {
            if (TryGetLiveClipboardContent(snapshotItem.LiveContentKey, out var liveContent))
                return liveContent;

            if (snapshotItem.Kind == RichTextContentKind.Image)
            {
                var bitmap = TryCreateBitmapFromSnapshotItem(snapshotItem);
                if (bitmap != null)
                    return RichTextContent.FromImage(bitmap, snapshotItem.DisplayText, snapshotItem.Source);
            }

            return new RichTextContent(snapshotItem.Kind, snapshotItem.DisplayText, null, snapshotItem.Source, snapshotItem.StyleKey);
        }

        private static string RegisterLiveClipboardContent(RichTextContent content)
        {
            if (content == null)
                return null;

            var key = Guid.NewGuid().ToString("N");
            lock (LiveClipboardContentCacheLock)
            {
                LiveClipboardContentCache[key] = content;
                LiveClipboardContentCacheOrder.Enqueue(key);
                while (LiveClipboardContentCacheOrder.Count > MaxLiveClipboardContentCacheSize)
                    LiveClipboardContentCache.Remove(LiveClipboardContentCacheOrder.Dequeue());
            }

            return key;
        }

        private static bool TryGetLiveClipboardContent(string key, out RichTextContent content)
        {
            content = null;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (LiveClipboardContentCacheLock)
                return LiveClipboardContentCache.TryGetValue(key, out content);
        }

        internal static void ClearLiveClipboardContentCache()
        {
            lock (LiveClipboardContentCacheLock)
            {
                LiveClipboardContentCache.Clear();
                LiveClipboardContentCacheOrder.Clear();
            }
        }

        internal static void ClearRichClipboardFallback()
        {
            lock (RichClipboardFallbackLock)
                _richClipboardFallbackSnapshot = null;
        }

        private static void RegisterRichClipboardFallback(RichTextInputSnapshot snapshot, string payload)
        {
            if (snapshot == null
                || snapshot.Items == null
                || snapshot.Items.Count == 0
                || string.IsNullOrEmpty(payload))
            {
                return;
            }

            lock (RichClipboardFallbackLock)
            {
                _richClipboardFallbackSnapshot = new RichClipboardFallbackSnapshot(
                    payload,
                    NormalizeClipboardTextForComparison(snapshot.Text, false),
                    NormalizeClipboardTextForComparison(snapshot.Text, true),
                    DateTimeOffset.UtcNow);
            }
        }

        private static bool CanRestoreRichClipboardFallback(string text)
        {
            return TryGetRichClipboardFallbackPayload(text, out _);
        }

        private static bool TryGetRichClipboardFallbackPayload(string text, out string payload)
        {
            payload = null;
            if (text == null)
                return false;

            var normalizedText = NormalizeClipboardTextForComparison(text, false);
            var spaceNormalizedText = NormalizeClipboardTextForComparison(text, true);
            lock (RichClipboardFallbackLock)
            {
                var snapshot = _richClipboardFallbackSnapshot;
                if (snapshot == null || snapshot.IsExpired)
                    return false;

                if (string.Equals(normalizedText, snapshot.Text, StringComparison.Ordinal)
                    || string.Equals(spaceNormalizedText, snapshot.TextWithObjectPlaceholdersAsSpaces, StringComparison.Ordinal))
                {
                    payload = snapshot.Payload;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeClipboardTextForComparison(string text, bool replaceObjectReplacementWithSpace)
        {
            if (text == null)
                return null;

            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return replaceObjectReplacementWithSpace
                ? text.Replace(ObjectReplacementCharacter, ' ')
                : text;
        }

        private static string GetDataObjectText(IDataObject dataObject)
        {
            if (dataObject == null || !dataObject.Contains(DataFormats.Text))
                return null;

            try
            {
                return dataObject.Get(DataFormats.Text) as string;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap TryCreateBitmapFromSnapshotItem(RichTextInputSnapshotItem snapshotItem)
        {
            var localPath = TryGetLocalFilePath(snapshotItem.Source);
            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                try
                {
                    return new Bitmap(localPath);
                }
                catch
                {
                }
            }

            if (string.IsNullOrEmpty(snapshotItem.ImageData))
                return null;

            try
            {
                var bytes = Convert.FromBase64String(snapshotItem.ImageData);
                using (var stream = new MemoryStream(bytes))
                    return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryEncodeBitmap(Bitmap bitmap, int maxBytes, out string imageData, out string imageMimeType)
        {
            imageData = null;
            imageMimeType = null;
            if (bitmap == null || maxBytes <= 0)
                return false;

            try
            {
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream);
                    var bytes = stream.ToArray();
                    if (bytes.Length > 0
                        && bytes.Length <= maxBytes
                        && CanDecodeBitmapBytes(bytes))
                    {
                        imageData = Convert.ToBase64String(bytes);
                        imageMimeType = "image/png";
                        return true;
                    }
                }
            }
            catch
            {
            }

            if (TrySaveBitmapToTemporaryPng(bitmap, maxBytes, out var pngBytes))
            {
                imageData = Convert.ToBase64String(pngBytes);
                imageMimeType = "image/png";
                return true;
            }

            if (TryRenderBitmapToPng(bitmap, maxBytes, out var renderedPngBytes))
            {
                imageData = Convert.ToBase64String(renderedPngBytes);
                imageMimeType = "image/png";
                return true;
            }

            try
            {
                var bytes = CreateBmpBytesFromBitmap(bitmap);
                if (bytes.Length <= 0 || bytes.Length > maxBytes)
                    return false;

                imageData = Convert.ToBase64String(bytes);
                imageMimeType = "image/bmp";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRenderBitmapToPng(Bitmap bitmap, int maxBytes, out byte[] bytes)
        {
            bytes = null;
            try
            {
                using (var renderTarget = new RenderTargetBitmap(bitmap.PixelSize, bitmap.Dpi))
                {
                    using (var context = renderTarget.CreateDrawingContext())
                    {
                        var rect = new Rect(bitmap.Size);
                        context.DrawImage(bitmap, rect, rect);
                    }

                    using (var stream = new MemoryStream())
                    {
                        renderTarget.Save(stream);
                        bytes = stream.ToArray();
                    }

                    if (bytes.Length > 0
                        && bytes.Length <= maxBytes
                        && CanDecodeBitmapBytes(bytes))
                    {
                        return true;
                    }

                    return TrySaveBitmapToTemporaryPng(renderTarget, maxBytes, out bytes);
                }
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        private static bool TrySaveBitmapToTemporaryPng(Bitmap bitmap, int maxBytes, out byte[] bytes)
        {
            bytes = null;
            var fileName = Path.Combine(Path.GetTempPath(), "AvaloniaEdit.RichTextInput." + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                bitmap.Save(fileName);
                bytes = File.ReadAllBytes(fileName);
                return bytes.Length > 0
                    && bytes.Length <= maxBytes
                    && CanDecodeBitmapBytes(bytes);
            }
            catch
            {
                bytes = null;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(fileName))
                        File.Delete(fileName);
                }
                catch
                {
                }
            }
        }

        private static bool CanDecodeBitmapBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return false;

            try
            {
                using (var stream = new MemoryStream(bytes))
                using (var decoded = new Bitmap(stream))
                    return decoded.PixelSize.Width > 0 && decoded.PixelSize.Height > 0;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] CreateBmpBytesFromBitmap(Bitmap bitmap)
        {
            var width = bitmap.PixelSize.Width;
            var height = bitmap.PixelSize.Height;
            if (width <= 0 || height <= 0)
                return Array.Empty<byte>();

            var rowBytes = checked(width * 4);
            var pixelBytes = checked(rowBytes * height);
            var pointer = Marshal.AllocHGlobal(pixelBytes);
            try
            {
                CopyBitmapPixelsToBgra8888(bitmap, pointer, pixelBytes, rowBytes);

                var fileSize = 14 + 40 + pixelBytes;
                var bytes = new byte[fileSize];
                bytes[0] = (byte)'B';
                bytes[1] = (byte)'M';
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2, 4), fileSize);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10, 4), 54);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14, 4), 40);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), width);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), -height);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26, 2), 1);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28, 2), 32);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34, 4), pixelBytes);
                Marshal.Copy(pointer, bytes, 54, pixelBytes);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static void CopyBitmapPixelsToBgra8888(Bitmap bitmap, IntPtr pointer, int pixelBytes, int rowBytes)
        {
            using (var framebuffer = new MemoryLockedFramebuffer(
                pointer,
                bitmap.PixelSize,
                rowBytes,
                bitmap.Dpi,
                PixelFormat.Bgra8888,
                AlphaFormat.Premul))
            {
                if (TryCopyPixelsWithFramebuffer(bitmap, framebuffer))
                    return;
            }

            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pointer, pixelBytes, rowBytes);
        }

        private static bool TryCopyPixelsWithFramebuffer(Bitmap bitmap, ILockedFramebuffer framebuffer)
        {
            var method = typeof(Bitmap).GetMethod(
                nameof(Bitmap.CopyPixels),
                new[] { typeof(ILockedFramebuffer), typeof(AlphaFormat) });
            if (method == null)
                return false;

            try
            {
                method.Invoke(bitmap, new object[] { framebuffer, AlphaFormat.Premul });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasReadableImageSource(string source)
        {
            var localPath = TryGetLocalFilePath(source);
            return !string.IsNullOrEmpty(localPath) && File.Exists(localPath);
        }

        private static string TryGetLocalFilePath(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
                return uri.LocalPath;

            return source;
        }

        private static string SerializeSnapshot(RichTextInputSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("AvaloniaEdit.RichTextInput/1");
            builder.AppendLine(Encode(snapshot.Text));
            foreach (var item in snapshot.Items)
            {
                builder.Append(item.Offset);
                builder.Append('|');
                builder.Append((int)item.Kind);
                builder.Append('|');
                builder.Append(Encode(item.DisplayText));
                builder.Append('|');
                builder.Append(Encode(item.Source));
                builder.Append('|');
                builder.Append(Encode(item.StyleKey));
                builder.Append('|');
                builder.Append(Encode(item.LiveContentKey));
                builder.Append('|');
                builder.Append(Encode(item.ImageMimeType));
                builder.Append('|');
                builder.Append(Encode(item.ImageData));
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static bool TryDeserializeSnapshot(string payload, out RichTextInputSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrEmpty(payload))
                return false;

            var lines = payload.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 2 || lines[0] != "AvaloniaEdit.RichTextInput/1")
                return false;

            var items = new List<RichTextInputSnapshotItem>();
            for (var i = 2; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i]))
                    continue;

                var parts = lines[i].Split('|');
                if ((parts.Length < 4 || parts.Length > 8)
                    || !int.TryParse(parts[0], out var itemOffset)
                    || !int.TryParse(parts[1], out var kind))
                    return false;

                items.Add(new RichTextInputSnapshotItem
                {
                    Offset = itemOffset,
                    Kind = (RichTextContentKind)kind,
                    DisplayText = Decode(parts[2]),
                    Source = Decode(parts[3]),
                    StyleKey = parts.Length > 4 ? Decode(parts[4]) : null,
                    LiveContentKey = parts.Length > 5 ? Decode(parts[5]) : null,
                    ImageMimeType = parts.Length > 6 ? Decode(parts[6]) : null,
                    ImageData = parts.Length > 7 ? Decode(parts[7]) : null
                });
            }

            snapshot = new RichTextInputSnapshot(Decode(lines[1]), items);
            return true;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool InsertContents(int offset, bool replaceSelection, IList<RichTextContent> contents)
        {
            var contentList = NormalizeContents(contents);
            if (contentList.Count == 0)
                return false;

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            using (document.RunUpdate())
            using (DeferRedraw())
            using (SuspendInvalidItemValidation())
            {
                if (replaceSelection && !_textArea.Selection.IsEmpty)
                {
                    _textArea.RemoveSelectedText();
                    offset = _textArea.Caret.Offset;
                }

                InsertContentsCore(offset, contentList);
                offset += contentList.Count;
            }

            _textArea.Caret.Offset = offset;
            _textArea.ClearSelection();
            FinalizeCaretAfterInsertion();
            return true;
        }

        private RichTextContentItem InsertContentCore(int offset, RichTextContent content)
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            document.Insert(offset, ObjectReplacementString, AnchorMovementType.BeforeInsertion);
            var item = AddItem(offset, content);
            if (document.UndoStack.AcceptChanges)
                document.UndoStack.Push(new RichTextContentUndoOperation(this, content, offset, true, item));
            return item;
        }

        private IReadOnlyList<RichTextContentItem> InsertContentsCore(int offset, IList<RichTextContent> contents)
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            document.Insert(offset, new string(ObjectReplacementCharacter, contents.Count), AnchorMovementType.BeforeInsertion);

            var items = new List<RichTextContentItem>(contents.Count);
            for (var i = 0; i < contents.Count; i++)
            {
                var itemOffset = offset + i;
                var content = contents[i];
                var item = AddItem(itemOffset, content);
                items.Add(item);
                if (document.UndoStack.AcceptChanges)
                    document.UndoStack.Push(new RichTextContentUndoOperation(this, content, itemOffset, true, item));
            }

            return items;
        }

        private static IList<RichTextContent> NormalizeContents(IEnumerable<RichTextContent> contents)
        {
            return contents?
                .Where(content => content != null)
                .ToArray() ?? Array.Empty<RichTextContent>();
        }

        private bool InsertText(int offset, bool replaceSelection, string text)
        {
            if (text == null)
                return false;

            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            using (document.RunUpdate())
            {
                if (replaceSelection && !_textArea.Selection.IsEmpty)
                {
                    _textArea.RemoveSelectedText();
                    offset = _textArea.Caret.Offset;
                }

                document.Insert(offset, text);
                _textArea.Caret.Offset = offset + text.Length;
                _textArea.ClearSelection();
            }

            FinalizeCaretAfterInsertion();
            return true;
        }

        private void FinalizeCaretAfterInsertion()
        {
            _textArea.Caret.ResetVisualColumn();
            _textArea.Focus();
            _textArea.Caret.BringCaretToView();
        }

        private async Task<RichTextContent> CreateContentForFileAsync(IStorageItem file)
        {
            if (file == null)
                return null;

            var context = new RichTextFileImportContext(
                this,
                file,
                null,
                file is IStorageFolder,
                () => CreateDefaultContentForFileAsync(file));
            if (FileContentImporter != null)
                return await FileContentImporter(context);

            return await context.CreateDefaultContentAsync();
        }

        private async Task<RichTextContent> CreateDefaultContentForFileAsync(IStorageItem file)
        {
            if (file is IStorageFolder)
                return RichTextContent.FromFolder(file);

            if (ConvertImageFilesToImages && file is IStorageFile storageFile && IsImageFileName(file.Name))
            {
                var bitmap = await TryLoadBitmapAsync(storageFile);
                if (bitmap != null)
                    return RichTextContent.FromImage(bitmap, file.Name, file.Path?.ToString());
            }

            return RichTextContent.FromFile(file);
        }

        private async Task<RichTextContent> CreateContentForFileNameAsync(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            var context = new RichTextFileImportContext(
                this,
                null,
                fileName,
                Directory.Exists(fileName),
                () => Task.FromResult(CreateDefaultContentForFileName(fileName)));
            if (FileContentImporter != null)
                return await FileContentImporter(context);

            return await context.CreateDefaultContentAsync();
        }

        private RichTextContent CreateDefaultContentForFileName(string fileName)
        {
            if (Directory.Exists(fileName))
                return RichTextContent.FromFolderName(fileName);

            if (ConvertImageFilesToImages && IsImageFileName(fileName) && File.Exists(fileName))
            {
                try
                {
                    return RichTextContent.FromImage(new Bitmap(fileName), Path.GetFileName(fileName), fileName);
                }
                catch
                {
                }
            }

            return RichTextContent.FromFileName(fileName);
        }

        public static bool IsImageFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return !string.IsNullOrEmpty(extension) && ImageExtensions.Contains(extension);
        }

        public static bool IsExecutableFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return !string.IsNullOrEmpty(extension) && ExecutableExtensions.Contains(extension);
        }

        public static bool IsArchiveFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return !string.IsNullOrEmpty(extension) && ArchiveExtensions.Contains(extension);
        }

        public static bool IsDocumentFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return !string.IsNullOrEmpty(extension) && DocumentExtensions.Contains(extension);
        }

        private static async Task<Bitmap> TryLoadBitmapAsync(IStorageFile file)
        {
            try
            {
                using (var stream = await file.OpenReadAsync())
                {
                    return new Bitmap(stream);
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool ContainsBitmapData(IDataObject dataObject)
        {
            if (dataObject == null)
                return false;

            var formats = dataObject.GetDataFormats() ?? Array.Empty<string>();
            return formats.Any(IsBitmapDataFormat);
        }

        private static Bitmap TryGetBitmap(IDataObject dataObject)
        {
            if (dataObject == null)
                return null;

            var dataFormats = dataObject.GetDataFormats() ?? Array.Empty<string>();
            var formats = dataFormats
                .Concat(BitmapDataFormats)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(IsBitmapDataFormat)
                .ToArray();

            foreach (var format in formats)
            {
                try
                {
                    var value = dataObject.Get(format);
                    var bitmap = TryCreateBitmapFromClipboardData(value, format);
                    if (bitmap != null)
                        return bitmap;
                }
                catch
                {
                }
            }

            return null;
        }

        private static Bitmap TryCreateBitmap(Stream stream, bool isDib)
        {
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return TryCreateBitmap(memory.ToArray(), isDib);
            }
        }

        private static Bitmap TryCreateBitmap(byte[] bytes, bool isDib)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            if (isDib && !HasBmpFileHeader(bytes))
            {
                var bmpBytes = CreateBmpBytesFromDib(bytes);
                using (var memory = new MemoryStream(bmpBytes))
                    return new Bitmap(memory);
            }

            using (var memory = new MemoryStream(bytes))
                return new Bitmap(memory);
        }

        private static Bitmap TryCreateBitmapFromClipboardData(object value, string format)
        {
            try
            {
                switch (value)
                {
                    case Bitmap bitmap:
                        return bitmap;
                    case Stream stream:
                    {
                        if (stream.CanSeek)
                            stream.Position = 0;
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            return TryCreateBitmapFromClipboardBytes(memory.ToArray(), format);
                        }
                    }
                    case byte[] bytes:
                        return TryCreateBitmapFromClipboardBytes(bytes, format);
                }
            }
            catch
            {
            }

            return null;
        }

        private static Bitmap TryCreateBitmapFromClipboardBytes(byte[] bytes, string format)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            try
            {
                return TryCreateBitmap(bytes, IsDibDataFormat(format));
            }
            catch
            {
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || !IsBitmapDataFormat(format))
                return null;

            try
            {
                var pngBytes = MacPasteboard.TryConvertImageBytesToPng(bytes);
                return pngBytes == null ? null : TryCreateBitmap(pngBytes, false);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasBmpFileHeader(byte[] bytes)
        {
            return bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';
        }

        private static byte[] CreateBmpBytesFromDib(byte[] dibBytes)
        {
            if (dibBytes.Length < 4)
                throw new InvalidDataException("DIB data is too small.");

            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dibBytes.AsSpan(0, 4));
            if (headerSize <= 0 || headerSize > dibBytes.Length)
                throw new InvalidDataException("Invalid DIB header size.");

            var colorTableSize = GetDibColorTableSize(dibBytes, headerSize);
            var pixelOffset = 14 + headerSize + colorTableSize;
            var fileSize = 14 + dibBytes.Length;
            var bmpBytes = new byte[fileSize];
            bmpBytes[0] = (byte)'B';
            bmpBytes[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(bmpBytes.AsSpan(2, 4), fileSize);
            BinaryPrimitives.WriteInt32LittleEndian(bmpBytes.AsSpan(10, 4), pixelOffset);
            Buffer.BlockCopy(dibBytes, 0, bmpBytes, 14, dibBytes.Length);
            return bmpBytes;
        }

        private static int GetDibColorTableSize(byte[] dibBytes, int headerSize)
        {
            if (headerSize == 12)
            {
                if (dibBytes.Length < 12)
                    return 0;

                var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(dibBytes.AsSpan(10, 2));
                return bitsPerPixel <= 8 ? (1 << bitsPerPixel) * 3 : 0;
            }

            if (dibBytes.Length < 36)
                return 0;

            var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dibBytes.AsSpan(14, 2));
            var compression = BinaryPrimitives.ReadUInt32LittleEndian(dibBytes.AsSpan(16, 4));
            var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dibBytes.AsSpan(32, 4));
            var colorCount = colorsUsed != 0 || bitCount > 8 ? colorsUsed : 1u << bitCount;
            var maskSize = headerSize == 40
                ? compression == 6 ? 16 : compression == 3 ? 12 : 0
                : 0;
            return checked((int)colorCount * 4 + maskSize);
        }

        private static bool IsDibDataFormat(string format)
        {
            return string.Equals(format, "DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase)
                || string.Equals(format, "CF_DIB", StringComparison.OrdinalIgnoreCase)
                || string.Equals(format, "CF_DIBV5", StringComparison.OrdinalIgnoreCase)
                || string.Equals(format, "Format17", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBitmapDataFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
                return false;

            return BitmapDataFormats.Any(known =>
                string.Equals(known, format, StringComparison.OrdinalIgnoreCase));
        }

        private void TextArea_DocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            DetachFromDocument(e.OldDocument);
            _items.Clear();
            InvalidateItemsCache();
            AttachToDocument(e.NewDocument);
            RequestRedraw();
        }

        private void TextView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_items.Count > 0)
                _textArea.TextView.Redraw();
        }

        private void TextArea_SelectionChanged(object sender, EventArgs e)
        {
            if (_items.Count > 0)
                ContentSelectionChanged?.Invoke(this, new RichTextContentChangedEventArgs(null));
        }

        private void TextArea_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || e.Key != Key.Enter)
                return;

            if ((e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None)
                return;

            e.Handled = HandleSelectedContentEnterKey() || HandleEnterKey();
        }

        internal bool HandleEnterKey()
        {
            switch (EnterKeyBehavior)
            {
                case RichTextEnterKeyBehavior.KeepDefault:
                    return false;
                case RichTextEnterKeyBehavior.PlainNewLine:
                    InsertPlainNewLine();
                    return true;
                case RichTextEnterKeyBehavior.PlainNewLineWhenAdjacentToContent:
                    return HandleCaretAdjacentContentEnterKey();
                default:
                    return false;
            }
        }

        internal bool HandleSelectedContentEnterKey()
        {
            if (SelectedContentEnterBehavior == RichTextSelectedContentEnterBehavior.KeepDefault
                || !TryGetSingleSelectedContent(out var item))
            {
                return false;
            }

            switch (SelectedContentEnterBehavior)
            {
                case RichTextSelectedContentEnterBehavior.MoveCaretAfterContent:
                    _textArea.ClearSelection();
                    _textArea.Caret.Offset = item.EndOffset;
                    FinalizeCaretAfterInsertion();
                    return true;
                case RichTextSelectedContentEnterBehavior.InsertNewLineBeforeContent:
                {
                    var offset = item.Offset;
                    _textArea.ClearSelection();
                    InsertNewLineAt(offset);
                    return true;
                }
                case RichTextSelectedContentEnterBehavior.InsertNewLineAfterContent:
                {
                    var offset = item.EndOffset;
                    _textArea.ClearSelection();
                    InsertNewLineAt(offset);
                    return true;
                }
                default:
                    return false;
            }
        }

        internal bool HandleCaretAdjacentContentEnterKey()
        {
            if (!_textArea.Selection.IsEmpty || _textArea.Document == null)
                return false;

            var offset = Math.Max(0, Math.Min(_textArea.Caret.Offset, _textArea.Document.TextLength));
            if (TryGetItem(offset, out _) || TryGetItem(offset - ObjectReplacementString.Length, out _))
            {
                InsertNewLineAt(offset);
                return true;
            }

            return false;
        }

        private void InsertPlainNewLine()
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            using (document.RunUpdate())
            {
                if (!_textArea.Selection.IsEmpty)
                    _textArea.RemoveSelectedText();

                InsertNewLineAt(_textArea.Caret.Offset);
            }
        }

        private void InsertNewLineAt(int offset)
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            var line = document.GetLineByOffset(Math.Min(offset, document.TextLength));
            var newLine = TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
            document.Insert(offset, newLine);
            _textArea.Caret.Offset = offset + newLine.Length;
            FinalizeCaretAfterInsertion();
        }

        private bool TryGetSingleSelectedContent(out RichTextContentItem selectedItem)
        {
            selectedItem = null;
            if (_textArea.Selection.IsEmpty)
                return false;

            var segment = _textArea.Selection.SurroundingSegment;
            var items = GetSelectedItems();
            if (items.Count != 1)
                return false;

            var item = items[0];
            if (segment.Offset != item.Offset || segment.EndOffset != item.EndOffset)
                return false;

            selectedItem = item;
            return true;
        }

        private double GetConstrainedInlineWidth()
        {
            var textViewWidth = _textArea.TextView.Bounds.Width;
            if (double.IsNaN(textViewWidth) || double.IsInfinity(textViewWidth) || textViewWidth <= 0)
                return MaxInlineElementWidth;

            var available = Math.Max(MinInlineElementWidth, textViewWidth - 40);
            return Math.Min(MaxInlineElementWidth, available);
        }

        private void AttachToDocument(TextDocument document)
        {
            if (document != null)
            {
                TextDocumentWeakEventManager.Changing.AddHandler(document, Document_Changing);
                TextDocumentWeakEventManager.Changed.AddHandler(document, Document_Changed);
            }
        }

        private void DetachFromDocument(TextDocument document)
        {
            if (document != null)
            {
                TextDocumentWeakEventManager.Changing.RemoveHandler(document, Document_Changing);
                TextDocumentWeakEventManager.Changed.RemoveHandler(document, Document_Changed);
            }
        }

        private void Document_Changing(object sender, DocumentChangeEventArgs e)
        {
            _richContentChangedDuringDocumentChange = false;
            if (e.RemovalLength == 0 || _items.Count == 0)
                return;

            var document = _textArea.Document;
            var removedItems = GetItemsInRange(e.Offset, e.Offset + e.RemovalLength)
                .ToArray();
            var reanchorOffsets = GetReanchorOffsets(e, removedItems.Length);
            for (var i = 0; i < removedItems.Length; i++)
            {
                var item = removedItems[i];
                if (i < reanchorOffsets.Count && _items.Remove(item))
                {
                    _richContentChangedDuringDocumentChange = true;
                    InvalidateItemsCache();
                    _pendingReanchors.Add(new PendingRichContentReanchor(item.Content, e.Offset + reanchorOffsets[i]));
                    continue;
                }

                RemoveItem(item);
                _richContentChangedDuringDocumentChange = true;
                if (document?.UndoStack.AcceptChanges == true)
                    document.UndoStack.Push(new RichTextContentUndoOperation(this, item.Content, item.Offset, false, item));
            }
        }

        private void Document_Changed(object sender, DocumentChangeEventArgs e)
        {
            var reanchored = ApplyPendingReanchors();
            if (IsInvalidItemValidationSuspended())
            {
                if (_richContentChangedDuringDocumentChange || reanchored || ShouldRebuildRichContentVisuals(e))
                    RequestRedraw();
                _richContentChangedDuringDocumentChange = false;
                return;
            }

            if (_richContentChangedDuringDocumentChange || reanchored || ShouldRebuildRichContentVisuals(e))
                RequestRedraw();
            _richContentChangedDuringDocumentChange = false;
        }

        private IReadOnlyList<int> GetReanchorOffsets(DocumentChangeEventArgs e, int removedItemCount)
        {
            if (removedItemCount == 0 || e.RemovalLength <= 1 || e.InsertionLength <= 1)
                return Array.Empty<int>();

            var offsets = new List<int>();
            for (var i = 0; i < e.InsertedText.TextLength; i++)
            {
                if (e.InsertedText.GetCharAt(i) == ObjectReplacementCharacter)
                    offsets.Add(i);
            }

            if (offsets.Count == 0)
                return Array.Empty<int>();

            if (offsets.Count > removedItemCount)
                offsets.RemoveRange(removedItemCount, offsets.Count - removedItemCount);

            return offsets;
        }

        private bool ApplyPendingReanchors()
        {
            if (_pendingReanchors.Count == 0)
                return false;

            var document = _textArea.Document;
            var reanchored = false;
            foreach (var pending in _pendingReanchors)
            {
                if (document != null
                    && pending.Offset >= 0
                    && pending.Offset < document.TextLength
                    && document.GetCharAt(pending.Offset) == ObjectReplacementCharacter)
                {
                    AddItem(pending.Offset, pending.Content, false);
                    reanchored = true;
                }
            }

            _pendingReanchors.Clear();
            return reanchored;
        }

        private bool ShouldRebuildRichContentVisuals(DocumentChangeEventArgs e)
        {
            return _items.Count > 0
                && (ContainsLineBreak(e.InsertedText) || ContainsLineBreak(e.RemovedText));
        }

        private static bool ContainsLineBreak(ITextSource text)
        {
            if (text == null || text.TextLength == 0)
                return false;

            for (var i = 0; i < text.TextLength; i++)
            {
                var c = text.GetCharAt(i);
                if (c == '\r' || c == '\n')
                    return true;
            }

            return false;
        }

        private bool RemoveInvalidItems()
        {
            InvalidItemValidationPassCount++;
            var document = _textArea.Document;
            if (document == null || _items.Count == 0)
                return false;

            var removed = false;
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (item.Length != ObjectReplacementString.Length
                    || item.Offset < 0
                    || item.Offset >= document.TextLength
                    || document.GetCharAt(item.Offset) != ObjectReplacementCharacter)
                {
                    _items.RemoveAt(i);
                    InvalidateItemsCache();
                    removed = true;
                }
            }

            return removed;
        }

        private IReadOnlyList<RichTextContentItem> GetItemsInRange(int startOffset, int endOffset)
        {
            return GetItemsInRange(startOffset, endOffset, false);
        }

        private IReadOnlyList<RichTextContentItem> GetItemsInRange(int startOffset, int endOffset, bool validate)
        {
            if (startOffset >= endOffset || _items.Count == 0)
                return Array.Empty<RichTextContentItem>();

            if (validate)
                RemoveInvalidItems();

            if (_items.Count == 0)
                return Array.Empty<RichTextContentItem>();

            var items = GetOrderedItemsCache();
            var index = FindItemIndexAtOrAfter(startOffset);
            if (index < 0 || index >= items.Length)
                return Array.Empty<RichTextContentItem>();

            var result = new List<RichTextContentItem>();
            for (var i = index; i < items.Length && items[i].Offset < endOffset; i++)
                result.Add(items[i]);

            return result;
        }

        private IReadOnlyList<SelectionSegment> GetOrderedSelectionSegments()
        {
            return _textArea.Selection.Segments
                .OrderBy(segment => segment.StartOffset)
                .ToArray();
        }

        private IDisposable DeferRedraw()
        {
            _deferRedrawCount++;
            return new CallbackOnDispose(() =>
            {
                _deferRedrawCount--;
                if (_deferRedrawCount == 0 && _redrawPending)
                {
                    _redrawPending = false;
                    _textArea.TextView.Redraw();
                }
            });
        }

        private IDisposable SuspendInvalidItemValidation()
        {
            _suspendInvalidItemValidationCount++;
            return new CallbackOnDispose(() => _suspendInvalidItemValidationCount--);
        }

        private bool IsInvalidItemValidationSuspended()
        {
            return _suspendInvalidItemValidationCount > 0;
        }

        private void RequestRedraw()
        {
            if (_deferRedrawCount > 0)
            {
                _redrawPending = true;
                return;
            }

            _textArea.TextView.Redraw();
        }

        private void InvalidateItemsCache()
        {
            _orderedItemsCache = null;
        }

        private RichTextContentItem[] GetOrderedItemsCache()
        {
            if (_orderedItemsCache == null)
            {
                _orderedItemsCache = _items.OrderBy(item => item.Offset).ToArray();
                ItemsCacheBuildCount++;
            }

            return _orderedItemsCache;
        }

        private int FindItemIndexAtOrAfter(int offset)
        {
            var items = GetOrderedItemsCache();
            var low = 0;
            var high = items.Length - 1;
            var result = -1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                if (items[middle].Offset >= offset)
                {
                    result = middle;
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return result;
        }

        private RichTextContentItem AddItem(int offset, RichTextContent content, bool raiseEvent = true)
        {
            var document = _textArea.Document ?? throw ThrowUtil.NoDocumentAssigned();
            var item = new RichTextContentItem(content, new AnchorSegment(document, offset, ObjectReplacementString.Length));
            _items.Add(item);
            InvalidateItemsCache();
            if (raiseEvent)
                ContentInserted?.Invoke(this, new RichTextContentChangedEventArgs(item));
            RequestRedraw();
            return item;
        }

        private bool CanRemove(RichTextContentItem item)
        {
            if (CanRemoveContent?.Invoke(item) == false)
                return false;

            var args = new RichTextContentRemovingEventArgs(item);
            ContentRemoving?.Invoke(this, args);
            return !args.Cancel;
        }

        private void RemoveItem(RichTextContentItem item)
        {
            if (item != null && _items.Remove(item))
            {
                InvalidateItemsCache();
                ContentRemoved?.Invoke(this, new RichTextContentChangedEventArgs(item));
                RequestRedraw();
            }
        }

        public void SelectContent(RichTextContentItem item)
        {
            if (item == null || !_items.Contains(item) || _textArea.Document == null)
                return;

            _textArea.Focus();
            _textArea.Selection = Selection.Create(_textArea, item.Offset, item.Offset + item.Length);
            _textArea.Caret.Offset = item.Offset + item.Length;
        }

        public bool IsContentSelected(RichTextContentItem item)
        {
            if (item == null || _textArea.Selection.IsEmpty)
                return false;

            return _textArea.Selection.Segments.Any(segment =>
                item.Offset >= segment.StartOffset && item.Offset + item.Length <= segment.EndOffset);
        }

        internal RichTextContentPointerEventArgs RaiseContentPointerPressed(RichTextContentItem item, RoutedEventArgs routedEventArgs)
        {
            var args = new RichTextContentPointerEventArgs(this, item, routedEventArgs, RichTextContentPointerEventKind.PointerPressed);
            ContentPointerPressed?.Invoke(this, args);
            return args;
        }

        internal RichTextContentPointerEventArgs RaiseContentPointerReleased(RichTextContentItem item, RoutedEventArgs routedEventArgs)
        {
            var args = new RichTextContentPointerEventArgs(this, item, routedEventArgs, RichTextContentPointerEventKind.PointerReleased);
            ContentPointerReleased?.Invoke(this, args);
            return args;
        }

        internal RichTextContentPointerEventArgs RaiseContentDoubleTapped(RichTextContentItem item, RoutedEventArgs routedEventArgs)
        {
            var args = new RichTextContentPointerEventArgs(this, item, routedEventArgs, RichTextContentPointerEventKind.DoubleTapped);
            ContentDoubleTapped?.Invoke(this, args);
            return args;
        }

        internal RichTextContentPointerEventArgs RaiseContentContextRequested(RichTextContentItem item, RoutedEventArgs routedEventArgs)
        {
            var args = new RichTextContentPointerEventArgs(this, item, routedEventArgs, RichTextContentPointerEventKind.ContextRequested);
            ContentContextRequested?.Invoke(this, args);
            return args;
        }

        internal bool ShouldHandleContentPointerEvent(RichTextContentPointerEventArgs args)
        {
            return ContentPointerHandledSelector?.Invoke(args) ?? HandleContentPointerEvents;
        }

        internal void ApplyContentPointerSelection(RichTextContentPointerEventArgs args)
        {
            if (args == null || args.EventKind != RichTextContentPointerEventKind.PointerPressed)
                return;

            var behavior = ContentPointerSelectionBehaviorSelector?.Invoke(args)
                ?? (SelectContentOnPointerPressed
                    ? ContentPointerSelectionBehavior
                    : RichTextContentPointerSelectionBehavior.None);

            switch (behavior)
            {
                case RichTextContentPointerSelectionBehavior.SelectContent:
                    SelectContent(args.Item);
                    break;
                case RichTextContentPointerSelectionBehavior.PreserveSelection:
                    if (!IsContentSelected(args.Item))
                        SelectContent(args.Item);
                    break;
                case RichTextContentPointerSelectionBehavior.ExtendSelection:
                    ExtendSelectionToContent(args.Item);
                    break;
            }
        }

        private void ExtendSelectionToContent(RichTextContentItem item)
        {
            if (item == null || !_items.Contains(item) || _textArea.Document == null)
                return;

            var anchorOffset = _textArea.Selection.IsEmpty
                ? _textArea.Caret.Offset
                : _textArea.Selection.SurroundingSegment.Offset;
            _textArea.Focus();
            _textArea.Selection = Selection.Create(_textArea, anchorOffset, item.EndOffset);
            _textArea.Caret.Offset = item.EndOffset;
        }

        private static class MacPasteboard
        {
            private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
            private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
            private const string AppKitFramework = "/System/Library/Frameworks/AppKit.framework/AppKit";
            private const int RtldNow = 2;
            private static readonly UIntPtr NSBitmapImageFileTypePng = new UIntPtr(4);

            public static byte[] TryConvertImageBytesToPng(byte[] bytes)
            {
                if (bytes == null || bytes.Length == 0)
                    return null;

                try
                {
                    var dataClass = GetObjectiveCClass("NSData");
                    var imageRepClass = GetObjectiveCClass("NSBitmapImageRep");
                    var dictionaryClass = GetObjectiveCClass("NSDictionary");
                    if (dataClass == IntPtr.Zero || imageRepClass == IntPtr.Zero || dictionaryClass == IntPtr.Zero)
                        return null;

                    var data = CreateNSData(dataClass, bytes);
                    if (data == IntPtr.Zero)
                        return null;

                    var imageRep = IntPtr_objc_msgSend_IntPtr(imageRepClass, sel_registerName("imageRepWithData:"), data);
                    if (imageRep == IntPtr.Zero)
                        return null;

                    var properties = IntPtr_objc_msgSend(dictionaryClass, sel_registerName("dictionary"));
                    var pngData = IntPtr_objc_msgSend_UIntPtr_IntPtr(
                        imageRep,
                        sel_registerName("representationUsingType:properties:"),
                        NSBitmapImageFileTypePng,
                        properties);

                    return pngData == IntPtr.Zero ? null : CopyNSDataBytes(pngData);
                }
                catch
                {
                    return null;
                }
            }

            private static IntPtr GetObjectiveCClass(string name)
            {
                var cls = objc_getClass(name);
                if (cls != IntPtr.Zero)
                    return cls;

                dlopen(AppKitFramework, RtldNow);
                return objc_getClass(name);
            }

            private static IntPtr CreateNSData(IntPtr dataClass, byte[] bytes)
            {
                var pointer = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                    return IntPtr_objc_msgSend_IntPtr_UIntPtr(
                        dataClass,
                        sel_registerName("dataWithBytes:length:"),
                        pointer,
                        new UIntPtr((uint)bytes.Length));
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            private static byte[] CopyNSDataBytes(IntPtr data)
            {
                var length = UIntPtr_objc_msgSend(data, sel_registerName("length")).ToUInt64();
                if (length == 0 || length > int.MaxValue)
                    return null;

                var bytesPointer = IntPtr_objc_msgSend(data, sel_registerName("bytes"));
                if (bytesPointer == IntPtr.Zero)
                    return null;

                var bytes = new byte[(int)length];
                Marshal.Copy(bytesPointer, bytes, 0, bytes.Length);
                return bytes;
            }

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
            private static extern IntPtr objc_getClass(string name);

            [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
            private static extern IntPtr sel_registerName(string name);

            [DllImport(SystemLibrary, EntryPoint = "dlopen")]
            private static extern IntPtr dlopen(string path, int mode);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            private static extern IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            private static extern IntPtr IntPtr_objc_msgSend_IntPtr_UIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, UIntPtr arg2);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            private static extern IntPtr IntPtr_objc_msgSend_UIntPtr_IntPtr(IntPtr receiver, IntPtr selector, UIntPtr arg1, IntPtr arg2);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            private static extern UIntPtr UIntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);
        }

        private sealed class MemoryLockedFramebuffer : ILockedFramebuffer
        {
            public MemoryLockedFramebuffer(
                IntPtr address,
                PixelSize size,
                int rowBytes,
                Vector dpi,
                PixelFormat format,
                AlphaFormat alphaFormat)
            {
                Address = address;
                Size = size;
                RowBytes = rowBytes;
                Dpi = dpi;
                Format = format;
                AlphaFormat = alphaFormat;
            }

            public IntPtr Address { get; }

            public PixelSize Size { get; }

            public int RowBytes { get; }

            public Vector Dpi { get; }

            public PixelFormat Format { get; }

            public AlphaFormat AlphaFormat { get; }

            public void Dispose()
            {
            }
        }

        private sealed class RichTextContentUndoOperation : IUndoableOperation
        {
            private readonly RichTextInputManager _manager;
            private readonly RichTextContent _content;
            private readonly int _offset;
            private readonly bool _isInsertion;
            private RichTextContentItem _item;

            public RichTextContentUndoOperation(RichTextInputManager manager, RichTextContent content, int offset, bool isInsertion, RichTextContentItem item)
            {
                _manager = manager;
                _content = content;
                _offset = offset;
                _isInsertion = isInsertion;
                _item = item;
            }

            public void Undo()
            {
                if (_isInsertion)
                    _manager.RemoveItem(_item);
                else
                    _item = _manager.AddItem(_offset, _content);
            }

            public void Redo()
            {
                if (_isInsertion)
                    _item = _manager.AddItem(_offset, _content);
                else
                    _manager.RemoveItem(_item);
            }
        }

        private sealed class PendingRichContentReanchor
        {
            public PendingRichContentReanchor(RichTextContent content, int offset)
            {
                Content = content;
                Offset = offset;
            }

            public RichTextContent Content { get; }

            public int Offset { get; }
        }

        public static Control CreateDefaultElement(RichTextContentItem item)
        {
            return CreateDefaultElement(item, 220, 180, 120);
        }

        private static Control CreateDefaultElement(RichTextContentItem item, double maxInlineWidth, double maxImageWidth, double maxImageHeight)
        {
            if (item.Content.Kind == RichTextContentKind.Image && item.Content.Value is Bitmap bitmap)
                return CreateImageElement(bitmap, item.Content.DisplayText, Math.Min(maxImageWidth, maxInlineWidth), maxImageHeight);

            if (item.Content.Kind == RichTextContentKind.Emoji)
                return CreateEmojiElement(item.Content.DisplayText);

            return CreateFileLikeElement(item.Content, maxInlineWidth);
        }

        private static Control CreateImageElement(Bitmap bitmap, string displayText, double maxWidth, double maxHeight)
        {
            var width = Math.Min(maxWidth, Math.Max(48, bitmap.Size.Width));
            var height = Math.Min(maxHeight, Math.Max(48, bitmap.Size.Height));
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(3),
                Margin = new Thickness(2, 1),
                Child = new Image
                {
                    Source = bitmap,
                    Width = width,
                    Height = height,
                    Stretch = Stretch.Uniform,
                    [ToolTip.TipProperty] = displayText
                }
            };
        }

        private static Control CreateEmojiElement(string emoji)
        {
            return new TextBlock
            {
                Text = emoji,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0)
            };
        }

        private static Control CreateFileLikeElement(RichTextContent content, double maxInlineWidth)
        {
            var icon = content.Kind == RichTextContentKind.File
                ? "FILE"
                : content.Kind == RichTextContentKind.Folder
                    ? "DIR"
                    : content.Kind.ToString().ToUpperInvariant();
            var title = string.IsNullOrWhiteSpace(content.DisplayText) ? content.Kind.ToString() : content.DisplayText;
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 4),
                Margin = new Thickness(2, 1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = icon,
                            FontSize = 10,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = title,
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

        private sealed class RichClipboardFallbackSnapshot
        {
            private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(10);

            public RichClipboardFallbackSnapshot(
                string payload,
                string text,
                string textWithObjectPlaceholdersAsSpaces,
                DateTimeOffset createdAt)
            {
                Payload = payload;
                Text = text;
                TextWithObjectPlaceholdersAsSpaces = textWithObjectPlaceholdersAsSpaces;
                CreatedAt = createdAt;
            }

            public string Payload { get; }

            public string Text { get; }

            public string TextWithObjectPlaceholdersAsSpaces { get; }

            public DateTimeOffset CreatedAt { get; }

            public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > Expiration;
        }
    }

    internal sealed class RichTextInlineObjectGenerator : VisualLineElementGenerator
    {
        private readonly RichTextInputManager _manager;

        public RichTextInlineObjectGenerator(RichTextInputManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            return _manager.GetFirstInterestedOffset(startOffset);
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            return _manager.TryGetItem(offset, out var item)
                ? new InlineObjectElement(
                    RichTextInputManager.ObjectReplacementString.Length,
                    _manager.CreateElement(item),
                    _manager.GetInlineObjectAlignment(item),
                    _manager.GetInlineObjectBaselineOffset(item),
                    _manager.GetInlineObjectArrangeOffset(item))
                : null;
        }
    }

    internal sealed class RichTextInlineContentControl : Border
    {
        private readonly RichTextInputManager _manager;
        private readonly RichTextContentItem _item;

        public RichTextInlineContentControl(RichTextInputManager manager, RichTextContentItem item, Control content)
        {
            _manager = manager;
            _item = item;
            Child = content;
            Focusable = false;
            Classes.Add("rich-text-inline-content");
            UpdateSelection();
            _manager.ContentSelectionChanged += Manager_ContentSelectionChanged;
            AddHandler(DoubleTappedEvent, OnDoubleTapped);
            AddHandler(ContextRequestedEvent, OnContextRequested);
            DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var args = _manager.RaiseContentPointerPressed(_item, e);
            if (args.Handled)
            {
                e.Handled = true;
                return;
            }

            _manager.ApplyContentPointerSelection(args);

            e.Handled = _manager.ShouldHandleContentPointerEvent(args);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            var args = _manager.RaiseContentPointerReleased(_item, e);
            e.Handled = args.Handled || _manager.ShouldHandleContentPointerEvent(args);
        }

        private void OnDoubleTapped(object sender, TappedEventArgs e)
        {
            var args = _manager.RaiseContentDoubleTapped(_item, e);
            e.Handled = args.Handled || _manager.ShouldHandleContentPointerEvent(args);
        }

        private void OnContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var args = _manager.RaiseContentContextRequested(_item, e);
            e.Handled = args.Handled || _manager.ShouldHandleContentPointerEvent(args);
        }

        private void Manager_ContentSelectionChanged(object sender, RichTextContentChangedEventArgs e)
        {
            UpdateSelection();
        }

        private void OnDetachedFromVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            _manager.ContentSelectionChanged -= Manager_ContentSelectionChanged;
            RemoveHandler(DoubleTappedEvent, OnDoubleTapped);
            RemoveHandler(ContextRequestedEvent, OnContextRequested);
            DetachedFromVisualTree -= OnDetachedFromVisualTree;
        }

        private void UpdateSelection()
        {
            var selected = _manager.IsContentSelected(_item);
            var style = _manager.GetInlineContentStyle(_item, selected);
            Background = style.Background;
            BorderBrush = style.BorderBrush;
            BorderThickness = style.BorderThickness;
            CornerRadius = style.CornerRadius;
            Padding = style.Padding;
            Classes.Set("selected", selected);
        }

    }
}
