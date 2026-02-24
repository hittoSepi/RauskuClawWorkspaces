using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace RauskuClaw.GUI.Views
{
    public partial class VmActionProgressWindow : Window
    {
        private bool _allowClose;

        public VmActionProgressWindow(string title, string status)
        {
            InitializeComponent();
            TitleText.Text = title;
            StatusText.Text = status;
        }

        public void UpdateStatus(string status)
        {
            StatusText.Text = status;
        }

        public void AllowClose()
        {
            _allowClose = true;
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }
    }
}
