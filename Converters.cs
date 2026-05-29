using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioMixerApp
{
    public class FirstStripVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool canDelete)
            {
                return canDelete ? Visibility.Visible : Visibility.Collapsed;
            }
            return value?.ToString() == "Дорожка 1" ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class MasterBusVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool canDelete)
            {
                return canDelete ? Visibility.Visible : Visibility.Collapsed;
            }
            return value?.ToString() == "Мастер" ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class VolumeToPercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double v)
            {
                double percent = Math.Round((v + 60) / 72 * 100);
                if (percent < 0) percent = 0;
                if (percent > 100) percent = 100;
                return percent + "%";
            }
            return "0%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}