using System.Collections.Generic;
using SteelGrid.Core.Layout;
using SteelGrid.Core.Model;
using SteelGrid.Core.Report;

namespace SteelGrid.Plugin.Commands
{
    internal static class PluginTableRows
    {
        public static List<ReportItem> GetHorizontalRows(LayoutResult result)
        {
            var rows = ReportTables.ReportTable(result, "横向");
            if (result.Spec.Horizontal.Type != BarType.TwistedSquare)
            {
                return rows;
            }

            foreach (var row in rows)
            {
                row.FirstHole = "";
                row.LastHole = "";
            }

            return rows;
        }
    }
}
