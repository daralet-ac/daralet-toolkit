using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    // These mirror PlayerUiItemStyleReply and PlayerUiItemStyle in daralet-ACE's PlayerUiSnapshots.cs.

    /// <summary>
    /// @playerui style's answer: the looks an item of yours has that you can choose (/style), in /style's order, so a
    /// look's number is its place here from 1; null when it has only the one. Problem says why it couldn't be asked about.
    /// </summary>
    [DataContract]
    public class ItemStylesInfo {
        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "problem")] public string Problem;
        [DataMember(Name = "styles")] public List<ItemStyleInfo> Styles;
    }

    /// <summary>A look of an item: Showing is the one it has now, Earned one a quest unlocked.</summary>
    [DataContract]
    public class ItemStyleInfo {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "showing")] public bool Showing;
        [DataMember(Name = "earned")] public bool Earned;
    }
}
