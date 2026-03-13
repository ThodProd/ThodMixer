using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace Midi
{
    public partial class App : Application
    {
        private TaskbarIcon? _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Получаем иконку из App.xaml
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        }

        private void Tray_Open(object sender, RoutedEventArgs e)
        {
            var window = Current.MainWindow;

            if (window == null)
                return;

            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }

        private void Tray_Exit(object sender, RoutedEventArgs e)
        {
            if (_trayIcon != null)
                _trayIcon.Dispose();

            Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_trayIcon != null)
                _trayIcon.Dispose();

            base.OnExit(e);
        }
    }
}