using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GatewayApp.Views.Dialogs;

internal static class DialogReadOnlyHelper
{
    public static void SetReadOnly(DependencyObject parent, bool disableSelectors)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            switch (child)
            {
                case TextBox textBox:
                    textBox.IsReadOnly = true;
                    break;
                case ComboBox comboBox when disableSelectors:
                    comboBox.IsEnabled = false;
                    break;
                case RadioButton radioButton when disableSelectors:
                    radioButton.IsEnabled = false;
                    break;
            }

            SetReadOnly(child, disableSelectors);
        }
    }
}
