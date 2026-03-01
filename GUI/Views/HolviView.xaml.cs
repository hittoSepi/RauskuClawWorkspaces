using System.Windows.Controls;

namespace RauskuClaw.GUI.Views
{
    /// <summary>
    /// HolviView.xaml interaction logic
    /// </summary>
    public partial class HolviView : UserControl
    {
        public HolviView()
        {
            InitializeComponent();
        }

        private void SetupStatusTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-scroll to bottom when new text is added
            if (sender is TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }
    }
}
