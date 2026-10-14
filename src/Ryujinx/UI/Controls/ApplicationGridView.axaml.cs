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
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            _clockTimer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        private void UpdateClock() => HomeClock.Text = DateTime.Now.ToString("HH:mm");

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
