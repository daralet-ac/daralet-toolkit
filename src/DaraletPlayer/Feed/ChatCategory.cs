namespace DaraletPlayer.Feed {
    /// <summary>
    /// What a chat window can be set to show: the game's own chat options (its ChatDisplayMask, in daralet-ACE's
    /// libs/entity/Enum), whose bits are the chat types each takes in, with Society and Olthoi chat, which the game's options
    /// don't have, as their own.
    /// </summary>
    public enum ChatCategory {
        /// <summary>The game telling you things: system messages, levels and skills, appraisals, crafting, recalls.</summary>
        Gameplay,
        /// <summary>Help, abuse and admin channels: the game shows these in the main window whatever its options say.</summary>
        Alerts,
        /// <summary>Speech and emotes nearby.</summary>
        AreaChat,
        Tells,
        Combat,
        Magic,
        Allegiance,
        Fellowship,
        Errors,
        General,
        Trade,
        Lfg,
        Roleplay,
        Society,
        Olthoi,
    }
}
