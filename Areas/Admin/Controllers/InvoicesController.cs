using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ASM_1.Data;
using ASM_1.Models.Food;

namespace ASM_1.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class InvoicesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public InvoicesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var invoices = await _context.Invoices
                .Include(i => i.Discount)
                .Include(i => i.TableInvoices)
                    .ThenInclude(ti => ti.Table)
                .OrderByDescending(i => i.CreatedDate)
                .ToListAsync();

            return View(invoices);
        }

        public async Task<IActionResult> Statistics(DateTime? from, DateTime? to)
        {
            var endDate = (to?.Date ?? DateTime.Today).AddDays(1);
            var startDate = from?.Date ?? endDate.AddDays(-30);

            var invoicesQuery = _context.Invoices
                .Include(i => i.TableInvoices)
                .Where(i => i.CreatedDate >= startDate && i.CreatedDate < endDate)
                .Where(i => i.Status != "Cancelled");

            var invoices = await invoicesQuery.ToListAsync();

            var daily = invoices
                .GroupBy(i => i.CreatedDate.Date)
                .OrderBy(g => g.Key)
                .Select(g => new RevenuePoint
                {
                    Label = g.Key.ToString("dd/MM"),
                    StartDate = g.Key,
                    Amount = g.Sum(x => x.FinalAmount),
                    InvoiceCount = g.Count()
                })
                .ToList();

            var weekly = invoices
                .GroupBy(i => GetWeekStart(i.CreatedDate))
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var start = g.Key;
                    var end = start.AddDays(6);
                    return new RevenuePoint
                    {
                        Label = $"{start:dd/MM} - {end:dd/MM}",
                        StartDate = start,
                        EndDate = end,
                        Amount = g.Sum(x => x.FinalAmount),
                        InvoiceCount = g.Count()
                    };
                })
                .ToList();

            var vm = new RevenueStatisticsViewModel
            {
                FromDate = startDate,
                ToDate = endDate.AddDays(-1),
                DailyRevenue = daily,
                WeeklyRevenue = weekly,
                TotalRevenue = invoices.Sum(i => i.FinalAmount),
                TotalInvoices = invoices.Count,
                AverageDailyRevenue = daily.Any() ? daily.Average(d => d.Amount) : 0m,
                AverageWeeklyRevenue = weekly.Any() ? weekly.Average(w => w.Amount) : 0m
            };

            return View(vm);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var invoice = await _context.Invoices
                .Include(i => i.Discount)
                .Include(i => i.TableInvoices)
                    .ThenInclude(ti => ti.Table)
                .Include(i => i.InvoiceDetails)
                    .ThenInclude(d => d.FoodItem)
                .Include(i => i.InvoiceDetails)
                    .ThenInclude(d => d.InvoiceDetailFoodOptions).ThenInclude(fo => fo.FoodOption)
                .FirstOrDefaultAsync(m => m.InvoiceId == id);

            if (invoice == null)
            {
                return NotFound();
            }

            return View(invoice);
        }

        public IActionResult Create()
        {
            ViewData["DiscountId"] = new SelectList(_context.Discounts, "DiscountId", "Code");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("InvoiceId,InvoiceCode,CreatedDate,TotalAmount,FinalAmount,DiscountId,Status,Notes")] Invoice invoice)
        {
            if (ModelState.IsValid)
            {
                _context.Add(invoice);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["DiscountId"] = new SelectList(_context.Discounts, "DiscountId", "Code", invoice.DiscountId);
            return View(invoice);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
            {
                return NotFound();
            }
            ViewData["DiscountId"] = new SelectList(_context.Discounts, "DiscountId", "Code", invoice.DiscountId);
            return View(invoice);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("InvoiceId,InvoiceCode,CreatedDate,TotalAmount,FinalAmount,DiscountId,Status,Notes")] Invoice invoice)
        {
            if (id != invoice.InvoiceId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(invoice);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!InvoiceExists(invoice.InvoiceId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["DiscountId"] = new SelectList(_context.Discounts, "DiscountId", "Code", invoice.DiscountId);
            return View(invoice);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var invoice = await _context.Invoices
                .Include(i => i.Discount)
                .FirstOrDefaultAsync(m => m.InvoiceId == id);
            if (invoice == null)
            {
                return NotFound();
            }

            return View(invoice);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice != null)
            {
                _context.Invoices.Remove(invoice);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool InvoiceExists(int id)
        {
            return _context.Invoices.Any(e => e.InvoiceId == id);
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var day = date.Date;
            int diff = (7 + (int)day.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return day.AddDays(-diff);
        }
    }
}
