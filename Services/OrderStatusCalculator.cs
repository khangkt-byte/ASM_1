using ASM_1.Models.Food;

namespace ASM_1.Services
{
    public static class OrderStatusCalculator
    {
        public static OrderStatus Calculate(IEnumerable<OrderItem> items)
        {
            var statuses = items.Select(i => i.Status).ToList();
            if (statuses.Count == 0)
            {
                return OrderStatus.Pending;
            }

            if (statuses.All(s => s == OrderStatus.Canceled))
            {
                return OrderStatus.Canceled;
            }

            if (statuses.All(s => s == OrderStatus.Paid))
            {
                return OrderStatus.Paid;
            }

            if (statuses.All(s => s is OrderStatus.Paid or OrderStatus.Served))
            {
                return OrderStatus.Served;
            }

            if (statuses.Any(s => s == OrderStatus.Requested_Bill))
            {
                return OrderStatus.Requested_Bill;
            }

            if (statuses.All(s => s is OrderStatus.Ready or OrderStatus.Served or OrderStatus.Paid or OrderStatus.Requested_Bill))
            {
                return OrderStatus.Ready;
            }

            if (statuses.Any(s => s == OrderStatus.In_Kitchen))
            {
                return OrderStatus.In_Kitchen;
            }

            if (statuses.Any(s => s == OrderStatus.Confirmed))
            {
                return OrderStatus.Confirmed;
            }

            return OrderStatus.Pending;
        }
    }
}
