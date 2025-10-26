using ASM_1.Data;
using ASM_1.Models.Food;
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

        public CashierController(ApplicationDbContext context)
        {
            _context = context;
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
    }
}
