using System.ComponentModel;
using System.Windows;

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
