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
    [Authorize(Roles = "Cashier")]
    public class CashierController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly OrderNotificationService _orderNotificationService;

        public CashierController(ApplicationDbContext context, OrderNotificationService orderNotificationService)
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
        public async Task<IActionResult> MarkServed(int invoiceId)
        {
            var invoice = await _context.Invoices
                .Include(i => i.OrderItems)
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);

            if (invoice == null)
            {
                return NotFound();
            }

            foreach (var orderItem in invoice.OrderItems.Where(oi => oi.Status == OrderStatus.Ready))
            {
                orderItem.Status = OrderStatus.Served;
            }

            invoice.Status = "Served";

            await _context.SaveChangesAsync();

            var orderIds = invoice.OrderItems
                .Select(oi => oi.OrderId)
                .Distinct()
                .ToList();

            foreach (var orderId in orderIds)
            {
                await _orderNotificationService.RefreshAndBroadcastAsync(orderId);
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkPaid(int invoiceId)
        {
            var invoice = await _context.Invoices
                .Include(i => i.OrderItems)
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);

            if (invoice == null)
            {
                return NotFound();
            }

            foreach (var orderItem in invoice.OrderItems)
            {
                orderItem.Status = OrderStatus.Paid;
            }

            invoice.Status = "Paid";

            await _context.SaveChangesAsync();

            var orderIds = invoice.OrderItems
                .Select(oi => oi.OrderId)
                .Distinct()
                .ToList();

            foreach (var orderId in orderIds)
            {
                await _orderNotificationService.RefreshAndBroadcastAsync(orderId);
            }
            return RedirectToAction(nameof(Print), new { invoiceId });
        }

        [HttpGet]
        public async Task<IActionResult> Print(int invoiceId)
        {
            var invoice = await _context.Invoices
                .Include(i => i.OrderItems)
                    .ThenInclude(oi => oi.FoodItem)
                .Include(i => i.OrderItems)
                    .ThenInclude(oi => oi.Options)
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);

            if (invoice == null)
            {
                return NotFound();
            }

            var model = MapInvoice(invoice);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> DashboardData()
        {
            var model = await BuildDashboardAsync();

            var result = new
            {
                waiting = model.WaitingInvoices.Select(MapInvoiceDto).ToList(),
                ready = model.ReadyInvoices.Select(MapInvoiceDto).ToList(),
                completed = model.CompletedInvoices.Select(MapInvoiceDto).ToList()
            };

            return Json(result);
        }

        private async Task<CashierDashboardViewModel> BuildDashboardAsync()
        {
            var invoices = await _context.Invoices
                .Include(i => i.OrderItems)
                    .ThenInclude(oi => oi.FoodItem)
                .Include(i => i.OrderItems)
                    .ThenInclude(oi => oi.Options)
                .OrderByDescending(i => i.CreatedDate)
                .ToListAsync();

            var model = new CashierDashboardViewModel
            {
                WaitingInvoices = invoices
                    .Where(i => i.Status == "Pending" || i.Status == "InKitchen")
                    .Select(MapInvoice)
                    .ToList(),
                ReadyInvoices = invoices
                    .Where(i => i.Status == "Ready" || i.Status == "Served")
                    .Select(MapInvoice)
                    .ToList(),
                CompletedInvoices = invoices
                    .Where(i => i.Status == "Paid")
                    .Select(MapInvoice)
                    .ToList()
            };

            return model;
        }

        private static InvoiceSummaryViewModel MapInvoice(Invoice invoice)
        {
            var model = new InvoiceSummaryViewModel
            {
                InvoiceId = invoice.InvoiceId,
                InvoiceCode = invoice.InvoiceCode,
                Status = invoice.Status,
                CreatedDate = invoice.CreatedDate,
                FinalAmount = invoice.FinalAmount,
                IsPrepaid = invoice.IsPrepaid
            };

            model.Items = invoice.OrderItems
                .Select(orderItem =>
                {
                    var options = orderItem.Options
                        .Select(o => !string.IsNullOrWhiteSpace(o.OptionValueNameSnap)
                            ? o.OptionValueNameSnap!
                            : o.OptionGroupNameSnap ?? string.Empty)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToList();

                    return new OrderLineItemViewModel
                    {
                        OrderItemId = orderItem.OrderItemId,
                        ItemName = orderItem.FoodItem.Name,
                        Quantity = orderItem.Quantity,
                        Status = orderItem.Status,
                        Note = orderItem.Note,
                        Options = options
                    };
                })
                .ToList();

            return model;
        }

        private static object MapInvoiceDto(InvoiceSummaryViewModel invoice)
        {
            return new
            {
                id = invoice.InvoiceId,
                code = invoice.InvoiceCode,
                status = invoice.Status,
                createdAt = invoice.CreatedDate,
                finalAmount = invoice.FinalAmount,
                isPrepaid = invoice.IsPrepaid,
                items = invoice.Items.Select(MapLineItemDto).ToList()
            };
        }

        private static object MapLineItemDto(OrderLineItemViewModel item)
        {
            return new
            {
                id = item.OrderItemId,
                name = item.ItemName,
                quantity = item.Quantity,
                status = item.Status.ToString(),
                note = item.Note,
                options = item.Options
            };
        }
    }
}
