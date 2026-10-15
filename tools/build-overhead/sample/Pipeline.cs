using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Unit__N__
{
    public interface IStep
    {
        Task<Order> RunAsync(Order order, CancellationToken ct);
    }

    public class ValidateStep : IStep
    {
        public Task<Order> RunAsync(Order order, CancellationToken ct)
        {
            if (order.Lines.Count == 0)
                throw new InvalidOperationException("Order " + order.Id + " has no lines");
            return Task.FromResult(order);
        }
    }

    public class PriceStep : IStep
    {
        public async Task<Order> RunAsync(Order order, CancellationToken ct)
        {
            await Task.Yield();
            order.Total = order.Lines.Sum(l => l.Price * l.Quantity);
            return order;
        }
    }

    public class Pipeline
    {
        private readonly List<IStep> _steps = new List<IStep>();
        private readonly List<Func<Order, bool>> _filters = new List<Func<Order, bool>>();

        public Pipeline Add(IStep step)
        {
            _steps.Add(step);
            return this;
        }

        public void Filter(decimal minimum)
        {
            _filters.Add(o => o.Total >= minimum);
        }

        public async Task<Order> RunAsync(Order order, CancellationToken ct)
        {
            foreach (var step in _steps)
            {
                order = await step.RunAsync(order, ct);
            }
            foreach (var f in _filters)
            {
                if (!f(order)) return null;
            }
            return order;
        }

        public Task<List<Order>> RunAllAsync(IEnumerable<Order> orders, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                var done = new List<Order>();
                foreach (var o in orders)
                {
                    var r = await RunAsync(o, ct);
                    if (r != null) done.Add(r);
                }
                return done;
            });
        }

        public void Fire(Order order)
        {
            Task.Run(() => RunAsync(order, CancellationToken.None)).Wait();
        }
    }
}
