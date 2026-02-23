using System.Windows;
using System.Windows.Input;

namespace RauskuClaw.GUI.Views
{
    public partial class ThemedDialogWindow : Window
    {
        private readonly bool _isConfirm;

        public ThemedDialogWindow(string title, string message, bool isConfirm)
        {
            InitializeComponent();
            _isConfirm = isConfirm;
            TitleText.Text = title;
            MessageText.Text = message;

            YesButton.Visibility = isConfirm ? Visibility.Visible : Visibility.Collapsed;
            NoButton.Visibility = isConfirm ? Visibility.Visible : Visibility.Collapsed;
            OkButton.Visibility = isConfirm ? Visibility.Collapsed : Visibility.Visible;
        }

        public static bool ShowConfirm(Window? owner, string title, string message)
        {
            var dialog = new ThemedDialogWindow(title, message, isConfirm: true)
            {
                Owner = owner ?? Application.Current?.MainWindow
            };
            var result = dialog.ShowDialog();
            return result == true;
        }

        public static void ShowInfo(Window? owner, string title, string message)
        {
            var dialog = new ThemedDialogWindow(title, message, isConfirm: false)
            {
                Owner = owner ?? Application.Current?.MainWindow
            };
            dialog.ShowDialog();
        }

        private void YesButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void NoButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = _isConfirm ? false : true;
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
