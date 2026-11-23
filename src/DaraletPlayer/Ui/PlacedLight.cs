namespace DaraletPlayer.Ui {
    /// <summary>A light the dats place (PlacedLights), in its landblock's meters.</summary>
    internal struct PlacedLight {
        /// <summary>The landblock, 0xXXYY.</summary>
        public uint Block;

        public float X;
        public float Y;
        public float Z;

        /// <summary>0xRRGGBB, unused for a fire, which takes the fires' warm colour and flicker.</summary>
        public uint Colour;

        public float Intensity;
        public float Falloff;

        /// <summary>A surface painted with fire (PlacedLights.Fires.cs): warm and flickering, as FireLights' fires.</summary>
        public bool Fire;

        /// <summary>A fire's flicker phase, so no two flicker together.</summary>
        public int Seed;
    }
}
