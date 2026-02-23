using System.Windows;
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
    }
}
