using ASM_1.Data;
using ASM_1.Models.Food;
using ASM_1.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Security.Claims;

namespace ASM_1.Controllers
{
    public class CartController : BaseController
    {
        private readonly TableCodeService _tableCodeService;
        private readonly UserSessionService _userSessionService;

        public CartController(ApplicationDbContext context, TableCodeService tableCodeService, UserSessionService userSessionService) : base(context)
        {
            _tableCodeService = tableCodeService;
            _userSessionService = userSessionService;
        }

        [HttpGet("{tableCode}/cart")]
        public async Task<IActionResult> Index(string tableCode)
        {
            //if (!User.Identity?.IsAuthenticated ?? true)
            //{
            //    TempData["ErrorMessage"] = "Bạn cần đăng nhập để xem giỏ hàng.";
            //    return RedirectToAction("Login", "Account");
            //}

            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null) return RedirectToAction("InvalidTable");

            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);

            var cart = await GetCartAsync(userId);
            return View(cart.CartItems);
        }

        [HttpGet("{tableCode}/cart/count")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> CartCountValue(string tableCode)
        {
            //if (!(User?.Identity?.IsAuthenticated ?? false))
            //    return Content("0", "text/plain");

            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            if (userId == null)
            {
                return Content("0", "text/plain"); // hoặc xử lý khác tùy bạn
            }

            var count = await _context.CartItems
                .Where(ci => ci.Cart != null && ci.Cart.UserID == userId)
                .SumAsync(ci => (int?)ci.Quantity) ?? 0;

            return Content(count.ToString(), "text/plain");
        }

        // THÊM MỚI: Action Checkout
        [HttpGet("{tableCode}/cart/check")]
        public async Task<IActionResult> Checkout(string tableCode)
        {
            //if (!User.Identity?.IsAuthenticated ?? true)
            //{
            //    TempData["ErrorMessage"] = "Bạn cần đăng nhập để thanh toán.";
            //    return RedirectToAction("Login", "Account");
            //}

            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null) return RedirectToAction("InvalidTable");

            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            var cart = await GetCartAsync(userId);

            if (!cart.CartItems.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn đang trống.";
                return RedirectToAction("Index", new { tableCode });
            }

            return View(cart.CartItems);
        }

        //THÊM MỚI: Thanh toán thành công
        [HttpGet("{tableCode}/cart/success")]
        public IActionResult Success(string tableCode)
        {
            if (TempData["OrderSuccess"] == null)
            {
                return RedirectToAction("Index", "Food", new { tableCode });
            }
            return View();
        }

        //    // THÊM MỚI: Xử lý đặt hàng
        //    [HttpPost]
        //    [ValidateAntiForgeryToken]
        //    public async Task<IActionResult> PlaceOrder(string fullName, string phone, string email,
        //string address, string city, string district, string ward, string note,
        //string deliveryTime, string paymentMethod)
        //    {
        //        // THÊM DEBUG
        //        Console.WriteLine("=== PlaceOrder method called ===");
        //        Console.WriteLine($"FullName: {fullName}");
        //        Console.WriteLine($"Phone: {phone}");
        //        Console.WriteLine($"DeliveryTime: {deliveryTime}");

        //        if (!User.Identity?.IsAuthenticated ?? true)
        //        {
        //            Console.WriteLine("User not authenticated");
        //            return RedirectToAction("Login", "Account");
        //        }

        //        string userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
        //        Console.WriteLine($"UserId: {userId}");

        //        var cart = await GetCartAsync(userId);

        //        if (!cart.CartItems.Any())
        //        {
        //            Console.WriteLine("Cart is empty");
        //            TempData["ErrorMessage"] = "Giỏ hàng của bạn đang trống.";
        //            return RedirectToAction("Index");
        //        }

        //        Console.WriteLine($"Cart has {cart.CartItems.Count} items");

        //        // Xóa giỏ hàng sau khi đặt thành công
        //        _context.CartItems.RemoveRange(cart.CartItems);
        //        await _context.SaveChangesAsync();

        //        Console.WriteLine("Cart cleared successfully");

        //        // Truyền thông tin qua TempData
        //        TempData["OrderSuccess"] = true;
        //        TempData["CustomerName"] = fullName;
        //        TempData["CustomerPhone"] = phone;
        //        TempData["CustomerAddress"] = address + ", " + ward + ", " + district + ", " + city;
        //        TempData["DeliveryType"] = deliveryTime == "now" ? "Tại chỗ" : "Giao hàng";
        //        TempData["PaymentMethod"] = paymentMethod;

        //        Console.WriteLine("TempData set, redirecting to Success");

        //        return RedirectToAction("Success");
        //    }

        [HttpPost("{tableCode}/cart/place-order")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(string tableCode, string? paymentMethod)
        {
            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy thông tin bàn từ mã QR.";
                return RedirectToAction(nameof(Index), new { tableCode });
            }

            // Ghi nhận ghi chú tổng hợp từ các món (nếu có)
            string? note = null;

            // 1) Lấy user & giỏ hàng
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

            var table = await _context.Tables
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TableId == tableId.Value);

            // 2) Tính tiền
            var subtotal = cart.CartItems.Sum(x => x.UnitPrice * x.Quantity);
            decimal shipping = 0m; // tuỳ mô hình giao/nhận
            var finalAmount = subtotal + shipping;

            var normalizedPayment = string.IsNullOrWhiteSpace(paymentMethod)
                ? "cod"
                : paymentMethod.Trim().ToLowerInvariant();

            // 3) Lưu dữ liệu: Order + OrderItem (+ snapshot option)
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Tổng hợp ghi chú của từng món để lưu vào đơn
                var itemNotes = cart.CartItems
                    .Where(ci => !string.IsNullOrWhiteSpace(ci.Note))
                    .Select(ci => $"{ci.ProductName}: {ci.Note}")
                    .ToList();
                if (itemNotes.Count > 0)
                {
                    note = string.Join(" | ", itemNotes);
                }

                var order = new Order
                {
                    OrderCode = NewOrderCode(),
                    TableId = tableId,
                    TableCode = tableCode,
                    TableName = table?.TableName,
                    CustomerSessionId = userId,
                    Status = OrderStatus.Pending,
                    PaymentMethod = normalizedPayment,
                    SubTotal = subtotal,
                    FinalAmount = finalAmount,
                    Note = note,
                    PlacedAt = DateTime.UtcNow
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                var orderItemPairs = new List<(OrderItem orderItem, CartItem cartItem)>();
                foreach (var ci in cart.CartItems)
                {
                    var oi = new OrderItem
                    {
                        OrderId = order.OrderId,
                        FoodItemId = ci.ProductID,
                        Quantity = ci.Quantity,
                        UnitBasePrice = ci.UnitPrice,
                        LineTotal = ci.UnitPrice * ci.Quantity,
                        Note = ci.Note,
                        CreatedAt = DateTime.UtcNow
                    };

                    orderItemPairs.Add((oi, ci));
                }

                if (orderItemPairs.Count > 0)
                {
                    _context.OrderItems.AddRange(orderItemPairs.Select(p => p.orderItem));
                    await _context.SaveChangesAsync();

                    var optionSnapshots = new List<OrderItemOption>();
                    foreach (var pair in orderItemPairs)
                    {
                        if (pair.cartItem.Options == null || pair.cartItem.Options.Count == 0)
                        {
                            continue;
                        }

                        foreach (var opt in pair.cartItem.Options)
                        {
                            optionSnapshots.Add(new OrderItemOption
                            {
                                OrderItemId = pair.orderItem.OrderItemId,
                                PriceDelta = 0m,
                                OptionGroupNameSnap = opt.OptionTypeName,
                                OptionValueNameSnap = opt.OptionName,
                                OptionValueCodeSnap = null,
                                OptionGroupId = null,
                                OptionValueId = null
                            });
                        }
                    }

                    if (optionSnapshots.Count > 0)
                    {
                        _context.OrderItemOptions.AddRange(optionSnapshots);
                        await _context.SaveChangesAsync();
                    }
                }

                _context.CartItems.RemoveRange(cart.CartItems);
                cart.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                TempData["OrderSuccess"] = true;
                TempData["PaymentMethod"] = normalizedPayment;
                TempData["TableName"] = table?.TableName ?? $"Bàn {tableId}";
                TempData["OrderCode"] = order.OrderCode;
                TempData["CustomerName"] = "Khách bàn";

                return RedirectToAction(nameof(Success), new { tableCode });
            }
            catch
            {
                await tx.RollbackAsync();
                TempData["ErrorMessage"] = "Có lỗi khi đặt hàng. Vui lòng thử lại.";
                return RedirectToAction(nameof(Index), new { tableCode });
            }
        }

        // ===== Helpers =====

        private static string NewOrderCode()
        {
            var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var rnd = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
            return $"ORD-{ts}-{rnd}";
        }

        [HttpPost("{tableCode}/cart/add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(
            string tableCode,
            int id,                               // FoodItemId
            [FromForm] int[]? selectedOptionIds,  // danh sách FoodOptionId mà user chọn (nhiều loại OptionType)
            int quantity,
            string? note = null)
        {
            // 1️⃣ Kiểm tra đăng nhập
            //if (!User.Identity?.IsAuthenticated ?? true)
            //{
            //    TempData["ErrorMessage"] = "Bạn cần đăng nhập để thêm sản phẩm vào giỏ hàng.";
            //    return RedirectToAction("Login", "Account");
            //}



            quantity = Math.Clamp(quantity, 1, 10);

            // 2️⃣ Lấy món ăn
            var foodItem = await _context.FoodItems
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.FoodItemId == id);

            if (foodItem == null)
                return NotFound();

            // 3️⃣ Lấy danh sách Option mà người dùng chọn
            var selectedOptions = (selectedOptionIds != null && selectedOptionIds.Length > 0)
                ? await _context.FoodOptions
                    .Include(o => o.OptionType)
                    .AsNoTracking()
                    .Where(o => selectedOptionIds.Contains(o.FoodOptionId))
                    .ToListAsync()
                : new List<FoodOption>();

            // 4️⃣ Tính giá tổng (base + phụ thu)
            decimal basePrice = foodItem.DiscountPrice > 0 ? foodItem.DiscountPrice : foodItem.BasePrice;
            decimal extraPrice = selectedOptions.Sum(o => o.ExtraPrice);
            decimal unitPrice = basePrice + extraPrice;

            // 5️⃣ Lấy user ID
            string userId = _userSessionService.GetOrCreateUserSessionId(tableCode);

            // 6️⃣ Lấy hoặc tạo giỏ hàng
            var cart = await GetCartAsync(userId);

            // 7️⃣ Kiểm tra xem đã có món trùng (cùng sản phẩm, cùng option, cùng ghi chú)
            var sameItem = cart.CartItems.FirstOrDefault(i =>
                i.ProductID == id &&
                i.Options.Select(o => o.OptionTypeName + ":" + o.OptionName)
                    .OrderBy(x => x)
                    .SequenceEqual(selectedOptions
                        .Select(o => o.OptionType.TypeName + ":" + o.OptionName)
                        .OrderBy(x => x)) &&
                string.Equals((i.Note ?? "").Trim(), (note ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            );

            // 8️⃣ Nếu chưa có thì thêm mới
            if (sameItem == null)
            {
                var newItem = new CartItem
                {
                    ProductID = foodItem.FoodItemId,
                    ProductName = foodItem.Name,
                    ProductImage = foodItem.ImageUrl ?? "",
                    Note = note?.Trim() ?? "",
                    UnitPrice = unitPrice,
                    Quantity = quantity,
                    TotalPrice = unitPrice * quantity,
                    Options = selectedOptions.Select(opt => new CartItemOption
                    {
                        OptionTypeName = opt.OptionType.TypeName,
                        OptionName = opt.OptionName
                    }).ToList()
                };

                cart.CartItems.Add(newItem);
            }
            else
            {
                // Nếu trùng thì chỉ cộng số lượng
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

    }
}

