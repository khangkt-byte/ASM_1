using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ASM_1.Data;
using ASM_1.Models.Food;
using ASM_1.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASM_1.Controllers
{
    public class CartController : BaseController
    {
        private readonly TableCodeService _tableCodeService;
        private readonly UserSessionService _userSessionService;
        private readonly ITableTrackerService _tableTracker;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public CartController(ApplicationDbContext context, TableCodeService tableCodeService, UserSessionService userSessionService, ITableTrackerService tableTracker)
            : base(context)
        {
            _tableCodeService = tableCodeService;
            _userSessionService = userSessionService;
            _tableTracker = tableTracker;
        }

        [HttpGet("{tableCode}/cart")]
        public async Task<IActionResult> Index(string tableCode)
        {
            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null) return RedirectToAction("InvalidTable");

            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            var cart = await GetCartAsync(userId);

            await PopulateDynamicPricingBannerAsync(tableId.Value);

            return View(cart.CartItems);
        }

        [HttpGet("{tableCode}/cart/count")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> CartCountValue(string tableCode)
        {
            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            if (userId == null)
            {
                return Content("0", "text/plain");
            }

            var count = await _context.CartItems
                .Where(ci => ci.Cart != null && ci.Cart.UserID == userId)
                .SumAsync(ci => (int?)ci.Quantity) ?? 0;

            return Content(count.ToString(), "text/plain");
        }

        [HttpGet("{tableCode}/cart/check")]
        public async Task<IActionResult> Checkout(string tableCode)
        {
            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null) return RedirectToAction("InvalidTable");

            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            var cart = await GetCartAsync(userId);

            if (!cart.CartItems.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn đang trống.";
                return RedirectToAction("Index", new { tableCode });
            }

            await PopulateDynamicPricingBannerAsync(tableId.Value);

            if (TempData.ContainsKey("DiscountError"))
            {
                ViewBag.DiscountError = TempData["DiscountError"];
            }

            if (TempData.ContainsKey("LastDiscountCode"))
            {
                ViewBag.LastDiscountCode = TempData.Peek("LastDiscountCode")?.ToString();
            }

            TempData.Keep("LastDiscountCode");

            return View(cart.CartItems);
        }

        [HttpGet("{tableCode}/cart/success")]
        public IActionResult Success(string tableCode)
        {
            if (TempData["OrderSuccess"] == null)
            {
                return RedirectToAction("Index", "Food", new { tableCode });
            }
            return View();
        }

        [HttpPost("{tableCode}/cart/place-order")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(string tableCode, string paymentMethod, string? discountCode)
        {
            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null) return RedirectToAction("InvalidTable");

            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            var cart = await _context.Carts
                .Include(c => c.CartItems)
                    .ThenInclude(i => i.Options)
                .FirstOrDefaultAsync(c => c.UserID == userId);

            if (cart == null || cart.CartItems == null || !cart.CartItems.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn đang trống.";
                return RedirectToAction(nameof(Index), new { tableCode });
            }

            var discountResult = await ValidateDiscountAsync(discountCode, cart.CartItems);
            if (!string.IsNullOrWhiteSpace(discountResult.ErrorMessage))
            {
                TempData["DiscountError"] = discountResult.ErrorMessage;
                if (!string.IsNullOrWhiteSpace(discountCode))
                {
                    TempData["LastDiscountCode"] = discountCode;
                }
                return RedirectToAction(nameof(Checkout), new { tableCode });
            }

            var table = await _context.Tables.FirstOrDefaultAsync(t => t.TableId == tableId);
            var linkedTableIds = new List<int>();
            TableMergeGroupSnapshot? mergeGroup = null;
            if (table != null)
            {
                linkedTableIds.Add(table.TableId);
                if (_tableTracker.TryGetMergeGroup(table.TableId, out var group))
                {
                    mergeGroup = group;
                    linkedTableIds = group.TableIds.ToList();
                }
            }

            if (linkedTableIds.Count > 0)
            {
                var hasPending = await _context.TableInvoices
                    .Include(ti => ti.Invoice)
                    .AnyAsync(ti => linkedTableIds.Contains(ti.TableId) && ti.Invoice.Status == "Pending");

                if (hasPending)
                {
                    TempData["ErrorMessage"] = "Bàn đang có hóa đơn chờ xử lý. Vui lòng hoàn tất trước khi tạo hóa đơn mới.";
                    return RedirectToAction(nameof(Checkout), new { tableCode });
                }
            }

            var subtotal = cart.CartItems.Sum(x => x.UnitPrice * x.Quantity);
            decimal shipping = 0m;
            var discountAmount = discountResult.DiscountAmount;
            var finalAmount = Math.Max(0m, subtotal - discountAmount + shipping);

            bool isPrepaid = paymentMethod is "momo" or "zalopay" or "vnpay";
            var nowLocal = DateTime.Now;

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var invoice = new Invoice
                {
                    InvoiceCode = NewInvoiceCode(),
                    CreatedDate = nowLocal,
                    TotalAmount = subtotal,
                    FinalAmount = finalAmount,
                    DiscountId = discountResult.Discount?.DiscountId,
                    Status = isPrepaid ? "Paid" : "Pending"
                };
                _context.Invoices.Add(invoice);
                await _context.SaveChangesAsync();

                foreach (var ci in cart.CartItems)
                {
                    var oi = new OrderItem
                    {
                        InvoiceId = invoice.InvoiceId,
                        FoodItemId = ci.ProductID,
                        Quantity = ci.Quantity,
                        UnitBasePrice = ci.BaseUnitPrice,
                        LineTotal = ci.UnitPrice * ci.Quantity,
                        Note = ci.Note,
                        CreatedAt = DateTime.UtcNow,
                        DynamicPriceFactor = ci.AppliedDynamicFactor
                    };
                    _context.OrderItems.Add(oi);
                    await _context.SaveChangesAsync();

                    if (ci.Options != null && ci.Options.Count > 0)
                    {
                        foreach (var opt in ci.Options)
                        {
                            var oio = new OrderItemOption
                            {
                                OrderItemId = oi.OrderItemId,
                                PriceDelta = opt.PriceDelta,
                                OptionGroupNameSnap = opt.OptionTypeName,
                                OptionValueNameSnap = opt.OptionName,
                                OptionValueCodeSnap = null,
                                Qty = opt.Quantity,
                                ScalePicked = opt.ScaleValue
                            };
                            _context.OrderItemOptions.Add(oio);
                        }
                    }

                    if (isPrepaid)
                    {
                        var detail = new InvoiceDetail
                        {
                            InvoiceId = invoice.InvoiceId,
                            FoodItemId = ci.ProductID,
                            Quantity = ci.Quantity,
                            UnitPrice = ci.UnitPrice,
                            SubTotal = ci.UnitPrice * ci.Quantity
                        };
                        _context.InvoiceDetails.Add(detail);
                    }
                }

                await _context.SaveChangesAsync();

                if (linkedTableIds.Count > 0)
                {
                    decimal ratio = linkedTableIds.Count > 0 ? Math.Round(1m / linkedTableIds.Count, 2) : 1m;
                    foreach (var id in linkedTableIds)
                    {
                        _context.TableInvoices.Add(new TableInvoice
                        {
                            TableId = id,
                            InvoiceId = invoice.InvoiceId,
                            SplitRatio = ratio,
                            MergeGroupId = mergeGroup?.GroupId
                        });
                    }
                    await _context.SaveChangesAsync();
                }

                _context.CartItems.RemoveRange(cart.CartItems);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData.Remove("LastDiscountCode");
                TempData.Remove("DiscountError");
                TempData["OrderSuccess"] = true;
                TempData["PaymentMethod"] = paymentMethod;
                if (table != null)
                {
                    TempData["TableName"] = table.TableName;
                }
                if (discountResult.Discount != null)
                {
                    TempData["AppliedDiscount"] = discountResult.Discount.Code;
                }

                return RedirectToAction(nameof(Success), new { tableCode });
            }
            catch
            {
                await tx.RollbackAsync();
                TempData["ErrorMessage"] = "Có lỗi khi đặt hàng. Vui lòng thử lại.";
                return RedirectToAction(nameof(Checkout), new { tableCode });
            }
        }

        [HttpPost("{tableCode}/cart/add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(
            string tableCode,
            int id,
            [FromForm] int[]? selectedOptionIds,
            [FromForm] string? selectionsJson,
            int quantity,
            string? note = null)
        {
            quantity = Math.Clamp(quantity, 1, 10);

            var foodItem = await _context.FoodItems
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.FoodItemId == id);

            if (foodItem == null)
                return NotFound();

            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            Table? table = null;
            decimal? dynamicFactor = null;
            if (tableId.HasValue)
            {
                table = await _context.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.TableId == tableId.Value);
                if (table != null && PricingHelper.TryGetDynamicFactor(table, DateTime.UtcNow, out var factor, out _))
                {
                    dynamicFactor = factor;
                }
            }

            var (resolvedOptions, optionsTotal) = await ResolveSelectedOptionsAsync(id, selectionsJson, selectedOptionIds);

            decimal basePrice = PricingHelper.CalculateEffectiveBasePrice(foodItem);
            decimal priceBeforeDynamic = basePrice + optionsTotal;
            decimal finalUnitPrice = dynamicFactor.HasValue && dynamicFactor.Value > 0 && dynamicFactor.Value != 1m
                ? PricingHelper.ApplyDynamicFactor(priceBeforeDynamic, dynamicFactor)
                : priceBeforeDynamic;

            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            var cart = await GetCartAsync(userId);

            var normalizedNote = (note ?? string.Empty).Trim();
            var optionSignature = BuildOptionSignature(resolvedOptions);
            var sameItem = cart.CartItems.FirstOrDefault(i =>
                i.ProductID == id &&
                Nullable.Equals(i.AppliedDynamicFactor, dynamicFactor) &&
                string.Equals((i.Note ?? string.Empty).Trim(), normalizedNote, StringComparison.OrdinalIgnoreCase) &&
                BuildOptionSignature(i.Options ?? new List<CartItemOption>()) == optionSignature);

            if (sameItem == null)
            {
                var newItem = new CartItem
                {
                    ProductID = foodItem.FoodItemId,
                    ProductName = foodItem.Name,
                    ProductImage = foodItem.ImageUrl ?? string.Empty,
                    Note = normalizedNote,
                    Quantity = quantity,
                    BaseUnitPrice = basePrice,
                    OptionsTotal = optionsTotal,
                    UnitPrice = finalUnitPrice,
                    TotalPrice = finalUnitPrice * quantity,
                    AppliedDynamicFactor = dynamicFactor,
                    Options = resolvedOptions.Select(opt => new CartItemOption
                    {
                        OptionTypeName = opt.OptionTypeName,
                        OptionName = opt.OptionName,
                        PriceDelta = opt.PriceDelta,
                        Quantity = opt.Quantity,
                        ScaleValue = opt.ScaleValue
                    }).ToList()
                };

                cart.CartItems.Add(newItem);
            }
            else
            {
                sameItem.Quantity += quantity;
                sameItem.TotalPrice = sameItem.UnitPrice * sameItem.Quantity;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Index", "Food", new { tableCode });
        }

        [HttpPost("{tableCode}/cart/item/{cartItemId}/remove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFromCart(int cartItemId, string tableCode)
        {
            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            var cart = await GetCartAsync(userId);
            var item = cart.CartItems.FirstOrDefault(i => i.CartItemID == cartItemId);
            if (item != null)
            {
                cart.CartItems.Remove(item);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index), new { tableCode });
        }

        [HttpPost("{tableCode}/cart/clear")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearCart(string tableCode)
        {
            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            var cart = await GetCartAsync(userId);

            if (cart.CartItems.Any())
            {
                _context.CartItems.RemoveRange(cart.CartItems);
                cart.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index), new { tableCode });
        }

        [HttpPost("{tableCode}/cart/item/{cartItemId}/qty/{delta}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeQuantity(string tableCode, int cartItemId, int delta)
        {
            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            var cart = await GetCartAsync(userId);
            var item = cart.CartItems.FirstOrDefault(i => i.CartItemID == cartItemId);
            if (item == null) return NotFound();

            item.Quantity = Math.Clamp(item.Quantity + delta, 1, 10);
            item.TotalPrice = item.UnitPrice * item.Quantity;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { tableCode });
        }

        private async Task PopulateDynamicPricingBannerAsync(int tableId)
        {
            var table = await _context.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.TableId == tableId);
            if (table == null) return;

            if (PricingHelper.TryGetDynamicFactor(table, DateTime.UtcNow, out var factor, out var label))
            {
                ViewBag.DynamicPricingLabel = label;
                ViewBag.DynamicPriceFactor = factor;
                ViewBag.TableName = table.TableName;
            }
        }

        private async Task<(List<CartItemOption> Options, decimal OptionsTotal)> ResolveSelectedOptionsAsync(int foodItemId, string? selectionsJson, int[]? legacyOptionIds)
        {
            if (!string.IsNullOrWhiteSpace(selectionsJson))
            {
                var selections = JsonSerializer.Deserialize<List<SelectionDto>>(selectionsJson, JsonOptions) ?? new List<SelectionDto>();
                var valueIds = selections.Select(s => s.OptionValueId).Where(id => id > 0).Distinct().ToList();

                if (valueIds.Count > 0)
                {
                    var optionValues = await _context.OptionValues
                        .Include(v => v.OptionGroup)
                        .Where(v => valueIds.Contains(v.OptionValueId))
                        .ToListAsync();

                    var overrides = await _context.MenuItemOptionValues
                        .Where(v => v.FoodItemId == foodItemId && valueIds.Contains(v.OptionValueId))
                        .ToDictionaryAsync(v => v.OptionValueId);

                    var lookup = optionValues.ToDictionary(v => v.OptionValueId);
                    var results = new List<CartItemOption>();
                    decimal total = 0m;

                    foreach (var selection in selections)
                    {
                        if (!lookup.TryGetValue(selection.OptionValueId, out var value)) continue;

                        var quantity = Math.Max(1, selection.Qty);
                        var priceDelta = overrides.TryGetValue(value.OptionValueId, out var ov) && ov.PriceDeltaOverride.HasValue
                            ? ov.PriceDeltaOverride.Value
                            : value.PriceDelta;

                        results.Add(new CartItemOption
                        {
                            OptionTypeName = value.OptionGroup.Name,
                            OptionName = value.Name,
                            PriceDelta = priceDelta,
                            Quantity = quantity,
                            ScaleValue = selection.ScalePicked
                        });

                        total += priceDelta * quantity;
                    }

                    return (results, total);
                }
            }

            if (legacyOptionIds != null && legacyOptionIds.Length > 0)
            {
                var foodOptions = await _context.FoodOptions
                    .Include(o => o.OptionType)
                    .AsNoTracking()
                    .Where(o => legacyOptionIds.Contains(o.FoodOptionId))
                    .ToListAsync();

                var results = foodOptions.Select(opt => new CartItemOption
                {
                    OptionTypeName = opt.OptionType?.TypeName ?? "Tùy chọn",
                    OptionName = opt.OptionName,
                    PriceDelta = opt.ExtraPrice,
                    Quantity = 1
                }).ToList();

                var total = results.Sum(o => o.PriceDelta);
                return (results, total);
            }

            return (new List<CartItemOption>(), 0m);
        }

        private static string BuildOptionSignature(IEnumerable<CartItemOption> options)
        {
            return string.Join("|", (options ?? Array.Empty<CartItemOption>())
                .OrderBy(o => o.OptionTypeName)
                .ThenBy(o => o.OptionName)
                .ThenBy(o => o.Quantity)
                .Select(o => $"{o.OptionTypeName}:{o.OptionName}:{o.Quantity}:{o.PriceDelta}:{o.ScaleValue}"));
        }

        private async Task<DiscountValidationResult> ValidateDiscountAsync(string? discountCode, List<CartItem> items)
        {
            if (string.IsNullOrWhiteSpace(discountCode))
            {
                return new DiscountValidationResult(null, 0m, null);
            }

            var normalized = discountCode.Trim();
            var discount = await _context.Discounts
                .Include(d => d.Combos!)
                    .ThenInclude(c => c.ComboDetails!)
                .FirstOrDefaultAsync(d => d.Code == normalized);

            if (discount == null)
            {
                return new DiscountValidationResult(null, 0m, "Mã giảm giá không hợp lệ.");
            }

            var now = DateTime.Now;
            if (!discount.IsActive || discount.StartDate > now || discount.EndDate < now)
            {
                return new DiscountValidationResult(discount, 0m, "Mã giảm giá đã hết hạn hoặc chưa kích hoạt.");
            }

            if (discount.Combos != null && discount.Combos.Count > 0)
            {
                var eligibleItemIds = discount.Combos
                    .SelectMany(c => c.ComboDetails ?? new List<ComboDetail>())
                    .Select(cd => cd.FoodItemId)
                    .ToHashSet();

                bool matches = items.Any(ci => eligibleItemIds.Contains(ci.ProductID));
                if (!matches)
                {
                    return new DiscountValidationResult(discount, 0m, "Mã giảm giá chỉ áp dụng cho các combo đủ điều kiện.");
                }
            }

            var subtotal = items.Sum(ci => ci.UnitPrice * ci.Quantity);
            if (subtotal <= 0)
            {
                return new DiscountValidationResult(discount, 0m, "Đơn hàng chưa đủ điều kiện để áp dụng mã giảm giá.");
            }

            var discountValue = subtotal * discount.Percent / 100m;
            if (discount.MaxAmount.HasValue)
            {
                discountValue = Math.Min(discountValue, discount.MaxAmount.Value);
            }

            discountValue = decimal.Round(discountValue, 0, MidpointRounding.AwayFromZero);

            if (discountValue <= 0)
            {
                return new DiscountValidationResult(discount, 0m, "Giá trị mã giảm giá không phù hợp với đơn hàng hiện tại.");
            }

            return new DiscountValidationResult(discount, discountValue, null);
        }

        private static string NewInvoiceCode()
        {
            var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var rnd = Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();
            return $"INV-{ts}-{rnd}";
        }

        private record SelectionDto
        {
            public int OptionValueId { get; set; }
            public int GroupId { get; set; }
            public int Qty { get; set; } = 1;
            public decimal? ScalePicked { get; set; }
            public string? Type { get; set; }
        }

        private record DiscountValidationResult(Discount? Discount, decimal DiscountAmount, string? ErrorMessage);
    }
}
