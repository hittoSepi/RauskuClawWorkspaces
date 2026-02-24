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

            var entry = vm.SelectedEntry;
            if (sender is DataGrid grid && grid.SelectedItem is SftpEntryItemViewModel selectedEntry)
            {
                entry = selectedEntry;
            }

            await vm.HandleEntryDoubleClickAsync(entry);
        }

        private async void PathInputTextBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not SftpFilesViewModel vm)
            {
                return;
            }

            if ((e.Key == Key.Down || e.Key == Key.Up) && vm.IsPathSuggestionsOpen && PathSuggestionsListBox.Items.Count > 0)
            {
                e.Handled = true;
                if (PathSuggestionsListBox.SelectedIndex < 0)
                {
                    PathSuggestionsListBox.SelectedIndex = 0;
                }
                PathSuggestionsListBox.Focus();
                return;
            }

            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            if (vm.IsPathSuggestionsOpen && !string.IsNullOrWhiteSpace(vm.SelectedPathSuggestion))
            {
                await vm.AcceptPathSuggestionAsync(vm.SelectedPathSuggestion);
                return;
            }

            if (vm.NavigateToPathCommand.CanExecute(null))
            {
                vm.NavigateToPathCommand.Execute(null);
            }
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
            if (DataContext is not SftpFilesViewModel vm)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await vm.AcceptPathSuggestionAsync(vm.SelectedPathSuggestion);
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                vm.IsPathSuggestionsOpen = false;
                PathInputTextBox.Focus();
                return;
            }

            if (e.Key == Key.Back && PathSuggestionsListBox.SelectedIndex <= 0)
            {
                PathInputTextBox.Focus();
            }
        }
    }
}
