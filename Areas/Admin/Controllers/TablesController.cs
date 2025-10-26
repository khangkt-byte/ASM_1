using ASM_1.Data;
using ASM_1.Models.Food;
using ASM_1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace ASM_1.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize]
    public class TablesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ITableTrackerService _tableTracker;
        private readonly IWebHostEnvironment _env;
        private readonly TableCodeService _tableCodeService;

        public TablesController(ApplicationDbContext context, ITableTrackerService tableTracker, IWebHostEnvironment env, TableCodeService tableCodeService)
        {
            _context = context;
            _tableTracker = tableTracker;
            _env = env;
            _tableCodeService = tableCodeService;
        }

        public async Task<IActionResult> Index()
        {
            var tables = await _context.Tables.ToListAsync();
            var mergeGroups = _tableTracker.GetMergeGroups();
            var mergeLookup = mergeGroups
                .SelectMany(g => g.TableIds.Select(id => new { id, g }))
                .ToDictionary(x => x.id, x => x.g);

            foreach (var t in tables)
            {
                int guestCount = _tableTracker.GetGuestCount(t.TableId);
                var status = guestCount < t.SeatCount ? "Available" : "Full";
                if (mergeLookup.TryGetValue(t.TableId, out var group))
                {
                    status = $"Merged #{group.GroupId}";
                }
                t.Status = status;
            }

            var viewModel = new TableManagementViewModel
            {
                Tables = tables,
                ActiveMerges = mergeGroups.Select(g => new TableMergeGroupViewModel
                {
                    GroupId = g.GroupId,
                    Label = g.Label,
                    CreatedAt = g.CreatedAt,
                    DisplayName = $"Nhóm #{g.GroupId}",
                    Tables = tables.Where(t => g.TableIds.Contains(t.TableId)).OrderBy(t => t.TableName).ToList()
                }).ToList()
            };

            return View(viewModel);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var table = await _context.Tables.FirstOrDefaultAsync(m => m.TableId == id);
            if (table == null)
            {
                return NotFound();
            }

            int guestCount = _tableTracker.GetGuestCount(table.TableId);
            table.Status = guestCount < table.SeatCount ? "Available" : "Full";

            if (_tableTracker.TryGetMergeGroup(table.TableId, out var group))
            {
                ViewBag.MergeGroup = group;
            }

            string filePath = Path.Combine(_env.WebRootPath, "uploads", "qr", $"table_{id}.png");
            bool exists = System.IO.File.Exists(filePath);

            ViewBag.QrExists = exists;
            ViewBag.QrPath = exists ? $"/uploads/qr/table_{id}.png" : null;
            return View(table);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Merge(int[] selectedTableIds, string? mergeLabel)
        {
            if (selectedTableIds == null || selectedTableIds.Length < 2)
            {
                TempData["Error"] = "Vui lòng chọn ít nhất hai bàn để gộp.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var snapshot = _tableTracker.MergeTables(selectedTableIds, mergeLabel);
                TempData["Success"] = $"Đã gộp {snapshot.TableIds.Count} bàn vào nhóm #{snapshot.GroupId}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SplitGroup(int groupId)
        {
            if (_tableTracker.SplitGroup(groupId))
            {
                TempData["Success"] = $"Đã tách nhóm bàn #{groupId}.";
            }
            else
            {
                TempData["Error"] = "Không tìm thấy nhóm bàn để tách.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SplitTable(int tableId)
        {
            if (_tableTracker.SplitTable(tableId))
            {
                TempData["Success"] = "Đã đưa bàn trở lại trạng thái riêng lẻ.";
            }
            else
            {
                TempData["Error"] = "Bàn không nằm trong nhóm gộp nào.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> GenerateAll()
        {
            var tables = await _context.Tables.ToListAsync();
            string qrDir = Path.Combine(_env.WebRootPath, "uploads", "qr");
            Directory.CreateDirectory(qrDir);

            foreach (var table in tables)
            {
                string code = _tableCodeService.EncryptTableId(table.TableId);
                string baseurl = $"{Request.Scheme}://{Request.Host.Value}";
                string url = $"{baseurl}/{code}";
                string filePath = Path.Combine(qrDir, $"table_{table.TableId}.png");

                if (!System.IO.File.Exists(filePath))
                {
                    var qrGenerator = new QRCodeGenerator();
                    var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                    var pngQr = new PngByteQRCode(qrCodeData);
                    byte[] qrBytes = pngQr.GetGraphic(20);
                    await System.IO.File.WriteAllBytesAsync(filePath, qrBytes);
                }
            }

            TempData["Success"] = "Đã tạo QR cho tất cả bàn!";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult DownloadQR(int id)
        {
            string qrDir = Path.Combine(_env.WebRootPath, "uploads", "qr");
            string filePath = Path.Combine(qrDir, $"table_{id}.png");

            if (!System.IO.File.Exists(filePath))
            {
                TempData["Error"] = $"Không tìm thấy mã QR cho bàn {id}.";
                return RedirectToAction(nameof(Details), new { id });
            }

            string fileName = $"table_{id}_QR.png";
            var mimeType = "image/png";

            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, mimeType, fileName);
        }

        [HttpPost]
        public async Task<IActionResult> Refresh(int id)
        {
            var table = await _context.Tables.FindAsync(id);
            if (table == null) return NotFound();

            string qrDir = Path.Combine(_env.WebRootPath, "uploads", "qr");
            Directory.CreateDirectory(qrDir);
            string filePath = Path.Combine(qrDir, $"table_{id}.png");

            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            string code = _tableCodeService.EncryptTableId(id);
            string baseurl = $"{Request.Scheme}://{Request.Host.Value}";
            string url = $"{baseurl}/{code}";

            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var pngQr = new PngByteQRCode(qrCodeData);
            byte[] qrBytes = pngQr.GetGraphic(20);
            await System.IO.File.WriteAllBytesAsync(filePath, qrBytes);

            TempData["Success"] = $"Đã làm mới QR cho bàn {table.TableName}";
            return RedirectToAction(nameof(Details), new { id });
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("TableId,TableName,SeatCount")] Table table)
        {
            if (ModelState.IsValid)
            {
                table.Status = "Available";
                _context.Add(table);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(table);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var table = await _context.Tables.FindAsync(id);
            if (table == null)
            {
                return NotFound();
            }
            return View(table);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("TableId,TableName,SeatCount")] Table table)
        {
            if (id != table.TableId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    int guestCount = _tableTracker.GetGuestCount(table.TableId);
                    table.Status = guestCount < table.SeatCount ? "Available" : "Full";

                    _context.Update(table);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TableExists(table.TableId))
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
            return View(table);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var table = await _context.Tables
                .FirstOrDefaultAsync(m => m.TableId == id);
            if (table == null)
            {
                return NotFound();
            }

            return View(table);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var table = await _context.Tables.FindAsync(id);
            if (table != null)
            {
                _context.Tables.Remove(table);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool TableExists(int id)
        {
            return _context.Tables.Any(e => e.TableId == id);
        }
    }
}
