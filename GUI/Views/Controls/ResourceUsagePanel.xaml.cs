using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using RauskuClaw.GUI.ViewModels;

namespace RauskuClaw.GUI.Views.Controls
{
    public enum ResourceUsageScope
    {
        Combined,
        SelectedWorkspace,
        Auto
    }

    /// <summary>
    /// Reusable panel for workspace/VM runtime resource usage.
    /// </summary>
    public partial class ResourceUsagePanel : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty ScopeProperty = DependencyProperty.Register(
            nameof(Scope),
            typeof(ResourceUsageScope),
            typeof(ResourceUsagePanel),
            new PropertyMetadata(ResourceUsageScope.Auto, OnScopeChanged));

        public static readonly DependencyProperty AllowScopeToggleProperty = DependencyProperty.Register(
            nameof(AllowScopeToggle),
            typeof(bool),
            typeof(ResourceUsagePanel),
            new PropertyMetadata(false, OnAllowScopeToggleChanged));

        private bool _isCombinedMode = true;
        private bool _hasSelectedWorkspace;
        private bool _manualCombinedMode = true;
        private INotifyPropertyChanged? _currentContextNotifier;

        public ResourceUsagePanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, _) => UpdateMode();
            Unloaded += (_, _) => DetachContextNotifier();
        }

        public ResourceUsageScope Scope
        {
            get => (ResourceUsageScope)GetValue(ScopeProperty);
            set => SetValue(ScopeProperty, value);
        }

        public bool AllowScopeToggle
        {
            get => (bool)GetValue(AllowScopeToggleProperty);
            set => SetValue(AllowScopeToggleProperty, value);
        }

        public bool IsCombinedMode
        {
            get => _isCombinedMode;
            private set
            {
                if (_isCombinedMode == value)
                {
                    return;
                }

                _isCombinedMode = value;
                OnPropertyChanged();
            }
        }

        public bool HasSelectedWorkspace
        {
            get => _hasSelectedWorkspace;
            private set
            {
                if (_hasSelectedWorkspace == value)
                {
                    return;
                }

                _hasSelectedWorkspace = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private static void OnScopeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ResourceUsagePanel panel)
            {
                panel.UpdateMode();
            }
        }

        private static void OnAllowScopeToggleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ResourceUsagePanel panel)
            {
                if (!(bool)e.NewValue)
                {
                    panel._manualCombinedMode = true;
                }

                panel.UpdateMode();
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachContextNotifier();
            AttachContextNotifier();
            UpdateMode();
        }

        private void AttachContextNotifier()
        {
            if (DataContext is INotifyPropertyChanged notifier)
            {
                _currentContextNotifier = notifier;
                _currentContextNotifier.PropertyChanged += OnContextPropertyChanged;
            }
        }

        private void DetachContextNotifier()
        {
            if (_currentContextNotifier != null)
            {
                _currentContextNotifier.PropertyChanged -= OnContextPropertyChanged;
                _currentContextNotifier = null;
            }
        }

        private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedWorkspace))
            {
                UpdateMode();
            }
        }

        private void OnShowCombinedClicked(object sender, RoutedEventArgs e)
        {
            _manualCombinedMode = true;
            UpdateMode();
        }

        private void OnShowSelectedClicked(object sender, RoutedEventArgs e)
        {
            if (!HasSelectedWorkspace)
            {
                return;
            }

            _manualCombinedMode = false;
            UpdateMode();
        }

        private void UpdateMode()
        {
            var selectedWorkspace = (DataContext as MainViewModel)?.SelectedWorkspace;
            HasSelectedWorkspace = selectedWorkspace != null;

            var combined = Scope switch
            {
                ResourceUsageScope.Combined => true,
                ResourceUsageScope.SelectedWorkspace => false,
                _ => selectedWorkspace == null
            };

            if (AllowScopeToggle)
            {
                combined = _manualCombinedMode;
            }

            if (!combined && !HasSelectedWorkspace)
            {
                combined = true;
            }

            IsCombinedMode = combined;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
