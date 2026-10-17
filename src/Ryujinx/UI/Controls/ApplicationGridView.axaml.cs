using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities.AppLibrary;
using Ryujinx.Ava.Utilities.Configuration;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Input;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Button = Avalonia.Controls.Button;
using Key = Avalonia.Input.Key;

namespace Ryujinx.Ava.UI.Controls
{
    public partial class ApplicationGridView : UserControl
    {
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(30) };
        private readonly DispatcherTimer _gamepadTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private Button _lastDockButton;
        private IGamepad _homeGamepad;
        private string _homeGamepadId;
        private StandardControllerInputConfig _homeGamepadConfig;
        private bool _ignoreNextGamepadSnapshot = true;
        private bool _aWasDown;
        private bool _bWasDown;
        private bool _plusWasDown;
        private bool _xWasDown;
        private HomeDirection _heldDirection;
        private long _nextDirectionRepeat;
        private CancellationTokenSource _launchAnimationCancellation;
        private ulong? _runningApplicationId;
        private ApplicationData _runningApplication;

        private enum HomeDirection { None, Left, Right, Up, Down }

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
            LaunchGhost.Effect = new BlurEffect { Radius = 10 };
            GameListBox.LayoutUpdated += (_, _) =>
            {
                UpdateGameTitlePosition();
                UpdateRunningBadgePosition();
            };
            _clockTimer.Tick += (_, _) => UpdateClock();
            _gamepadTimer.Tick += PollHomeGamepad;
            UpdateClock();
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateClock();
            _clockTimer.Start();
            _gamepadTimer.Start();
            Dispatcher.UIThread.Post(() =>
            {
                DockOnline.Focus();
                ShowDockLabel(DockOnline);
            }, DispatcherPriority.Loaded);
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            _clockTimer.Stop();
            StopHomeGamepadInput();
            EndLaunchAnimation();
            base.OnDetachedFromVisualTree(e);
        }

        public async Task<bool> PlayLaunchAnimationAsync(ApplicationData application)
        {
            EndLaunchAnimation();

            Point origin = new(768, 311);
            if (GameListBox.ContainerFromItem(application) is Control tile &&
                tile.TranslatePoint(new Point(), HomeCanvas) is { } tilePosition)
            {
                origin = new Point(tilePosition.X + 11, tilePosition.Y + 11);
            }

            LaunchAnimationLayer.IsVisible = true;
            CancellationTokenSource cancellation = new();
            _launchAnimationCancellation = cancellation;
            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                const double durationMs = 560;
                while (true)
                {
                    double progress = Math.Min(clock.Elapsed.TotalMilliseconds / durationMs, 1);
                    UpdateLaunchAnimation(origin, progress);
                    if (progress >= 1)
                    {
                        break;
                    }

                    await Task.Delay(16, cancellation.Token);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                // The home view was closed during the transition.
                return false;
            }
            finally
            {
                if (ReferenceEquals(_launchAnimationCancellation, cancellation))
                {
                    _launchAnimationCancellation = null;
                }

                cancellation.Dispose();
            }
        }

        public void EndLaunchAnimation()
        {
            _launchAnimationCancellation?.Cancel();
            _launchAnimationCancellation = null;
            LaunchAnimationLayer.IsVisible = false;
        }

        public void SetRunningApplication(ApplicationData application)
        {
            _runningApplication = application;
            _runningApplicationId = application?.Id;
            UpdateRunningBadgePosition();
            CloseGamePrompt.IsVisible = GameTitle.IsVisible && _runningApplicationId is not null;
        }

        public void FocusApplication(ApplicationData application)
        {
            if (application is null)
            {
                return;
            }

            GameListBox.SelectedItem = application;
            FocusGames();
        }

        private void UpdateRunningBadgePosition()
        {
            if (_runningApplication is null ||
                GameListBox.ContainerFromItem(_runningApplication) is not Control tile ||
                tile.TranslatePoint(new Point(), HomeCanvas) is not { } position ||
                position.X + 203 < 147 || position.X + 203 > HomeCanvas.Width)
            {
                RunningPlayerAvatar.IsVisible = false;
                RunningGameBadge.IsVisible = false;
                return;
            }

            Canvas.SetLeft(RunningPlayerAvatar, position.X + 33);
            Canvas.SetTop(RunningPlayerAvatar, position.Y + 293);
            Canvas.SetLeft(RunningGameBadge, position.X + 120);
            Canvas.SetTop(RunningGameBadge, position.Y + 294);
            RunningPlayerAvatar.IsVisible = true;
            RunningGameBadge.IsVisible = true;
        }

        private MainWindow GetMainWindow() => TopLevel.GetTopLevel(this) switch
        {
            MainWindow window => window,
            HomeOverlayWindow overlay => overlay.MainWindow,
            _ => null,
        };

        private void UpdateLaunchAnimation(Point origin, double progress)
        {
            double eased = progress * progress * (3 - 2 * progress);
            double size = 384 * (1 + 0.25 * eased);

            LaunchDim.Opacity = eased * eased;
            LaunchGhost.Width = LaunchGhost.Height = size;
            Canvas.SetLeft(LaunchGhost, origin.X - (size - 384) / 2);
            Canvas.SetTop(LaunchGhost, origin.Y - (size - 384) / 2);
            LaunchGhost.Opacity = 0.65 * (1 - eased);
        }

        public void StopHomeGamepadInput()
        {
            _gamepadTimer.Stop();
            ReleaseHomeGamepad();
        }

        private void ReleaseHomeGamepad()
        {
            _homeGamepad?.Dispose();
            _homeGamepad = null;
            _homeGamepadId = null;
            _homeGamepadConfig = null;
            _ignoreNextGamepadSnapshot = true;
            _heldDirection = HomeDirection.None;
            _aWasDown = _bWasDown = _plusWasDown = _xWasDown = false;
        }

        private void PollHomeGamepad(object sender, EventArgs args)
        {
            MainWindow window = GetMainWindow();
            bool isActive = TopLevel.GetTopLevel(this) is HomeOverlayWindow overlay
                ? overlay.IsActive
                : window?.IsActive == true;
            if (window is null || !isActive ||
                (window.ViewModel.AppHost is not null && !window.IsHomeOverlayVisible) ||
                window.ViewModel.ShowLoadProgress || LaunchAnimationLayer.IsVisible ||
                window.InputManager is null)
            {
                ReleaseHomeGamepad();
                return;
            }

            ReadOnlySpan<string> ids = window.InputManager.GamepadDriver.GamepadsIds;
            StandardControllerInputConfig playerConfig = null;
            StandardControllerInputConfig handheldConfig = null;
            foreach (InputConfig config in ConfigurationState.Instance.Hid.InputConfig.Value)
            {
                if (config is not StandardControllerInputConfig controllerConfig)
                {
                    continue;
                }

                if (controllerConfig.PlayerIndex == PlayerIndex.Player1)
                {
                    playerConfig = controllerConfig;
                    break;
                }

                if (controllerConfig.PlayerIndex == PlayerIndex.Handheld)
                {
                    handheldConfig = controllerConfig;
                }
            }

            playerConfig ??= handheldConfig;

            string id = null;
            foreach (string candidate in ids)
            {
                if (candidate == playerConfig?.Id)
                {
                    id = candidate;
                    break;
                }
            }

            if (id is null && ids.Length > 0)
            {
                id = ids[0];
            }

            if (id != _homeGamepadId || (id is not null && (_homeGamepad is null || !_homeGamepad.IsConnected)))
            {
                _homeGamepad?.Dispose();
                _homeGamepad = id is null ? null : window.InputManager.GamepadDriver.GetGamepad(id);
                _homeGamepadId = id;
                _homeGamepadConfig = null;
                _ignoreNextGamepadSnapshot = true;
            }

            if (_homeGamepad is null)
            {
                return;
            }

            StandardControllerInputConfig activeConfig = id == playerConfig?.Id ? playerConfig : null;
            if (!ReferenceEquals(activeConfig, _homeGamepadConfig))
            {
                if (activeConfig is null)
                {
                    _homeGamepad.Dispose();
                    _homeGamepad = window.InputManager.GamepadDriver.GetGamepad(id);
                    if (_homeGamepad is null)
                    {
                        return;
                    }
                }

                if (activeConfig is not null)
                {
                    _homeGamepad.SetConfiguration(activeConfig);
                }

                _homeGamepadConfig = activeConfig;
                _ignoreNextGamepadSnapshot = true;
            }

            GamepadStateSnapshot state = _homeGamepad.GetMappedStateSnapshot();
            bool aDown = state.IsPressed(GamepadButtonInputId.A);
            bool bDown = state.IsPressed(GamepadButtonInputId.B);
            bool plusDown = state.IsPressed(GamepadButtonInputId.Plus);
            bool xDown = state.IsPressed(GamepadButtonInputId.X);
            HomeDirection direction = GetDirection(state);

            if (_ignoreNextGamepadSnapshot)
            {
                _aWasDown = aDown;
                _bWasDown = bDown;
                _plusWasDown = plusDown;
                _xWasDown = xDown;
                _heldDirection = direction;
                _nextDirectionRepeat = Environment.TickCount64 + 350;
                _ignoreNextGamepadSnapshot = false;
                return;
            }

            if (GameListBox.ContextFlyout?.IsOpen == true)
            {
                if (bDown && !_bWasDown)
                {
                    GameListBox.ContextFlyout.Hide();
                }
            }
            else
            {
                long now = Environment.TickCount64;
                if (direction != _heldDirection)
                {
                    _heldDirection = direction;
                    _nextDirectionRepeat = now + 350;
                    NavigateHome(direction);
                }
                else if (direction != HomeDirection.None && now >= _nextDirectionRepeat)
                {
                    _nextDirectionRepeat = now + 110;
                    NavigateHome(direction);
                }

                if (aDown && !_aWasDown)
                {
                    ConfirmHomeSelection();
                }
                else if (bDown && !_bWasDown && GameTitle.IsVisible)
                {
                    (_lastDockButton ?? DockOnline).Focus();
                }
                else if (plusDown && !_plusWasDown && GameTitle.IsVisible)
                {
                    GameListBox.ContextFlyout?.ShowAt(GameListBox);
                }
                else if (xDown && !_xWasDown && _runningApplicationId is not null)
                {
                    window.CloseRunningGameFromHome();
                }
            }

            _aWasDown = aDown;
            _bWasDown = bDown;
            _plusWasDown = plusDown;
            _xWasDown = xDown;
        }

        private static HomeDirection GetDirection(GamepadStateSnapshot state)
        {
            if (state.IsPressed(GamepadButtonInputId.DpadLeft)) return HomeDirection.Left;
            if (state.IsPressed(GamepadButtonInputId.DpadRight)) return HomeDirection.Right;
            if (state.IsPressed(GamepadButtonInputId.DpadUp)) return HomeDirection.Up;
            if (state.IsPressed(GamepadButtonInputId.DpadDown)) return HomeDirection.Down;

            (float x, float y) = state.GetStick(Ryujinx.Input.StickInputId.Left);
            if (Math.Abs(x) >= Math.Abs(y) && Math.Abs(x) > 0.55f)
                return x < 0 ? HomeDirection.Left : HomeDirection.Right;
            if (Math.Abs(y) > 0.55f)
                return y > 0 ? HomeDirection.Up : HomeDirection.Down;

            return HomeDirection.None;
        }

        private void NavigateHome(HomeDirection direction)
        {
            if (GameTitle.IsVisible)
            {
                if (direction == HomeDirection.Down)
                {
                    (_lastDockButton ?? DockOnline).Focus();
                }
                else if (direction is HomeDirection.Left or HomeDirection.Right)
                {
                    int next = GameListBox.SelectedIndex + (direction == HomeDirection.Right ? 1 : -1);
                    if (next >= 0 && next < GameListBox.Items.Count)
                    {
                        GameListBox.SelectedIndex = next;
                        GameListBox.ScrollIntoView(next);
                    }
                }
            }
            else if (direction == HomeDirection.Up && GameListBox.Items.Count > 0)
            {
                FocusGames();
            }
            else if (direction is HomeDirection.Left or HomeDirection.Right)
            {
                MoveDockSelection(direction == HomeDirection.Right ? 1 : -1);
            }
        }

        private void ConfirmHomeSelection()
        {
            if (GameTitle.IsVisible && GameListBox.SelectedItem is ApplicationData selected)
            {
                OpenApplication(selected);
            }
            else
            {
                (_lastDockButton ?? DockOnline).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        private void UpdateClock() => HomeClock.Text = DateTime.Now.ToString("HH:mm");

        private void GameList_GotFocus(object sender, GotFocusEventArgs args)
        {
            ShowGameSelection();
        }

        private void ShowGameSelection()
        {
            bool hasSelection = GameListBox.SelectedItem is ApplicationData;
            GameListBox.Classes.Set("gameFocused", hasSelection);
            GameTitle.IsVisible = hasSelection;
            UpdateGameTitlePosition();
            OptionsPrompt.IsVisible = hasSelection;
            CloseGamePrompt.IsVisible = hasSelection && _runningApplicationId is not null;
            DockLabel.IsVisible = false;
            _lastDockButton?.Classes.Set("homeSelected", false);
        }

        private void GameList_LostFocus(object sender, RoutedEventArgs args)
        {
            GameListBox.Classes.Set("gameFocused", false);
            GameTitle.IsVisible = false;
            OptionsPrompt.IsVisible = false;
            CloseGamePrompt.IsVisible = false;
        }

        private void DockAction_GotFocus(object sender, GotFocusEventArgs args)
        {
            if (sender is Button button)
            {
                ShowDockLabel(button);
                GameListBox.Classes.Set("gameFocused", false);
                GameTitle.IsVisible = false;
                OptionsPrompt.IsVisible = false;
                CloseGamePrompt.IsVisible = false;
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

        private void FocusGames_OnClick(object sender, RoutedEventArgs args) => FocusGames();

        private void FocusGames()
        {
            if (GameListBox.Items.Count == 0)
            {
                return;
            }

            if (GameListBox.SelectedIndex < 0)
            {
                GameListBox.SelectedIndex = 0;
            }

            GameListBox.ScrollIntoView(GameListBox.SelectedIndex);
            bool focused = GameListBox.Focus();
            if (!focused && GameListBox.ContainerFromItem(GameListBox.SelectedItem) is Control item)
            {
                focused = item.Focus();
            }

            if (focused)
            {
                ShowGameSelection();
            }
        }

        private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs args) =>
            Dispatcher.UIThread.Post(UpdateGameTitlePosition, DispatcherPriority.Loaded);

        private void UpdateGameTitlePosition()
        {
            if (GameListBox.SelectedItem is not ApplicationData selected ||
                GameListBox.ContainerFromItem(selected) is not Control container ||
                container.TranslatePoint(new Point(), HomeCanvas) is not { } position)
            {
                return;
            }

            // Align the text with the icon inside the selected tile.
            const double titleIconAndSpacing = 43 + 19;
            double textLeft = Math.Clamp(position.X + 11, titleIconAndSpacing + 31, HomeCanvas.Width - 96);
            double left = textLeft - titleIconAndSpacing;
            double availableWidth = HomeCanvas.Width - textLeft - 31;
            double titleWidth = Math.Min(630, Math.Max(96, availableWidth));

            if (Math.Abs(GameTitleName.Width - titleWidth) > 0.5)
            {
                GameTitleName.Width = titleWidth;
            }

            if (Math.Abs(Canvas.GetLeft(GameTitle) - left) > 0.5)
            {
                Canvas.SetLeft(GameTitle, left);
            }

            double top = Math.Max(0, position.Y - 58);
            if (Math.Abs(Canvas.GetTop(GameTitle) - top) > 0.5)
            {
                Canvas.SetTop(GameTitle, top);
            }
        }

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
            if (args.Key == Key.Up && GameListBox.Items.Count > 0)
            {
                FocusGames();
                args.Handled = true;
                return;
            }

            if (args.Key is not (Key.Left or Key.Right) || _lastDockButton is null)
                return;

            args.Handled = MoveDockSelection(args.Key == Key.Right ? 1 : -1);
        }

        private bool MoveDockSelection(int step)
        {
            int index = DockBar.Children.IndexOf(_lastDockButton ?? DockOnline);
            int nextIndex = index + step;

            if (nextIndex >= 0 && nextIndex < DockBar.Children.Count && DockBar.Children[nextIndex] is Button nextButton)
            {
                nextButton.Focus();
                return true;
            }

            return false;
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
            GetMainWindow()?.Close();
        }

        private void CloseRunningGame_OnClick(object sender, RoutedEventArgs args) =>
            GetMainWindow()?.CloseRunningGameFromHome();

        private void ShowOptions_OnClick(object sender, RoutedEventArgs args)
        {
            if (GameTitle.IsVisible)
                GameListBox.ContextFlyout?.ShowAt(GameListBox);
        }

        public void GameList_DoubleTapped(object sender, TappedEventArgs args)
        {
            if (sender is ListBox { SelectedItem: ApplicationData selected })
                OpenApplication(selected);
        }

        private void OpenSettings_OnClick(object sender, RoutedEventArgs args)
        {
            if (GetMainWindow() is MainWindow window)
                window.OpenSettingsFromHome();
        }

        private void OpenInputSettings_OnClick(object sender, RoutedEventArgs args)
        {
            if (GetMainWindow() is MainWindow window)
                window.OpenSettingsFromHome("InputPage");
        }

        private void OpenNetworkSettings_OnClick(object sender, RoutedEventArgs args)
        {
            if (GetMainWindow() is MainWindow window)
                window.OpenSettingsFromHome("NetworkPage");
        }

        private void OpenSystemSettings_OnClick(object sender, RoutedEventArgs args)
        {
            if (GetMainWindow() is MainWindow window)
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
