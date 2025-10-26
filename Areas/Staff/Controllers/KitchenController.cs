using ASM_1.Data;
using ASM_1.Models.Food;
using ASM_1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace ASM_1.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Chef")]
    public class KitchenController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly OrderNotificationService _orderNotificationService;

        public KitchenController(ApplicationDbContext context, OrderNotificationService orderNotificationService)
        {
            _context = context;
            _orderNotificationService = orderNotificationService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = await BuildDashboardAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int id)
        {
            var orderItem = await _context.OrderItems
                .Include(o => o.Invoice)
                .FirstOrDefaultAsync(o => o.OrderItemId == id);

            if (orderItem == null)
            {
                return NotFound();
            }

            if (orderItem.Status == OrderStatus.Pending || orderItem.Status == OrderStatus.Confirmed)
            {
                orderItem.Status = OrderStatus.In_Kitchen;
                if (orderItem.Invoice != null)
                {
                    orderItem.Invoice.Status = "InKitchen";
                }

                await _context.SaveChangesAsync();
                await _orderNotificationService.RefreshAndBroadcastAsync(orderItem.OrderId);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkReady(int id)
        {
            var orderItem = await _context.OrderItems
                .Include(o => o.Invoice)
                    .ThenInclude(i => i.OrderItems)
                .FirstOrDefaultAsync(o => o.OrderItemId == id);

            if (orderItem == null)
            {
                return NotFound();
            }

            orderItem.Status = OrderStatus.Ready;

            if (orderItem.Invoice != null)
            {
                UpdateInvoiceStatus(orderItem.Invoice);
            }

            await _context.SaveChangesAsync();
            await _orderNotificationService.RefreshAndBroadcastAsync(orderItem.OrderId);
            return RedirectToAction(nameof(Index));
        }

        private async Task<KitchenDashboardViewModel> BuildDashboardAsync()
        {
            var orderItems = await _context.OrderItems
                .Include(o => o.FoodItem)
                .Include(o => o.Invoice)
                .Include(o => o.Options)
                .Where(o => o.Status == OrderStatus.Pending
                            || o.Status == OrderStatus.In_Kitchen
                            || o.Status == OrderStatus.Ready)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();

            var model = new KitchenDashboardViewModel
            {
                PendingOrders = orderItems
                    .Where(o => o.Status == OrderStatus.Pending)
                    .Select(MapKitchenItem)
                    .ToList(),
                InProgressOrders = orderItems
                    .Where(o => o.Status == OrderStatus.In_Kitchen)
                    .Select(MapKitchenItem)
                    .ToList(),
                ReadyOrders = orderItems
                    .Where(o => o.Status == OrderStatus.Ready)
                    .Select(MapKitchenItem)
                    .ToList()
            };

            return model;
        }

        private static KitchenOrderItemViewModel MapKitchenItem(OrderItem item)
        {
            var options = item.Options
                .Select(o => !string.IsNullOrWhiteSpace(o.OptionValueNameSnap)
                    ? o.OptionValueNameSnap!
                    : o.OptionGroupNameSnap ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            return new KitchenOrderItemViewModel
            {
                OrderItemId = item.OrderItemId,
                InvoiceCode = item.Invoice?.InvoiceCode ?? "#",
                FoodName = item.FoodItem.Name,
                Quantity = item.Quantity,
                Status = item.Status,
                CreatedAt = item.CreatedAt,
                Note = item.Note,
                Options = options
            };
        }

        private static void UpdateInvoiceStatus(Invoice invoice)
        {
            if (invoice.OrderItems.All(oi => oi.Status == OrderStatus.Ready
                                          || oi.Status == OrderStatus.Served
                                          || oi.Status == OrderStatus.Requested_Bill
                                          || oi.Status == OrderStatus.Paid))
            {
                invoice.Status = "Ready";
            }
            else if (invoice.OrderItems.Any(oi => oi.Status == OrderStatus.In_Kitchen))
            {
                invoice.Status = "InKitchen";
            }
            else
            {
                invoice.Status = "Pending";
            }
        }
    }
}
