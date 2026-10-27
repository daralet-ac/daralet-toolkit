namespace DaraletPlayer.Feed {
    /// <summary>A stack's size short enough for an icon's corner: 9999 as it is, then thousands to a tenth, "50.1k".</summary>
    public static class StackText {
        /// <summary>
        /// The size as shown on an icon. Rounded down to the hundred, so it never says more than there is: 50,199 is 50.1k,
        /// 50,000 is 50k. With tenths false, whole thousands only (50k), for when "50.1k" doesn't fit.
        /// </summary>
        public static string Short(int stack, bool tenths = true) {
            if (stack < 10000) {
                return stack.ToString();
            }

            var hundreds = stack / 100;

            if (!tenths || hundreds % 10 == 0) {
                return $"{hundreds / 10}k";
            }

            return $"{hundreds / 10}.{hundreds % 10}k";
        }

        /// <summary>
        /// A stack size as Decal gives it. The game sends it as an unsigned 16-bit number; read as a signed one, anything
        /// over 32,767 comes out negative (50,000 as -15,536), which hid a 50,000 pyreal stack's count.
        /// </summary>
        public static int FromDecal(int value) {
            return value < 0 && value >= short.MinValue ? value + 65536 : value;
        }
    }
}
