using Avalonia.Controls;
using Avalonia.Input;
using Ryujinx.Ava.UI.Controls;

namespace Ryujinx.Ava.UI.Windows
{
    public partial class HomeOverlayWindow : Window
    {
        public MainWindow MainWindow { get; }
        public ApplicationGridView Grid => HomeGrid;

        public HomeOverlayWindow()
        {
            InitializeComponent();
        }

        public HomeOverlayWindow(MainWindow mainWindow) : this()
        {
            MainWindow = mainWindow;
            DataContext = mainWindow.ViewModel;
            HomeGrid.ApplicationOpened += mainWindow.Application_Opened;
        }

        private void HomeHotkey_KeyUp(object sender, KeyEventArgs e)
        {
            if (MainWindow?.TryToggleHomeWithKey(e.Key) == true)
            {
                e.Handled = true;
            }
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (MainWindow is not null)
            {
                HomeGrid.ApplicationOpened -= MainWindow.Application_Opened;
            }
            base.OnClosed(e);
        }
    }
}
