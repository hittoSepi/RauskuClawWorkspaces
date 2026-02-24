using System.Windows;
using System.Windows.Input;
using RauskuClaw.GUI.ViewModels;

namespace RauskuClaw.GUI.Views
{
    /// <summary>
    /// Main application window - workspace management interface.
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel? _mainViewModel;

        public MainWindow()
        {
            InitializeComponent();
            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;

            // Initialize child ViewModels
            if (_mainViewModel is MainViewModel vm)
            {
                vm.WebUi = new WebUiViewModel();
                vm.SerialConsole = new SerialConsoleViewModel();
                vm.DockerContainers = new DockerContainersViewModel();
                vm.SshTerminal = new SshTerminalViewModel();
                vm.SftpFiles = new SftpFilesViewModel();
                vm.Settings = new SettingsViewModel();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _mainViewModel?.Shutdown();
            base.OnClosing(e);
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleWindowState()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
