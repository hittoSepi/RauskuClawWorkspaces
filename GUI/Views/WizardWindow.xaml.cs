using RauskuClaw.GUI.ViewModels;
using RauskuClaw.Services;
using System.Windows.Input;
using System.Windows;

namespace RauskuClaw.GUI.Views
{
    public partial class WizardWindow : Window
    {
        public WizardViewModel ViewModel { get; }

        public WizardWindow(RauskuClaw.Models.Settings? settings = null, RauskuClaw.Models.PortAllocation? suggestedPorts = null)
        {
            InitializeComponent();

            ViewModel = new WizardViewModel(settings, suggestedPorts);
            ViewModel.CloseRequested += OnCloseRequested;
            DataContext = ViewModel;
        }

        private void OnCloseRequested(bool accepted)
        {
            DialogResult = accepted;
            Close();
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsRunning)
            {
                if (ViewModel.IsCancellationRequested)
                {
                    DialogResult = false;
                    Close();
                    return;
                }

                ViewModel.CancelCommand.Execute(null);
                return;
            }

            DialogResult = ViewModel.StartSucceeded;
            Close();
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (ViewModel.IsRunning)
            {
                if (ViewModel.IsCancellationRequested)
                {
                    DialogResult = false;
                    base.OnClosing(e);
                    return;
                }

                e.Cancel = true;
                ViewModel.CancelCommand.Execute(null);
                return;
            }

            if (DialogResult != true)
            {
                DialogResult = ViewModel.StartSucceeded;
            }

            base.OnClosing(e);
        }
    }
}
