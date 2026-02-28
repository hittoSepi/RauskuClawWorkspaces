using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using RauskuClaw.GUI.ViewModels;

namespace RauskuClaw.GUI.Views
{
    /// <summary>
    /// SshTerminal.xaml interaction logic
    /// </summary>
    public partial class SshTerminal : UserControl
    {
        private const double BottomThreshold = 28;
        private SshTerminalViewModel? _vm;
        private bool _programmaticScroll;
        private bool _userScrollIntent;

        public SshTerminal()
        {
            InitializeComponent();
            DataContextChanged += SshTerminal_OnDataContextChanged;
            Loaded += SshTerminal_OnLoaded;
            Unloaded += SshTerminal_OnUnloaded;
            TerminalOutputTextBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(TerminalOutput_OnScrollChanged));
            TerminalOutputTextBox.PreviewMouseWheel += TerminalOutput_OnUserScrollIntent;
            TerminalOutputTextBox.PreviewMouseDown += TerminalOutput_OnPreviewMouseDown;
            TerminalOutputTextBox.PreviewKeyDown += TerminalOutput_OnPreviewKeyDown;
        }

        private void SshTerminal_OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachToViewModel(DataContext as SshTerminalViewModel);
        }

        private void SshTerminal_OnUnloaded(object sender, RoutedEventArgs e)
        {
            AttachToViewModel(null);
        }

        private void SshTerminal_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            AttachToViewModel(e.NewValue as SshTerminalViewModel);
        }

        private void AttachToViewModel(SshTerminalViewModel? vm)
        {
            _vm = vm;
        }

        private void TerminalOutputTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_vm == null || !_vm.AutoScroll)
            {
                return;
            }

            _programmaticScroll = true;
            TerminalOutputTextBox.ScrollToEnd();
            _programmaticScroll = false;
            ScrollToBottomButton.Visibility = Visibility.Collapsed;
        }

        private void TerminalOutput_OnScrollChanged(object sender, ScrollChangedEventArgs e)
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

            if (_userScrollIntent && e.VerticalChange != 0 && _vm.AutoScroll && !isNearBottom)
            {
                _vm.AutoScroll = false;
            }

            if (e.VerticalChange != 0)
            {
                _userScrollIntent = false;
            }
        }

        private void TerminalOutput_OnUserScrollIntent(object sender, MouseWheelEventArgs e)
        {
            _userScrollIntent = true;
            if (_vm != null && _vm.AutoScroll)
            {
                _vm.AutoScroll = false;
            }
        }

        private void TerminalOutput_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _userScrollIntent = true;
        }

        private void TerminalOutput_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_vm == null || !_vm.AutoScroll)
            {
                return;
            }

            if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            {
                _userScrollIntent = true;
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
            TerminalOutputTextBox.ScrollToEnd();
            _programmaticScroll = false;
            ScrollToBottomButton.Visibility = Visibility.Collapsed;
        }
    }
}
