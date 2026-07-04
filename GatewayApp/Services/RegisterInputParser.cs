using GatewayApp.Models;
using System.Globalization;

namespace GatewayApp.Services;

internal static class RegisterInputParser
{
    public static bool TryParse(MappingEntry entry, string input, out int raw, out string error)
    {
        raw = 0;
        error = string.Empty;

        if (entry.DisplayType == DisplayType.ScaledReal)
        {
            if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var scaled)
                && !double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out scaled))
            {
                error = Loc.Text("NumericRequired");
                return false;
            }

            var signed = (int)Math.Round(scaled * Math.Max(1, entry.RealScale));
            if (signed is < short.MinValue or > short.MaxValue)
            {
                error = Loc.Text("Int16Range");
                return false;
            }

            raw = unchecked((ushort)(short)signed);
            return true;
        }

        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && !int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
        {
            error = Loc.Text("IntegerInputRequired");
            return false;
        }

        if (value is < short.MinValue or > short.MaxValue)
        {
            error = Loc.Text("Int16Range");
            return false;
        }

        raw = unchecked((ushort)(short)value);
        return true;
    }
}
