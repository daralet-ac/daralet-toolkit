using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The Config page as the game shows it (gmConfigUI, its screenshots): six groups in its order, with the client's
    /// preference globals found from gmClient::InitUIPreferences (0x4037B0), each attached under the label string id
    /// beside it (ID_Sound_EffectVolume...), and the labels and tooltips from client_local_English.dat's string table
    /// 0x23000003.
    /// </summary>
    public static class ConfigOptions {
        public static readonly IReadOnlyList<(string Title, IReadOnlyList<ConfigOption> Options)> Groups = new[] {
            ("Sound Options", (IReadOnlyList<ConfigOption>)new[] {
                new ConfigOption(ConfigKind.List, "Sound Features", 0x8375C8, "Select stereo or mono sound."),
                new ConfigOption(ConfigKind.LightSlider, "Sound Effects", 0x8375B0, "Check to enable sound effects. The "
                    + "slider adjusts volume for sound effects like combat and creature noises.", 0x8375B4, true),
                new ConfigOption(ConfigKind.LightSlider, "Ambient Sounds", 0x8375B8, "Check to enable ambient sounds. The "
                    + "slider adjusts volume for ambient sounds like the background noises in towns.", 0x8375BC, true),
                new ConfigOption(ConfigKind.LightSlider, "Interface Sounds", 0x8375C0, "Check to enable interface sounds. "
                    + "The slider adjusts volume for interface sounds like alerts or button presses.", 0x8375C4, true),
                new ConfigOption(ConfigKind.Light, "Play Sounds Only When Active", 0x8375CC, "When selected, this option "
                    + "will mute the sounds in Asheron's Call whenever it is not the currently active application."),
            }),
            ("Camera Options", new[] {
                new ConfigOption(ConfigKind.Slider, "Camera Stiffness", 0x8375A8,
                    "Determines how quickly the camera will adjust position in response to player movement.",
                    low: "Soft", high: "Hard"),
                new ConfigOption(ConfigKind.Slider, "Camera Adjustment Speed", 0x8375AC,
                    "Determines how quickly the camera will adjust position in response to mouse movement.",
                    low: "Slow", high: "Fast"),
                new ConfigOption(ConfigKind.Slider, "Field of View", 0x8375EC, "This allows the user to zoom the camera in "
                    + "or out. Zooming in narrows your field of vision and allows you to see less of your surroundings.",
                    low: "Narrow", high: "Wide"),
                new ConfigOption(ConfigKind.Light, "Align camera to slope", 0x8375A4,
                    "Causes the tilt of the camera to adjust based on the steepness of the terrain under the player."),
            }),
            ("Graphics Options", new[] {
                new ConfigOption(ConfigKind.List, "Resolution", 0x837594, "Allows you to select from a list of supported "
                    + "display modes. Higher resolutions typically look better at the expense of performance."),
                new ConfigOption(ConfigKind.Light, "Full Screen", 0x837598, "When checked (recommended), the game runs in "
                    + "full screen mode. If unchecked, the game runs in a window on your desktop."),
                new ConfigOption(ConfigKind.Light, "Sync with Refresh Rate", 0x8375A0, "When checked (recommended), "
                    + "eliminates horizontal 'tearing' artifacts that may be visible at the expense of some performance."),
                new ConfigOption(ConfigKind.Slider, "Screen Brightness", 0x837600, "Increases or decreases the brightness "
                    + "of the screen. This option only controls brightness in full screen mode.", low: "Dark", high: "Bright"),
                new ConfigOption(ConfigKind.Light, "Adaptive Degrade", 0x837610, "Automatically adjusts the aggressiveness "
                    + "of adaptive degrading based on the current rendering performance of your system."),
                new ConfigOption(ConfigKind.Slider, "Adaptive Degrade Bias", 0x837614, "Manually adjusts aggressiveness of "
                    + "adaptive degrading. Moving the slider left will draw degrades at closer distances, resulting in "
                    + "better performance.", low: "Speed", high: "Detail"),
                new ConfigOption(ConfigKind.Slider, "Degrade Distance", 0x837618, "Scales the distance for adaptive "
                    + "degrading. Lower settings draw degrades at closer distances, resulting in better performance.",
                    low: "Close", high: "Far"),
            }),
            ("Rendering Quality Options", new[] {
                new ConfigOption(ConfigKind.List, "Landscape Texture Detail", 0x8375F0, "Adjusts the resolution of "
                    + "landscape textures. Higher detail textures require more video memory but greatly increase quality."),
                new ConfigOption(ConfigKind.List, "Environment Texture", 0x8375F4, "Adjusts the resolution of object "
                    + "textures rather than landscape. Higher detail textures require more video memory."),
                new ConfigOption(ConfigKind.List, "Texture Filtering", 0x8375E0, "Quality of filter applied to 3D textures. "
                    + "Bilinear is faster but low quality. Anisotropic is highest quality but can be quite slow."),
                new ConfigOption(ConfigKind.List, "Landscape Draw Distance", 0x8375FC, "Adjusts how far away landscape "
                    + "geography will be rendered. Lower distances can increase performance."),
                new ConfigOption(ConfigKind.Light, "Environment Detail Textures", 0x8375E8, "Enabling this option improves "
                    + "the graphical quality of the buildings but can reduce performance."),
                new ConfigOption(ConfigKind.Light, "Multiple Pass Alpha", 0x83761C, "Multiple pass alpha will render alpha "
                    + "textures twice to provide a softer edge. Enabling this option can significantly decrease performance."),
            }),
            ("Input Options", new[] {
                new ConfigOption(ConfigKind.Slider, "Mouselook Sensitivity", 0x837688,
                    "Mouse sensitivity when you're in mouse look mode."),
                new ConfigOption(ConfigKind.Light, "Invert mouselook axes", 0x837690,
                    "This option reverses the up/down and left/right movements of the mouse in mouselook mode."),
                new ConfigOption(ConfigKind.Light, "Turn your character with camera turning.", 0x837694,
                    "When this option is enabled, your character turns as you turn with the camera turn button."),
            }),
            ("UI Options", new[] {
                new ConfigOption(ConfigKind.List, "Chat Font Face", 0x837564, "Changes the font used for text added to "
                    + "the chat log."),
                new ConfigOption(ConfigKind.List, "Chat Font Size", 0x837568, "Changes the size of the font used for text "
                    + "added to the chat log."),
            }),
        };
    }
}
