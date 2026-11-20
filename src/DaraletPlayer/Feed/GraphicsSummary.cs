namespace DaraletPlayer.Feed {
    /// <summary>Every graphics setting in a line, for log.txt's session header and a test run's results.</summary>
    public static class GraphicsSummary {
        public static string Of(SettingsData data) {
            return data.DrawDistance.Summary() + "; " + data.Shading.Summary() + "; " + data.Water.Summary() + "; "
                + data.Image.Summary() + "; " + data.Grass.Summary() + "; " + data.SunShadows.Summary() + "; "
                + data.LampLight.Summary();
        }
    }
}
