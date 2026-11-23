using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fannyfique.App.Converters;

/// <summary>Masque l'élément si la valeur est nulle, vide, fausse ou zéro. Paramètre « invert » pour l'inverse.</summary>
public sealed class VisibleIfConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Vrai si la valeur est égale au paramètre (navigation par RadioButton).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string s && targetType.IsEnum ? Enum.Parse(targetType, s) : Binding.DoNothing;
}
