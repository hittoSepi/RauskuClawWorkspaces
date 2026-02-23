using System.Windows.Controls;
using System.Windows.Documents;
using RauskuClaw.GUI.Utils;
using RauskuClaw.GUI.ViewModels;

namespace RauskuClaw.GUI.Views
{
    /// <summary>
    /// SerialConsole.xaml interaction logic
    /// </summary>
    public partial class SerialConsole : UserControl
    {
        private readonly AnsiSgrParser _ansiParser = new();
        private SerialConsoleViewModel? _vm;

        public SerialConsole()
        {
            InitializeComponent();
            DataContextChanged += SerialConsole_OnDataContextChanged;
            Loaded += (_, _) => AttachToViewModel(DataContext as SerialConsoleViewModel);
            Unloaded += (_, _) => AttachToViewModel(null);
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
            }

            _vm = vm;
            _ansiParser.Reset();
            SerialOutputRichTextBox.Document = new FlowDocument(new Paragraph());

            if (_vm != null)
            {
                _vm.SerialChunkAppended += ViewModel_OnSerialChunkAppended;
                _vm.SerialOutputReset += ViewModel_OnSerialOutputReset;
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
                SerialOutputRichTextBox.ScrollToEnd();
            }
        }
    }
}
