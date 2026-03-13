using AudioMixerApp;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows;

namespace Midi
{
    public partial class App : Application
    {
        private TaskbarIcon? _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");

            // Показываем окно при старте
            Current.MainWindow?.Show();
        }

        private void Tray_Open(object sender, RoutedEventArgs e)
        {
            if (Current.MainWindow == null)
                return;

            Current.MainWindow.Show();
            Current.MainWindow.WindowState = WindowState.Normal;
            Current.MainWindow.Activate();
        }

        private void Tray_Exit(object sender, RoutedEventArgs e)
        {
            if (_trayIcon != null)
                _trayIcon.Dispose();

            if (Current.MainWindow is MainWindow mw)
                mw.ExitApplication();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_trayIcon != null)
                _trayIcon.Dispose();

            base.OnExit(e);
        }
    }
}