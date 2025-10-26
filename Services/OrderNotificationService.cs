using ASM_1.Data;
using ASM_1.Hubs;
using ASM_1.Models.Food;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ASM_1.Services
{
    public class OrderNotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<OrderStatusHub> _hubContext;
        private readonly TableCodeService _tableCodeService;

        public OrderNotificationService(
            ApplicationDbContext context,
            IHubContext<OrderStatusHub> hubContext,
            TableCodeService tableCodeService)
        {
            _context = context;
            _hubContext = hubContext;
            _tableCodeService = tableCodeService;
        }

        public async Task RefreshAndBroadcastAsync(int orderId)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.FoodItem)
                .Include(o => o.Items)
                    .ThenInclude(i => i.Options)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
            {
                return;
            }

            var updatedStatus = OrderStatusCalculator.Calculate(order.Items);
            var now = DateTime.UtcNow;

            if (order.Status != updatedStatus)
            {
                order.Status = updatedStatus;
            }

            order.UpdatedAt = now;
            await _context.SaveChangesAsync();

            await BroadcastAsync(order);
        }

        private async Task BroadcastAsync(Order order)
        {
            var tableCode = _tableCodeService.EncryptTableId(order.TableId);

            var summary = BuildSummary(order);
            var detail = BuildDetail(order, tableCode);

            await _hubContext.Clients
                .Group($"table:{tableCode}")
                .SendAsync("OrderUpdated", summary);

            await _hubContext.Clients
                .Group($"order:{order.OrderId}")
                .SendAsync("OrderDetailsUpdated", detail);
        }

        private static object BuildSummary(Order order)
        {
            return new
            {
                id = order.OrderId,
                code = order.OrderCode,
                placedAt = order.CreatedAt,
                table = order.TableNameSnapshot,
                sum = order.TotalAmount,
                status = order.Status.ToString()
            };
        }

        private static object BuildDetail(Order order, string tableCode)
        {
            return new
            {
                order = new
                {
                    id = order.OrderId,
                    code = order.OrderCode,
                    table = order.TableNameSnapshot,
                    tableCode,
                    placedAt = order.CreatedAt,
                    updatedAt = order.UpdatedAt,
                    status = order.Status.ToString(),
                    total = order.TotalAmount,
                    paymentMethod = order.PaymentMethod,
                    note = order.Note,
                    items = order.Items
                        .OrderBy(i => i.CreatedAt)
                        .Select(i => new
                        {
                            id = i.OrderItemId,
                            name = i.FoodItem?.Name ?? "Món",
                            quantity = i.Quantity,
                            status = i.Status.ToString(),
                            lineTotal = i.LineTotal,
                            note = i.Note,
                            options = i.Options
                                .Select(o => !string.IsNullOrWhiteSpace(o.OptionValueNameSnap)
                                    ? o.OptionValueNameSnap!
                                    : o.OptionGroupNameSnap ?? string.Empty)
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToList()
                        })
                        .ToList()
                }
            };
        }

    }
}
