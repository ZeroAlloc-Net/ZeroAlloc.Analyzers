using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sample.Unit__N__
{
    public class Parser
    {
        public List<OrderLine> ParseLines(string input)
        {
            var result = new List<OrderLine>();
            foreach (var raw in input.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var parts = line.Split(';');
                if (!Regex.IsMatch(parts[0], "^[A-Z]{3}-[0-9]+$")) continue;
                result.Add(new OrderLine
                {
                    Sku = parts[0],
                    Quantity = int.Parse(parts[1], CultureInfo.InvariantCulture),
                    Price = decimal.Parse(parts[2], CultureInfo.InvariantCulture),
                });
            }
            return result;
        }

        public OrderStatus ParseStatus(string text)
        {
            return (OrderStatus)Enum.Parse(typeof(OrderStatus), text);
        }

        public bool IsKnownStatus(int value)
        {
            return Enum.IsDefined(typeof(OrderStatus), value);
        }

        public string[] StatusNames()
        {
            return Enum.GetNames(typeof(OrderStatus));
        }

        public int CountDigits(string s)
        {
            int count = 0;
            foreach (char c in s.ToCharArray())
                if (char.IsDigit(c)) count++;
            return count;
        }

        public string Prefix(string s)
        {
            int idx = s.IndexOf("-");
            return idx < 0 ? s : s.Substring(0, idx);
        }
    }
}
