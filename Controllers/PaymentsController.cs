using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ASM_1.Data;
using ASM_1.Models.Food;
using ASM_1.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASM_1.Controllers
{
    [Route("orders/{orderId:int}/payment")]
    public class PaymentsController : Controller
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly IReadOnlyList<PaymentMethodOptionViewModel> SupportedMethods = new List<PaymentMethodOptionViewModel>
        {
            new() { Key = "card", Label = "Thẻ (POS)" },
            new() { Key = "momo", Label = "Ví MoMo" },
            new() { Key = "cash", Label = "Tiền mặt tại quầy" }
        };

        private readonly ApplicationDbContext _context;
        private readonly TableCodeService _tableCodeService;
        private readonly UserSessionService _userSessionService;

        public PaymentsController(
            ApplicationDbContext context,
            TableCodeService tableCodeService,
            UserSessionService userSessionService)
        {
            _context = context;
            _tableCodeService = tableCodeService;
            _userSessionService = userSessionService;
        }

        [HttpGet]
        public async Task<IActionResult> Get(int orderId, string tableCode)
        {
            var loadResult = await LoadOrderAsync(orderId, tableCode, track: false);
            if (loadResult == null)
            {
                return NotFound();
            }

            var (order, _, userSessionId) = loadResult.Value;
            return Json(BuildPaymentInfo(order, userSessionId));
        }

        public sealed class PaymentShareRequest
        {
            public string? SplitMode { get; set; }
            public string? PaymentMethod { get; set; }
            public int? ParticipantCount { get; set; }
            public decimal? Percentage { get; set; }
            public List<int>? ItemIds { get; set; }
            public string? DisplayName { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateShare(int orderId, string tableCode, [FromBody] PaymentShareRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });
            }

            if (!TryNormalizeSplitMode(request.SplitMode, out var splitMode))
            {
                return BadRequest(new { message = "Kiểu chia hóa đơn không hợp lệ." });
            }

            var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
            if (!SupportedMethods.Any(m => m.Key == paymentMethod))
            {
                return BadRequest(new { message = "Phương thức thanh toán không được hỗ trợ." });
            }

            var loadResult = await LoadOrderAsync(orderId, tableCode, track: true);
            if (loadResult == null)
            {
                return NotFound();
            }

            var (order, _, userSessionId) = loadResult.Value;

            if (order.Status == OrderStatus.Paid)
            {
                return BadRequest(new { message = "Đơn hàng đã được thanh toán." });
            }

            var session = order.PaymentSession;
            if (session == null)
            {
                session = new PaymentSession
                {
                    OrderId = order.OrderId,
                    SplitMode = splitMode,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBySessionId = userSessionId
                };

                if (splitMode == PaymentSplitMode.SplitEvenly)
                {
                    if (request.ParticipantCount is null or < 1)
                    {
                        return BadRequest(new { message = "Vui lòng nhập số người chia đều." });
                    }

                    session.ParticipantCount = request.ParticipantCount;
                }

                order.PaymentSession = session;
                _context.PaymentSessions.Add(session);
            }
            else if (session.SplitMode != splitMode)
            {
                return BadRequest(new { message = "Đơn hàng đã chọn kiểu chia khác. Vui lòng làm mới trang." });
            }
            else if (splitMode == PaymentSplitMode.SplitEvenly && request.ParticipantCount.HasValue)
            {
                if (request.ParticipantCount < 1)
                {
                    return BadRequest(new { message = "Số người chia đều không hợp lệ." });
                }

                session.ParticipantCount = request.ParticipantCount;
            }

            var now = DateTime.UtcNow;
            var existingShares = session.Shares.ToList();
            var currentShare = existingShares.FirstOrDefault(s => s.UserSessionId == userSessionId);
            var otherShares = existingShares.Where(s => s.UserSessionId != userSessionId).ToList();

            var totalAmount = order.TotalAmount;
            decimal alreadyPaid = otherShares.Sum(s => s.Amount);
            decimal newAmount;
            decimal? newPercentage = null;
            string? metadata = null;

            switch (splitMode)
            {
                case PaymentSplitMode.Full:
                    newAmount = Math.Max(totalAmount - alreadyPaid, 0);
                    break;
                case PaymentSplitMode.SplitEvenly:
                    if (session.ParticipantCount is null or < 1)
                    {
                        return BadRequest(new { message = "Chưa cấu hình số người chia đều." });
                    }

                    var shareAmount = Math.Round(totalAmount / session.ParticipantCount.Value, 2, MidpointRounding.AwayFromZero);
                    newAmount = shareAmount;
                    if (alreadyPaid + newAmount > totalAmount)
                    {
                        newAmount = Math.Max(totalAmount - alreadyPaid, 0);
                    }
                    break;
                case PaymentSplitMode.SplitByPercentage:
                    if (!request.Percentage.HasValue || request.Percentage.Value <= 0)
                    {
                        return BadRequest(new { message = "Vui lòng nhập phần trăm cần thanh toán." });
                    }

                    newPercentage = Math.Clamp(request.Percentage.Value, 0, 100);
                    var percentageSum = otherShares.Sum(s => s.Percentage ?? 0);
                    if (percentageSum + newPercentage > 100 + 0.001m)
                    {
                        return BadRequest(new { message = "Tổng phần trăm vượt quá 100%." });
                    }

                    newAmount = Math.Round(totalAmount * newPercentage.Value / 100m, 2, MidpointRounding.AwayFromZero);
                    if (alreadyPaid + newAmount > totalAmount)
                    {
                        newAmount = Math.Max(totalAmount - alreadyPaid, 0);
                    }
                    break;
                case PaymentSplitMode.PayOwnItems:
                    if (request.ItemIds == null || request.ItemIds.Count == 0)
                    {
                        return BadRequest(new { message = "Vui lòng chọn món cần thanh toán." });
                    }

                    var validItemIds = order.Items.Select(i => i.OrderItemId).ToHashSet();
                    var requestedIds = request.ItemIds.Where(validItemIds.Contains).Distinct().ToList();
                    if (requestedIds.Count == 0)
                    {
                        return BadRequest(new { message = "Danh sách món không hợp lệ." });
                    }

                    var usedItemIds = new HashSet<int>(otherShares
                        .SelectMany(s => ParseItemIds(s.Metadata)));
                    if (requestedIds.Any(id => usedItemIds.Contains(id)))
                    {
                        return BadRequest(new { message = "Một số món đã được người khác chọn thanh toán." });
                    }

                    newAmount = order.Items
                        .Where(i => requestedIds.Contains(i.OrderItemId))
                        .Sum(i => i.LineTotal);
                    metadata = SerializeItemMetadata(requestedIds);
                    break;
                default:
                    return BadRequest(new { message = "Kiểu chia hóa đơn không hợp lệ." });
            }

            if (newAmount <= 0)
            {
                return BadRequest(new { message = "Số tiền thanh toán không hợp lệ." });
            }

            if (currentShare == null)
            {
                currentShare = new PaymentShare
                {
                    PaymentSession = session,
                    UserSessionId = userSessionId,
                    DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                    PaymentMethod = paymentMethod,
                    Amount = newAmount,
                    Percentage = newPercentage,
                    Metadata = metadata,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                session.Shares.Add(currentShare);
                _context.PaymentShares.Add(currentShare);
            }
            else
            {
                currentShare.PaymentMethod = paymentMethod;
                currentShare.Amount = newAmount;
                currentShare.Percentage = newPercentage;
                currentShare.Metadata = metadata;
                currentShare.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
                currentShare.UpdatedAt = now;
            }

            session.UpdatedAt = now;

            var totalPaid = session.Shares.Sum(s => s.Amount);
            var outstanding = Math.Max(totalAmount - totalPaid, 0);

            if (outstanding <= 0)
            {
                session.IsFinalized = true;
                order.Status = OrderStatus.Paid;
                order.PaymentMethod = CombinePaymentMethods(session.Shares);
                order.UpdatedAt = DateTime.UtcNow;
                if (order.Invoice != null)
                {
                    order.Invoice.Status = "Paid";
                    order.Invoice.FinalAmount = order.TotalAmount;
                    order.Invoice.IsPrepaid = true;
                }
            }
            else
            {
                session.IsFinalized = false;
                order.PaymentMethod = CombinePaymentMethods(session.Shares);
                order.UpdatedAt = DateTime.UtcNow;
                if (order.Invoice != null && string.Equals(order.Invoice.Status, "Paid", StringComparison.OrdinalIgnoreCase))
                {
                    order.Invoice.Status = "Partial";
                }
            }

            await _context.SaveChangesAsync();

            await _context.Entry(order).Reference(o => o.PaymentSession).LoadAsync();
            if (order.PaymentSession != null)
            {
                await _context.Entry(order.PaymentSession).Collection(ps => ps.Shares).LoadAsync();
            }
            if (order.Invoice != null)
            {
                await _context.Entry(order).Reference(o => o.Invoice).LoadAsync();
            }

            return Json(BuildPaymentInfo(order, userSessionId));
        }

        private async Task<(Order Order, int TableId, string UserSessionId)?> LoadOrderAsync(int orderId, string tableCode, bool track)
        {
            if (string.IsNullOrWhiteSpace(tableCode))
            {
                return null;
            }

            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null)
            {
                return null;
            }

            var userSessionId = _userSessionService.GetOrCreateUserSessionId(tableCode);

            IQueryable<Order> query = _context.Orders
                .Where(o => o.OrderId == orderId && o.TableId == tableId)
                .Include(o => o.Items)
                    .ThenInclude(i => i.FoodItem)
                .Include(o => o.PaymentSession)!.ThenInclude(ps => ps.Shares)
                .Include(o => o.Invoice);

            if (!track)
            {
                query = query.AsNoTracking();
            }

            var order = await query.FirstOrDefaultAsync();
            if (order == null)
            {
                return null;
            }

            return (order, tableId.Value, userSessionId);
        }

        private PaymentInfoViewModel BuildPaymentInfo(Order order, string currentSessionId)
        {
            var session = order.PaymentSession;
            var shares = session?.Shares ?? Array.Empty<PaymentShare>();
            var shareViewModels = shares
                .OrderBy(s => s.CreatedAt)
                .Select(s => new PaymentShareViewModel
                {
                    PaymentShareId = s.PaymentShareId,
                    UserSessionId = s.UserSessionId,
                    DisplayName = s.DisplayName,
                    PaymentMethod = s.PaymentMethod,
                    Amount = s.Amount,
                    Percentage = s.Percentage,
                    ItemIds = ParseItemIds(s.Metadata),
                    IsCurrentUser = string.Equals(s.UserSessionId, currentSessionId, StringComparison.Ordinal),
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt
                })
                .ToList();

            var totalPaid = shareViewModels.Sum(s => s.Amount);
            var outstanding = Math.Max(order.TotalAmount - totalPaid, 0);
            var percent = shareViewModels.Sum(s => s.Percentage ?? 0);

            var sessionSummary = new PaymentSessionSummaryViewModel
            {
                PaymentSessionId = session?.PaymentSessionId,
                SplitMode = session?.SplitMode.ToString(),
                SplitModeKey = session != null ? GetSplitModeKey(session.SplitMode) : null,
                IsFinalized = session?.IsFinalized ?? false,
                ParticipantCount = session?.ParticipantCount,
                TotalAmount = order.TotalAmount,
                PaidAmount = totalPaid,
                OutstandingAmount = outstanding,
                TotalPercentage = percent,
                Shares = shareViewModels
            };

            return new PaymentInfoViewModel
            {
                Session = sessionSummary,
                Items = order.Items
                    .OrderBy(i => i.CreatedAt)
                    .Select(i => new PaymentOrderItemViewModel
                    {
                        OrderItemId = i.OrderItemId,
                        Name = i.FoodItem?.Name ?? $"Món #{i.OrderItemId}",
                        LineTotal = i.LineTotal,
                        Quantity = i.Quantity
                    })
                    .ToList(),
                Invoice = order.Invoice == null
                    ? null
                    : new PaymentInvoiceViewModel
                    {
                        InvoiceId = order.Invoice.InvoiceId,
                        InvoiceCode = order.Invoice.InvoiceCode,
                        Status = order.Invoice.Status,
                        FinalAmount = order.Invoice.FinalAmount
                    },
                AvailableMethods = SupportedMethods
            };
        }

        private static bool TryNormalizeSplitMode(string? value, out PaymentSplitMode mode)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                mode = PaymentSplitMode.Full;
                return true;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "full" or "all" => ReturnMode(PaymentSplitMode.Full, out mode),
                "even" or "split" => ReturnMode(PaymentSplitMode.SplitEvenly, out mode),
                "percentage" or "percent" => ReturnMode(PaymentSplitMode.SplitByPercentage, out mode),
                "items" or "mine" => ReturnMode(PaymentSplitMode.PayOwnItems, out mode),
                _ => ReturnInvalid(out mode)
            };
        }

        private static bool ReturnMode(PaymentSplitMode value, out PaymentSplitMode mode)
        {
            mode = value;
            return true;
        }

        private static bool ReturnInvalid(out PaymentSplitMode mode)
        {
            mode = PaymentSplitMode.Full;
            return false;
        }

        private static string NormalizePaymentMethod(string? method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return "cash";
            }

            var normalized = method.Trim().ToLowerInvariant();
            return normalized switch
            {
                "momo" => "momo",
                "card" or "pos" => "card",
                "cash" or "counter" or "cashier" => "cash",
                _ => "cash"
            };
        }

        private static string CombinePaymentMethods(IEnumerable<PaymentShare> shares)
        {
            return string.Join(", ", shares
                .Select(s => s.PaymentMethod)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .OrderBy(m => m));
        }

        private static IReadOnlyCollection<int> ParseItemIds(string? metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata))
            {
                return Array.Empty<int>();
            }

            try
            {
                var doc = JsonSerializer.Deserialize<ItemMetadata>(metadata, JsonOptions);
                return doc?.ItemIds?.Distinct().ToArray() ?? Array.Empty<int>();
            }
            catch
            {
                return Array.Empty<int>();
            }
        }

        private static string SerializeItemMetadata(IEnumerable<int> itemIds)
        {
            return JsonSerializer.Serialize(new ItemMetadata { ItemIds = itemIds.Distinct().ToArray() }, JsonOptions);
        }

        private static string GetSplitModeKey(PaymentSplitMode mode) => mode switch
        {
            PaymentSplitMode.Full => "full",
            PaymentSplitMode.SplitEvenly => "even",
            PaymentSplitMode.SplitByPercentage => "percentage",
            PaymentSplitMode.PayOwnItems => "items",
            _ => "full"
        };

        private sealed class ItemMetadata
        {
            public int[]? ItemIds { get; set; }
        }
    }
}
