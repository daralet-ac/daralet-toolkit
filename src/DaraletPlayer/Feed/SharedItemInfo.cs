using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    // These mirror PlayerUiSharedItem, PlayerUiShareReply and PlayerUiSharedLookup in daralet-ACE's PlayerUiSnapshots.cs.

    /// <summary>
    /// An item someone linked in chat, as it was when they linked it: the word it was asked about with (ChatItemLink.Key),
    /// what goes in the chat line, who linked it, where it's worn or wielded (EquipMask, 0 for neither: what the Appraisal
    /// window compares it with) and its description.
    /// </summary>
    [DataContract]
    public class SharedItemInfo {
        [DataMember(Name = "key")] public string Key;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "owner")] public string Owner;
        [DataMember(Name = "slots")] public int Slots;
        [DataMember(Name = "item")] public CorpseItemInfo Item;
    }

    /// <summary>@playerui shareitem's and shareagain's answer: the item asked about, and its share or why there's none.</summary>
    [DataContract]
    public class ShareReplyInfo {
        [DataMember(Name = "guid")] public uint Guid;
        [DataMember(Name = "problem")] public string Problem;
        [DataMember(Name = "shared")] public SharedItemInfo Shared;
    }

    /// <summary>@playerui shared's answer: the links' items, and the keys there's none for.</summary>
    [DataContract]
    public class SharedLookupInfo {
        [DataMember(Name = "items")] public List<SharedItemInfo> Items;
        [DataMember(Name = "missing")] public List<string> Missing;
    }
}
