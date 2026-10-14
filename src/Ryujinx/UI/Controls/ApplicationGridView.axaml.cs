using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities.AppLibrary;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Controls
{
    public partial class ApplicationGridView : UserControl
    {
        private MainWindowViewModel _viewModel;
        private int _selectionAnimationVersion;
        private bool _hadSelection;

        public static readonly RoutedEvent<ApplicationOpenedEventArgs> ApplicationOpenedEvent =
            RoutedEvent.Register<ApplicationGridView, ApplicationOpenedEventArgs>(nameof(ApplicationOpened), RoutingStrategies.Bubble);

        public event EventHandler<ApplicationOpenedEventArgs> ApplicationOpened
        {
            add => AddHandler(ApplicationOpenedEvent, value);
            remove => RemoveHandler(ApplicationOpenedEvent, value);
        }

        public ApplicationGridView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (_viewModel is not null)
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

                _viewModel = DataContext as MainWindowViewModel;
                _hadSelection = _viewModel?.HasGridSelection ?? false;

                if (_viewModel is not null)
                    _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            };
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.HasGridSelection))
            {
                bool hasSelection = _viewModel.HasGridSelection;

                if (hasSelection && !_hadSelection)
                    _ = AnimateSelectionAsync();

                _hadSelection = hasSelection;
            }
        }

        private async Task AnimateSelectionAsync()
        {
            int version = ++_selectionAnimationVersion;
            SelectionPanel.Opacity = 0.4;

            await Task.Delay(35);

            Dispatcher.UIThread.Post(() =>
            {
                if (version == _selectionAnimationVersion)
                    SelectionPanel.Opacity = 1;
            });
        }

        public void GameList_DoubleTapped(object sender, TappedEventArgs args)
        {
            if (sender is ListBox { SelectedItem: ApplicationData selected })
                OpenApplication(selected);
        }

        private void PlaySelected_OnClick(object sender, RoutedEventArgs args)
        {
            if (DataContext is MainWindowViewModel { GridSelectedApplication: { } selected })
                OpenApplication(selected);
        }

        private void OpenSettings_OnClick(object sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this) is MainWindow window)
                window.OpenSettingsFromHome();
        }

        private void OpenApplication(ApplicationData application) =>
            RaiseEvent(new ApplicationOpenedEventArgs(application, ApplicationOpenedEvent));
    }
}
