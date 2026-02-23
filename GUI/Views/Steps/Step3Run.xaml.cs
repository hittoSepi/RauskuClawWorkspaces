using System.Windows.Controls;

namespace RauskuClaw.GUI.Views.Steps
{
    /// <summary>
    /// Interaction logic for Step3Run.xaml
    /// </summary>
    public partial class Step3Run : UserControl
    {
        public Step3Run()
        {
            InitializeComponent();
        }

        private void RunLogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            RunLogTextBox.ScrollToEnd();
        }
    }
}
