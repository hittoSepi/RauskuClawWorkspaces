using System.Windows;
using System.Windows.Input;

namespace RauskuClaw.GUI.Views
{
    public enum ProvisioningSecretsDialogAction
    {
        Retry,
        OpenSettings,
        ContinueLocalTemplate
    }

    public partial class ProvisioningSecretsRequiredDialog : Window
    {
        public ProvisioningSecretsDialogAction SelectedAction { get; private set; } = ProvisioningSecretsDialogAction.Retry;

        public ProvisioningSecretsRequiredDialog()
        {
            InitializeComponent();
        }

        private void OpenSettingsButton_OnClick(object sender, RoutedEventArgs e)
        {
            SelectedAction = ProvisioningSecretsDialogAction.OpenSettings;
            DialogResult = true;
            Close();
        }

        private void RetryButton_OnClick(object sender, RoutedEventArgs e)
        {
            SelectedAction = ProvisioningSecretsDialogAction.Retry;
            DialogResult = true;
            Close();
        }

        private void FallbackButton_OnClick(object sender, RoutedEventArgs e)
        {
            SelectedAction = ProvisioningSecretsDialogAction.ContinueLocalTemplate;
            DialogResult = true;
            Close();
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            SelectedAction = ProvisioningSecretsDialogAction.Retry;
            DialogResult = false;
            Close();
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
