using System;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The radar colour the server gave an object (its RadarBlipColor, sent in its description), read from the client's
    /// own copy, as its radar reads it: gmRadarUI::GetBlipColor (0x4D8350) takes the object's ACCWeenieObject and looks
    /// at the colour at +0x104 first (1 to 10, RadarColor's order), its description flags at +0x100 only when that's 0.
    /// Decal doesn't pass the colour on. The object is ClientObjMaintSystem::GetWeenieObject(id) (0x5583F0, static
    /// cdecl), its id at +0x8. Game thread only, once logged in.
    /// </summary>
    internal static class ClientRadar {
        private const int GetWeenieObject = 0x5583F0;
        private const int IdOffset = 0x8;
        private const int BlipColorOffset = 0x104;

        private static bool broken;

        /// <summary>The object's radar colour (1 to 10), or 0 for none (or no telling).</summary>
        public static unsafe int BlipColor(int id) {
            if (broken || id == 0) {
                return 0;
            }

            try {
                var weenie = ((delegate* unmanaged[Cdecl]<uint, byte*>)GetWeenieObject)(unchecked((uint)id));

                // the id checked, in case the object found isn't the one asked for
                if (weenie == null || *(int*)(weenie + IdOffset) != id) {
                    return 0;
                }

                var color = *(int*)(weenie + BlipColorOffset);
                return color >= 1 && color <= 10 ? color : 0;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The radar can't read the server's blip colours, so it uses its own until the plugin next "
                    + "starts: " + ex);
                return 0;
            }
        }
    }
}
