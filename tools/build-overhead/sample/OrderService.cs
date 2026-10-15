using System;
using System.Collections.Generic;
using System.Linq;

namespace Sample.Unit__N__
{
    public class Order
    {
        public int Id { get; set; }
        public string Customer { get; set; }
        public decimal Total { get; set; }
        public OrderStatus Status { get; set; }
        public List<OrderLine> Lines { get; set; } = new List<OrderLine>();
    }

    public class OrderLine
    {
        public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }

    public enum OrderStatus { Pending, Paid, Shipped, Cancelled }

    public class OrderService
    {
        private readonly List<Order> _orders = new List<Order>();
        private readonly Dictionary<int, Order> _byId = new Dictionary<int, Order>();

        public void Add(Order order)
        {
            if (!_byId.ContainsKey(order.Id))
            {
                _byId[order.Id] = order;
                _orders.Add(order);
            }
        }

        public Order Find(int id)
        {
            if (_byId.ContainsKey(id))
                return _byId[id];
            return null;
        }

        public decimal TotalFor(string customer)
        {
            return _orders.Where(o => o.Customer == customer).Select(o => o.Total).Sum();
        }

        public int CountPaid()
        {
            return _orders.Where(o => o.Status == OrderStatus.Paid).Count();
        }

        public bool AnyLarge()
        {
            return _orders.Where(o => o.Total > 1000m).Any();
        }

        public List<string> Customers()
        {
            return _orders.Select(o => o.Customer).Distinct().ToList().OrderBy(c => c).ToList();
        }

        public string Describe(Order order)
        {
            string text = "";
            foreach (var line in order.Lines)
            {
                text += line.Sku + " x" + line.Quantity + "; ";
            }
            return "Order " + order.Id + " (" + order.Status.ToString() + "): " + text;
        }

        public IEnumerable<Order> Pending()
        {
            foreach (var o in _orders)
                if (o.Status == OrderStatus.Pending)
                    yield return o;
        }

        public int CountPendingTwice()
        {
            IEnumerable<Order> pending = Pending();
            int a = 0, b = 0;
            foreach (var o in pending) a++;
            foreach (var o in pending) b += o.Lines.Count;
            return a + b;
        }

        public Order[] Snapshot()
        {
            var list = new List<Order>();
            for (int i = 0; i < _orders.Count; i++)
                list.Add(_orders[i]);
            return list.ToArray();
        }
    }
}
