using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using RauskuClaw.Models;
using RauskuClaw.GUI.ViewModels;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.Views
{
    /// <summary>
    /// Main application window - workspace management interface.
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel? _mainViewModel;
        private bool _shutdownRoutineActive;
        private const double SidebarExpandedWidth = 280;
        private const double SidebarCollapsedWidth = 72;

        public MainWindow()
        {
            InitializeComponent();
            var pathResolver = new AppPathResolver();
            var settingsService = new SettingsService(pathResolver: pathResolver);
            _mainViewModel = new MainViewModel(settingsService, pathResolver);
            DataContext = _mainViewModel;

            if (_mainViewModel != null)
            {
                _mainViewModel.PropertyChanged += MainViewModel_OnPropertyChanged;
            }

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Show any queued startup messages
            App.StartupMessages.ShowQueuedMessages();

            // Unsubscribe after showing
            Loaded -= MainWindow_Loaded;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_shutdownRoutineActive || _mainViewModel == null)
            {
                base.OnClosing(e);
                return;
            }

            if (!_mainViewModel.HasRunningVmProcesses())
            {
                _mainViewModel.Shutdown();
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            _shutdownRoutineActive = true;
            _ = RunShutdownRoutineAsync();
        }

        private async System.Threading.Tasks.Task RunShutdownRoutineAsync()
        {
            var progressWindow = new VmActionProgressWindow("Closing Application", "Shutting down running VMs...")
            {
                Owner = this
            };

            progressWindow.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            try
            {
                _mainViewModel?.ShutdownWithProgress(status =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressWindow.UpdateStatus(status);
                    });
                    Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                });
            }
            finally
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                Close();
            }
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

        private void WorkspaceList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mainViewModel == null)
            {
                return;
            }

            if (_mainViewModel.SelectedWorkspace == null)
            {
                return;
            }

            if (_mainViewModel.SelectedMainSection != MainViewModel.MainContentSection.WorkspaceTabs)
            {
                _mainViewModel.SelectedMainSection = MainViewModel.MainContentSection.WorkspaceTabs;
            }
        }

        private void WorkspaceListItem_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mainViewModel == null)
            {
                return;
            }

            if (sender is not ListBoxItem item || item.DataContext is not Workspace workspace)
            {
                return;
            }

            if (!ReferenceEquals(_mainViewModel.SelectedWorkspace, workspace))
            {
                _mainViewModel.SelectedWorkspace = workspace;
            }

            _mainViewModel.SelectedMainSection = MainViewModel.MainContentSection.WorkspaceTabs;
        }

        private void MainViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed) && _mainViewModel != null)
            {
                AnimateSidebar(_mainViewModel.IsSidebarCollapsed);
            }
        }

        private void BootLogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            BootLogTextBox.ScrollToEnd();
        }

        private void AnimateSidebar(bool collapse)
        {
            double to = collapse ? SidebarCollapsedWidth : SidebarExpandedWidth;
            AnimateSidebarColumn(SidebarColumn, to);
            AnimateSidebarColumn(TitleSidebarColumn, to);
        }

        private static void AnimateSidebarColumn(ColumnDefinition column, double to)
        {
            double from = column.Width.Value;
            if (Math.Abs(from - to) < 0.1)
            {
                return;
            }

            var duration = new Duration(TimeSpan.FromMilliseconds(180));
            var animation = new GridLengthAnimation
            {
                From = new GridLength(from),
                To = new GridLength(to),
                Duration = duration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            column.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }
    }
}
