using System;
using System.Collections.Generic;

namespace Sample.Unit__N__
{
    public class Customer
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public Address Address { get; set; }
        public List<Order> Orders { get; set; } = new List<Order>();

        public override string ToString()
        {
            return string.Format("{0} <{1}>", Name, Email);
        }
    }

    public class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
        public string PostalCode { get; set; }
    }

    public struct Money
    {
        public decimal Amount;
        public string Currency;

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }
    }

    public class Ledger
    {
        private readonly Dictionary<Money, int> _counts = new Dictionary<Money, int>();
        private readonly List<Money> _entries = new List<Money>();

        public void Record(Money m)
        {
            _entries.Add(m);
            int count;
            if (_counts.TryGetValue(m, out count))
                _counts[m] = count + 1;
            else
                _counts[m] = 1;
        }

        public decimal Sum(string currency)
        {
            decimal total = 0;
            foreach (var e in _entries)
                if (e.Currency == currency) total += e.Amount;
            return total;
        }

        public bool Contains(Money m)
        {
            return _entries.Contains(m);
        }
    }

    public class Registry
    {
        private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();

        public void Register(Type t)
        {
            _types[t.Name] = t;
        }

        public object Create(string name)
        {
            return Activator.CreateInstance(_types[name]);
        }
    }
}
