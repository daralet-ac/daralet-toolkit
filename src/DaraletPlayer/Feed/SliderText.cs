using System.Globalization;
using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A slider's number as ImGui would write it from its printf format ("%d%%", "%d px", "%.2f"): the plugin draws the
    /// text itself over the slider's art (Sliders), so it reads the same formats. Only what the plugin's sliders use:
    /// %d and %i (rounded), %f with a precision (6 without one, as printf), and %%; anything else is left as written.
    /// </summary>
    internal static class SliderText {
        public static string Format(string format, double value) {
            if (string.IsNullOrEmpty(format)) {
                return "";
            }

            var text = new StringBuilder();

            for (var i = 0; i < format.Length; i++) {
                if (format[i] != '%' || i + 1 >= format.Length) {
                    text.Append(format[i]);
                    continue;
                }

                if (format[i + 1] == '%') {
                    text.Append('%');
                    i++;
                    continue;
                }

                // flags and width are skipped; a precision is kept
                var j = i + 1;

                while (j < format.Length && "-+ #0123456789".IndexOf(format[j]) >= 0) {
                    j++;
                }

                var precision = -1;

                if (j < format.Length && format[j] == '.') {
                    j++;
                    precision = 0;

                    while (j < format.Length && char.IsDigit(format[j])) {
                        precision = precision * 10 + (format[j] - '0');
                        j++;
                    }
                }

                if (j >= format.Length) {
                    text.Append(format, i, format.Length - i);
                    break;
                }

                switch (format[j]) {
                    case 'd':
                    case 'i':
                    case 'u':
                        text.Append(System.Math.Round(value).ToString("0", CultureInfo.InvariantCulture));
                        break;
                    case 'f':
                    case 'F':
                        text.Append(value.ToString("F" + (precision < 0 ? 6 : precision), CultureInfo.InvariantCulture));
                        break;
                    default:
                        text.Append(format, i, j - i + 1);
                        break;
                }

                i = j;
            }

            return text.ToString();
        }
    }
}
