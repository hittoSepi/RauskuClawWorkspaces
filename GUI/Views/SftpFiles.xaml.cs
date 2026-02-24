using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using RauskuClaw.GUI.ViewModels;

namespace RauskuClaw.GUI.Views
{
    public partial class SftpFiles : UserControl
    {
        public SftpFiles()
        {
            InitializeComponent();
        }

        private async void EntriesDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not SftpFilesViewModel vm)
            {
                return;
            }

            await vm.HandleEntryDoubleClickAsync(vm.SelectedEntry);
        }

        private async void PathInputTextBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || DataContext is not SftpFilesViewModel vm)
            {
                return;
            }

            e.Handled = true;
            if (vm.NavigateToPathCommand.CanExecute(null))
            {
                vm.NavigateToPathCommand.Execute(null);
            }

            await Task.CompletedTask;
        }

        private async void PathSuggestions_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not SftpFilesViewModel vm)
            {
                return;
            }

            await vm.AcceptPathSuggestionAsync(vm.SelectedPathSuggestion);
        }

        private async void PathSuggestions_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not SftpFilesViewModel vm || e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            await vm.AcceptPathSuggestionAsync(vm.SelectedPathSuggestion);
        }
    }
}
