using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>A heading and its rows, in a creature's or player's appraisal.</summary>
    public class AppraisalSection {
        public AppraisalSection(string title, List<AppraisalRow> rows) {
            Title = title;
            Rows = rows;
        }

        public string Title { get; }
        public List<AppraisalRow> Rows { get; }
    }
}
