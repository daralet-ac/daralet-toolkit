using System;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What the server told the client about showing an object on the radar, read from the client's own copy, as its
    /// radar reads it. The object is ClientObjMaintSystem::GetWeenieObject(id) (0x5583F0, static cdecl), an
    /// ACCWeenieObject, its id at +0x8 and its PublicWeenieDesc at +0x98 (AcClient's layout): the description flags at
    /// +0x100, the RadarBlipColor at +0x104 and the RadarBehavior at +0x108. gmRadarUI::GetBlipColor (0x4D8350) looks at
    /// the colour first (1 to 10, RadarColor's order), the flags only when that's 0. Decal passes neither on. Game
    /// thread only, once logged in.
    /// </summary>
    internal static class ClientRadar {
        private const int GetWeenieObject = 0x5583F0;
        private const int IdOffset = 0x8;
        private const int BlipColorOffset = 0x104;
        private const int RadarBehaviorOffset = 0x108;

        /// <summary>RadarBehavior (daralet-ACE's libs/entity/Enum): never on the radar, whatever its kind.</summary>
        public const int ShowNever = 1;

        private static bool broken;

        /// <summary>The object's radar colour (1 to 10), or 0 for none (or no telling).</summary>
        public static unsafe int BlipColor(int id) {
            var weenie = Weenie(id);

            if (weenie == null) {
                return 0;
            }

            var color = *(int*)(weenie + BlipColorOffset);
            return color >= 1 && color <= 10 ? color : 0;
        }

        /// <summary>
        /// The object's RadarBehavior (0 undefined, 1 never, 2 while moving, 3 while attacking, 4 always), or 0 for no
        /// telling. Invisible things (spawners, hidden creatures, pets) are sent as never.
        /// </summary>
        public static unsafe int Behavior(int id) {
            var weenie = Weenie(id);
            return weenie == null ? 0 : *(int*)(weenie + RadarBehaviorOffset);
        }

        private static unsafe byte* Weenie(int id) {
            if (broken || id == 0) {
                return null;
            }

            try {
                var weenie = ((delegate* unmanaged[Cdecl]<uint, byte*>)GetWeenieObject)(unchecked((uint)id));

                // the id checked, in case the object found isn't the one asked for
                return weenie == null || *(int*)(weenie + IdOffset) != id ? null : weenie;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The radar can't read what the server says about showing things on it, so it shows "
                    + "everything in its own colours until the plugin next starts: " + ex);
                return null;
            }
        }
    }
}
