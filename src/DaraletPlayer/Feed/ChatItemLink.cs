using System;
using System.Text;
using System.Text.RegularExpressions;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// How an item linked in chat is written in the line itself: only "[Name]", so it reads cleanly to anyone without the
    /// plugin. The plugin takes any "[...]" in what a player said as a possible link, and asks the server (@playerui shared)
    /// what that player linked by that name. Only their own links are found under their name, so typing "[Something]" can't
    /// pass anyone's item off as anything, and text in brackets that nobody linked finds nothing and stays text.
    /// </summary>
    public static class ChatItemLink {
        public static readonly Regex Pattern = new Regex(@"\[(?<name>[^\[\]\r\n]{1,80})\]", RegexOptions.CultureInvariant);

        /// <summary>
        /// A link as one word for a command: who said it and the name, in base64 with - and _ (SharedItemStore.Key in
        /// daralet-ACE, which reads these).
        /// </summary>
        public static string Key(string speaker, string name) {
            var link = $"{(speaker ?? "").TrimStart('+')}\n{name}".ToLowerInvariant();
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(link)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
