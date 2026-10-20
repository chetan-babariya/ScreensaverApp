using System;
using System.Globalization;

namespace ScreensaverApp.Configuration
{
    internal enum ClockStyle { Off, Minimal, Neon, Analog, Flip }
    internal enum ClockPosition { TopLeft, TopRight, Center, BottomLeft, BottomRight }
    internal sealed class ClockOptions
    {
        internal ClockStyle Style;
        internal ClockPosition Position = ClockPosition.BottomRight;
        internal int Size = 1, Color;
        internal bool Use24Hour = true, Seconds = true, Date = true;
        internal ClockOptions Copy() => (ClockOptions)MemberwiseClone();
        // A bounded, version-independent value also carries unsaved manager previews.
        internal string Encode() => string.Join(",", (int)Style, (int)Position, Size, Color,
            Use24Hour ? 1 : 0, Seconds ? 1 : 0, Date ? 1 : 0);
        internal static ClockOptions Decode(string value)
        {
            if (string.IsNullOrEmpty(value)) return new ClockOptions();
            string[] fields = value.Split(',');
            int[] values = new int[7]; int[] limits = { 4, 4, 2, 3, 1, 1, 1 };
            if (value.Length > 32 || fields.Length != 7) throw new FormatException("Invalid clock preferences.");
            for (int i = 0; i < values.Length; i++)
                if (!int.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]) || values[i] > limits[i])
                    throw new FormatException("Invalid clock preferences.");
            return new ClockOptions { Style = (ClockStyle)values[0], Position = (ClockPosition)values[1], Size = values[2], Color = values[3],
                Use24Hour = values[4] == 1, Seconds = values[5] == 1, Date = values[6] == 1 };
        }
        internal string TimeText(DateTime localTime) => localTime.ToString(
            (Use24Hour ? "HH:mm" : "hh:mm") + (Seconds ? ":ss" : "") + (Use24Hour ? "" : " tt"), CultureInfo.CurrentCulture);
    }
}
