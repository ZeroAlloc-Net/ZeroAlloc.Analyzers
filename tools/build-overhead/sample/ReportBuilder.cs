using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sample.Unit__N__
{
    public class ReportBuilder
    {
        private static readonly Dictionary<string, string> Headings = new Dictionary<string, string>
        {
            { "summary", "Summary" },
            { "lines", "Lines" },
            { "totals", "Totals" },
        };

        private static readonly HashSet<string> Reserved = new HashSet<string> { "id", "total", "status" };

        public string Build(IList<Order> orders)
        {
            var sb = new StringBuilder();
            sb.Append(Headings["summary"] + ":\n");
            for (int i = 0; i < orders.Count; i++)
            {
                var o = orders[i];
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0};{1};{2}", o.Id, o.Customer, o.Total));
                sb.Append("\n");
                sb.Append(o.Status.ToString());
            }
            return sb.ToString();
        }

        public bool IsReserved(string name)
        {
            return Reserved.Contains(name.ToLower());
        }

        public string Trimmed(string value)
        {
            if (value.StartsWith("#"))
                return value.Substring(1).Trim();
            return value.Trim();
        }

        public string[] Columns(string header)
        {
            return header.Split(new[] { ',' });
        }

        public string Join(IEnumerable<string> parts)
        {
            string result = string.Empty;
            foreach (var p in parts)
                result = result + p + ",";
            return result.TrimEnd(',');
        }

        public object BoxedTotal(Order o)
        {
            object boxed = o.Total;
            return boxed;
        }

        public string Pad(int value)
        {
            return value.ToString().PadLeft(8);
        }
    }
}
