using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities.AppLibrary;
using System;

namespace Ryujinx.Ava.UI.Controls
{
    public partial class ApplicationGridView : UserControl
    {
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(30) };
        private Button _lastDockButton;

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
            _clockTimer.Tick += (_, _) => UpdateClock();
            UpdateClock();
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateClock();
            _clockTimer.Start();
            Dispatcher.UIThread.Post(() =>
            {
                DockOnline.Focus();
                ShowDockLabel(DockOnline);
            }, DispatcherPriority.Loaded);
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            _clockTimer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        private void UpdateClock() => HomeClock.Text = DateTime.Now.ToString("HH:mm");

        private void GameList_GotFocus(object sender, GotFocusEventArgs args)
        {
            bool hasSelection = DataContext is MainWindowViewModel { HasGridSelection: true };
            GameListBox.Classes.Set("gameFocused", hasSelection);
            GameTitle.IsVisible = hasSelection;
            OptionsPrompt.IsVisible = hasSelection;
            DockLabel.IsVisible = false;
            _lastDockButton?.Classes.Set("homeSelected", false);
        }

        private void GameList_LostFocus(object sender, RoutedEventArgs args)
        {
            GameListBox.Classes.Set("gameFocused", false);
            GameTitle.IsVisible = false;
            OptionsPrompt.IsVisible = false;
        }

        private void DockAction_GotFocus(object sender, GotFocusEventArgs args)
        {
            if (sender is Button button)
            {
                ShowDockLabel(button);
                GameListBox.Classes.Set("gameFocused", false);
                GameTitle.IsVisible = false;
                OptionsPrompt.IsVisible = false;
            }
        }

        private void DockAction_LostFocus(object sender, RoutedEventArgs args)
        {
            if (GameTitle.IsVisible)
                DockLabel.IsVisible = false;
        }

        private void ShowDockLabel(Button button)
        {
            int index = DockBar.Children.IndexOf(button);
            if (index < 0)
                return;

            _lastDockButton?.Classes.Set("homeSelected", false);
            _lastDockButton = button;
            button.Classes.Set("homeSelected", true);
            DockLabel.Text = button.Tag?.ToString();
            Canvas.SetLeft(DockLabel, 127 + index * 124);
            DockLabel.IsVisible = true;
        }

        private void FocusGames_OnClick(object sender, RoutedEventArgs args) => GameListBox.Focus();

        private void GameList_KeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Down)
            {
                (_lastDockButton ?? DockOnline).Focus();
                args.Handled = true;
            }
            else if (args.Key == Key.Enter && DataContext is MainWindowViewModel { GridSelectedApplication: { } selected })
            {
                OpenApplication(selected);
                args.Handled = true;
            }
        }

        private void DockBar_KeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Up && DataContext is MainWindowViewModel { HasGridSelection: true })
            {
                GameListBox.Focus();
                args.Handled = true;
                return;
            }

            if (args.Key is not (Key.Left or Key.Right) || _lastDockButton is null)
                return;

            int index = DockBar.Children.IndexOf(_lastDockButton);
            int nextIndex = index + (args.Key == Key.Right ? 1 : -1);

            if (nextIndex >= 0 && nextIndex < DockBar.Children.Count && DockBar.Children[nextIndex] is Button nextButton)
            {
                nextButton.Focus();
                args.Handled = true;
            }
        }

        private async void OpenFile_OnClick(object sender, RoutedEventArgs args)
        {
            if (DataContext is MainWindowViewModel viewModel)
                await viewModel.OpenFile();
        }

        private async void OpenFolder_OnClick(object sender, RoutedEventArgs args)
        {
            if (DataContext is MainWindowViewModel viewModel)
                await viewModel.OpenFolder();
        }

        private void CloseWindow_OnClick(object sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this) is MainWindow window)
                window.Close();
        }

        private void ShowOptions_OnClick(object sender, RoutedEventArgs args)
        {
            if (GameTitle.IsVisible)
                GameListBox.ContextFlyout?.ShowAt(GameListBox);
        }

        private void Confirm_OnClick(object sender, RoutedEventArgs args)
        {
            if (GameTitle.IsVisible)
                PlaySelected_OnClick(sender, args);
            else
                _lastDockButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

        private void OpenInputSettings_OnClick(object sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this) is MainWindow window)
                window.OpenSettingsFromHome("InputPage");
        }

        private void OpenNetworkSettings_OnClick(object sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this) is MainWindow window)
                window.OpenSettingsFromHome("NetworkPage");
        }

        private void OpenSystemSettings_OnClick(object sender, RoutedEventArgs args)
        {
            if (TopLevel.GetTopLevel(this) is MainWindow window)
                window.OpenSettingsFromHome("SystemPage");
        }

        private async void OpenProfiles_OnClick(object sender, RoutedEventArgs args)
        {
            if (DataContext is MainWindowViewModel viewModel)
                await viewModel.ManageProfiles();
        }

        private void OpenScreenshots_OnClick(object sender, RoutedEventArgs args)
        {
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.OpenScreenshotsFolder();
        }

        private void OpenApplication(ApplicationData application) =>
            RaiseEvent(new ApplicationOpenedEventArgs(application, ApplicationOpenedEvent));
    }
}
