using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Input;
using RauskuClaw.GUI.Utils;
using RauskuClaw.GUI.ViewModels;

namespace RauskuClaw.GUI.Views
{
    /// <summary>
    /// SerialConsole.xaml interaction logic
    /// </summary>
    public partial class SerialConsole : UserControl
    {
        private const double BottomThreshold = 28;
        private readonly AnsiSgrParser _ansiParser = new();
        private SerialConsoleViewModel? _vm;
        private bool _programmaticScroll;

        public SerialConsole()
        {
            InitializeComponent();
            DataContextChanged += SerialConsole_OnDataContextChanged;
            Loaded += SerialConsole_OnLoaded;
            Unloaded += SerialConsole_OnUnloaded;
            SerialOutputRichTextBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(SerialOutput_OnScrollChanged));
            SerialOutputRichTextBox.PreviewMouseWheel += SerialOutput_OnUserScrollIntent;
            SerialOutputRichTextBox.PreviewKeyDown += SerialOutput_OnPreviewKeyDown;
        }

        private void SerialConsole_OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachToViewModel(DataContext as SerialConsoleViewModel);
            _vm?.SetViewerAttached(true);
        }

        private void SerialConsole_OnUnloaded(object sender, RoutedEventArgs e)
        {
            _vm?.SetViewerAttached(false);
            AttachToViewModel(null);
        }

        private void SerialConsole_OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            AttachToViewModel(e.NewValue as SerialConsoleViewModel);
        }

        private void AttachToViewModel(SerialConsoleViewModel? vm)
        {
            if (_vm != null)
            {
                _vm.SerialChunkAppended -= ViewModel_OnSerialChunkAppended;
                _vm.SerialOutputReset -= ViewModel_OnSerialOutputReset;
                _vm.SetViewerAttached(false);
            }

            _vm = vm;
            _ansiParser.Reset();
            SerialOutputRichTextBox.Document = new FlowDocument(new Paragraph());

            if (_vm != null)
            {
                _vm.SerialChunkAppended += ViewModel_OnSerialChunkAppended;
                _vm.SerialOutputReset += ViewModel_OnSerialOutputReset;
                _vm.SetViewerAttached(IsLoaded);
                RenderFullText(_vm.SerialOutput);
            }
        }

        private void ViewModel_OnSerialChunkAppended(object? sender, string chunk)
        {
            AppendAnsiChunk(chunk);
            ScrollIfEnabled();
        }

        private void ViewModel_OnSerialOutputReset(object? sender, string fullText)
        {
            RenderFullText(fullText);
            ScrollIfEnabled();
        }

        private void RenderFullText(string text)
        {
            _ansiParser.Reset();
            SerialOutputRichTextBox.Document = new FlowDocument(new Paragraph());
            AppendAnsiChunk(text);
        }

        private void AppendAnsiChunk(string chunk)
        {
            if (SerialOutputRichTextBox.Document.Blocks.FirstBlock is not Paragraph paragraph)
            {
                paragraph = new Paragraph();
                SerialOutputRichTextBox.Document.Blocks.Clear();
                SerialOutputRichTextBox.Document.Blocks.Add(paragraph);
            }

            var segments = _ansiParser.ParseChunk(chunk);
            foreach (var segment in segments)
            {
                if (string.IsNullOrEmpty(segment.Text))
                {
                    continue;
                }

                var run = new Run(segment.Text)
                {
                    Foreground = segment.Foreground,
                    Background = segment.Background,
                    FontWeight = segment.Weight
                };
                paragraph.Inlines.Add(run);
            }
        }

        private void ScrollIfEnabled()
        {
            if (_vm?.AutoScroll == true)
            {
                _programmaticScroll = true;
                SerialOutputRichTextBox.ScrollToEnd();
                _programmaticScroll = false;
                ScrollToBottomButton.Visibility = Visibility.Collapsed;
            }
        }

        private void SerialOutput_OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_vm == null)
            {
                return;
            }

            var distanceToBottom = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight);
            var isNearBottom = distanceToBottom <= BottomThreshold;
            ScrollToBottomButton.Visibility = isNearBottom ? Visibility.Collapsed : Visibility.Visible;

            if (_programmaticScroll)
            {
                return;
            }

            if (e.VerticalChange != 0 && _vm.AutoScroll && !isNearBottom)
            {
                _vm.AutoScroll = false;
            }
        }

        private void SerialOutput_OnUserScrollIntent(object sender, MouseWheelEventArgs e)
        {
            if (_vm != null && _vm.AutoScroll)
            {
                _vm.AutoScroll = false;
            }
        }

        private void SerialOutput_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_vm == null || !_vm.AutoScroll)
            {
                return;
            }

            if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            {
                _vm.AutoScroll = false;
            }
        }

        private void ScrollToBottomButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.AutoScroll = true;
            }

            _programmaticScroll = true;
            SerialOutputRichTextBox.ScrollToEnd();
            _programmaticScroll = false;
            ScrollToBottomButton.Visibility = Visibility.Collapsed;
        }
    }
}
