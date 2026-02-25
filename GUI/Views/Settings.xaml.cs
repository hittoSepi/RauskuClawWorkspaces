using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RauskuClaw.GUI.ViewModels;

namespace RauskuClaw.GUI.Views
{
    public partial class Settings : UserControl
    {
        public Settings()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HookMainWindowDataContext();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            HookMainWindowDataContext();
        }

        private void HookMainWindowDataContext()
        {
            if (Window.GetWindow(this)?.DataContext is MainViewModel main)
            {
                main.PropertyChanged -= OnMainViewModelPropertyChanged;
                main.PropertyChanged += OnMainViewModelPropertyChanged;
                if (main.FocusSecretsSection)
                {
                    ScrollSecretsSectionIntoView();
                }
            }
        }

        private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(MainViewModel.FocusSecretsSection), System.StringComparison.Ordinal))
            {
                return;
            }

            if (sender is MainViewModel main && main.FocusSecretsSection)
            {
                Dispatcher.InvokeAsync(ScrollSecretsSectionIntoView);
            }
        }

        private void ScrollSecretsSectionIntoView()
        {
            var transform = SecretsSectionBorder.TransformToAncestor(RootScrollViewer);
            var position = transform.Transform(new Point(0, 0));
            RootScrollViewer.ScrollToVerticalOffset(position.Y + RootScrollViewer.VerticalOffset - 12);
        }
    }
}
