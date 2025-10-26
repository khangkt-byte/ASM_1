using System.Text.Json;
using ASM_1.Models.Food;
using ASM_1.Models.Payments;

namespace ASM_1.Services
{
    public static class PaymentSplitCalculator
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static PaymentSplitComputationResult Compute(
            PaymentSplitRequest? request,
            IReadOnlyCollection<CartItem> cartItems,
            decimal finalAmount,
            string fallbackPaymentMethod)
        {
            var normalizedFallback = NormalizePaymentMethod(fallbackPaymentMethod, "cash");
            if (request == null || request.Participants.Count == 0)
            {
                return BuildSinglePayerResult(normalizedFallback, finalAmount);
            }

            var mode = NormalizeMode(request.Mode);
            return mode switch
            {
                PaymentSplitMode.Full => BuildSinglePayerResult(
                    NormalizePaymentMethod(request.Participants.FirstOrDefault()?.PaymentMethod, normalizedFallback),
                    finalAmount,
                    request.Participants.FirstOrDefault()?.Name,
                    request.Participants.FirstOrDefault()?.Id),
                PaymentSplitMode.Even => BuildEvenSplit(request, finalAmount, normalizedFallback),
                PaymentSplitMode.Percentage => BuildPercentageSplit(request, finalAmount, normalizedFallback),
                PaymentSplitMode.ByItem => BuildItemSplit(request, cartItems, finalAmount, normalizedFallback),
                _ => BuildSinglePayerResult(normalizedFallback, finalAmount)
            };
        }

        private static PaymentSplitMode NormalizeMode(string? mode)
        {
            return mode?.Trim().ToLowerInvariant() switch
            {
                "even" => PaymentSplitMode.Even,
                "percentage" => PaymentSplitMode.Percentage,
                "by-item" => PaymentSplitMode.ByItem,
                "byitem" => PaymentSplitMode.ByItem,
                _ => PaymentSplitMode.Full
            };
        }

        private static string NormalizePaymentMethod(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var normalized = value.Trim().ToLowerInvariant();
            return normalized switch
            {
                "card" or "momo" or "cash" => normalized,
                "cod" => "cash",
                _ => fallback
            };
        }

        private static PaymentSplitComputationResult BuildSinglePayerResult(string method, decimal amount, string? name = null, string? participantId = null)
        {
            var share = new PaymentSplitShare
            {
                ParticipantId = participantId ?? "single",
                DisplayName = string.IsNullOrWhiteSpace(name) ? "Khách" : name!.Trim(),
                PaymentMethod = method,
                Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
                SplitMode = PaymentSplitMode.Full
            };

            return new PaymentSplitComputationResult(
                PaymentSplitMode.Full,
                new[] { share },
                $"Thanh toán toàn bộ bằng {GetMethodLabel(method)}",
                AreAllPrepaid(new[] { share }),
                SerializeMeta(new[] { share }));
        }

        private static PaymentSplitComputationResult BuildEvenSplit(PaymentSplitRequest request, decimal total, string fallbackMethod)
        {
            var participants = request.Participants
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .ToList();

            if (participants.Count == 0)
            {
                return BuildSinglePayerResult(fallbackMethod, total);
            }

            var count = participants.Count;
            var baseShare = Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
            var shares = new List<PaymentSplitShare>(count);
            decimal accumulated = 0m;

            for (int i = 0; i < count; i++)
            {
                var participant = participants[i];
                var method = NormalizePaymentMethod(participant.PaymentMethod, fallbackMethod);
                var amount = i == count - 1
                    ? Math.Round(total - accumulated, 2, MidpointRounding.AwayFromZero)
                    : baseShare;

                accumulated += amount;

                shares.Add(new PaymentSplitShare
                {
                    ParticipantId = participant.Id,
                    DisplayName = string.IsNullOrWhiteSpace(participant.Name) ? $"Khách {i + 1}" : participant.Name.Trim(),
                    PaymentMethod = method,
                    Amount = amount,
                    SplitMode = PaymentSplitMode.Even
                });
            }

            var label = $"Chia đều cho {shares.Count} người";
            return new PaymentSplitComputationResult(
                PaymentSplitMode.Even,
                shares,
                label,
                AreAllPrepaid(shares),
                SerializeMeta(shares));
        }

        private static PaymentSplitComputationResult BuildPercentageSplit(PaymentSplitRequest request, decimal total, string fallbackMethod)
        {
            var participants = request.Participants
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .Select(p => new
                {
                    Participant = p,
                    Percentage = Math.Clamp(p.Percentage ?? 0m, 0m, 100m)
                })
                .Where(x => x.Percentage > 0)
                .ToList();

            if (participants.Count == 0)
            {
                return BuildSinglePayerResult(fallbackMethod, total);
            }

            var totalPercent = participants.Sum(x => x.Percentage);
            if (totalPercent <= 0)
            {
                return BuildSinglePayerResult(fallbackMethod, total);
            }

            var shares = new List<PaymentSplitShare>(participants.Count);
            decimal accumulated = 0m;

            for (int i = 0; i < participants.Count; i++)
            {
                var entry = participants[i];
                var method = NormalizePaymentMethod(entry.Participant.PaymentMethod, fallbackMethod);

                decimal percentage = entry.Percentage / totalPercent;
                decimal amount = Math.Round(total * percentage, 2, MidpointRounding.AwayFromZero);

                accumulated += amount;

                if (i == participants.Count - 1)
                {
                    var corrected = Math.Round(total - (accumulated - amount), 2, MidpointRounding.AwayFromZero);
                    accumulated = accumulated - amount + corrected;
                    amount = corrected;
                }

                shares.Add(new PaymentSplitShare
                {
                    ParticipantId = entry.Participant.Id,
                    DisplayName = string.IsNullOrWhiteSpace(entry.Participant.Name) ? $"Khách {i + 1}" : entry.Participant.Name.Trim(),
                    PaymentMethod = method,
                    Amount = amount,
                    SplitMode = PaymentSplitMode.Percentage,
                    Percentage = Math.Round(entry.Percentage, 2, MidpointRounding.AwayFromZero)
                });
            }

            var label = $"Chia theo tỷ lệ phần trăm ({Math.Round(totalPercent, 2)}%)";
            return new PaymentSplitComputationResult(
                PaymentSplitMode.Percentage,
                shares,
                label,
                AreAllPrepaid(shares),
                SerializeMeta(shares));
        }

        private static PaymentSplitComputationResult BuildItemSplit(PaymentSplitRequest request, IReadOnlyCollection<CartItem> cartItems, decimal total, string fallbackMethod)
        {
            if (cartItems == null || cartItems.Count == 0)
            {
                return BuildSinglePayerResult(fallbackMethod, total);
            }

            var participants = request.Participants
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .ToList();

            if (participants.Count == 0)
            {
                return BuildSinglePayerResult(fallbackMethod, total);
            }

            var itemMap = cartItems.ToDictionary(ci => ci.CartItemID, ci => ci);
            var assignedQuantities = cartItems.ToDictionary(ci => ci.CartItemID, ci => 0);

            var shares = new List<PaymentSplitShare>(participants.Count);

            foreach (var participant in participants)
            {
                var method = NormalizePaymentMethod(participant.PaymentMethod, fallbackMethod);
                var displayName = string.IsNullOrWhiteSpace(participant.Name) ? $"Khách {shares.Count + 1}" : participant.Name.Trim();
                decimal subtotal = 0m;
                Dictionary<int, int>? itemSelections = null;

                if (participant.Items != null && participant.Items.Count > 0)
                {
                    itemSelections = new();
                    foreach (var selection in participant.Items)
                    {
                        if (!itemMap.TryGetValue(selection.CartItemId, out var cartItem))
                        {
                            continue;
                        }

                        var quantity = Math.Clamp(selection.Quantity, 0, cartItem.Quantity);
                        if (quantity <= 0)
                        {
                            continue;
                        }

                        var alreadyAssigned = assignedQuantities[cartItem.CartItemID];
                        var remaining = cartItem.Quantity - alreadyAssigned;
                        if (remaining <= 0)
                        {
                            continue;
                        }

                        var acceptedQuantity = Math.Min(quantity, remaining);
                        if (acceptedQuantity <= 0)
                        {
                            continue;
                        }

                        assignedQuantities[cartItem.CartItemID] += acceptedQuantity;
                        subtotal += cartItem.UnitPrice * acceptedQuantity;
                        itemSelections[cartItem.CartItemID] = acceptedQuantity;
                    }
                }

                shares.Add(new PaymentSplitShare
                {
                    ParticipantId = participant.Id,
                    DisplayName = displayName,
                    PaymentMethod = method,
                    Amount = Math.Round(subtotal, 2, MidpointRounding.AwayFromZero),
                    SplitMode = PaymentSplitMode.ByItem,
                    ItemQuantities = itemSelections
                });
            }

            var subtotalTotal = shares.Sum(s => s.Amount);
            if (subtotalTotal <= 0m)
            {
                return BuildSinglePayerResult(fallbackMethod, total);
            }

            var remainder = Math.Round(total - subtotalTotal, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(remainder) > 0.01m)
            {
                var receiver = shares.FirstOrDefault(s => request.Participants.FirstOrDefault(p => p.Id == s.ParticipantId)?.PaysRemaining == true)
                    ?? shares.OrderByDescending(s => s.Amount).First();
                receiver.Amount = Math.Round(receiver.Amount + remainder, 2, MidpointRounding.AwayFromZero);
            }

            var label = $"Chia theo món ({shares.Count} người)";
            return new PaymentSplitComputationResult(
                PaymentSplitMode.ByItem,
                shares,
                label,
                AreAllPrepaid(shares),
                SerializeMeta(shares));
        }

        private static bool AreAllPrepaid(IEnumerable<PaymentSplitShare> shares)
        {
            return shares.All(s => s.PaymentMethod is "card" or "momo");
        }

        private static string SerializeMeta(IEnumerable<PaymentSplitShare> shares)
        {
            return JsonSerializer.Serialize(shares.Select(s => new
            {
                s.ParticipantId,
                s.DisplayName,
                s.PaymentMethod,
                s.Amount,
                s.Percentage,
                Items = s.ItemQuantities
            }), JsonOptions);
        }

        private static string GetMethodLabel(string method)
        {
            return method switch
            {
                "card" => "thẻ", 
                "momo" => "MoMo",
                "cash" => "tiền mặt",
                _ => method
            };
        }
    }

    public enum PaymentSplitMode
    {
        Full,
        Even,
        Percentage,
        ByItem
    }

    public class PaymentSplitShare
    {
        public string ParticipantId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public PaymentSplitMode SplitMode { get; set; }
        public decimal? Percentage { get; set; }
        public Dictionary<int, int>? ItemQuantities { get; set; }
    }

    public record PaymentSplitComputationResult(
        PaymentSplitMode Mode,
        IReadOnlyList<PaymentSplitShare> Shares,
        string DisplayLabel,
        bool AllPrepaid,
        string MetaJson);
}
