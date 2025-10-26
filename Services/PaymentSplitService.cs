using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ASM_1.Models.Food;

namespace ASM_1.Services
{
    public static class PaymentSplitService
    {
        public const string ModeSingle = "single";
        public const string ModeEqual = "equal";
        public const string ModePercentage = "percentage";
        public const string ModeItems = "items";

        private static readonly CultureInfo VietnamCulture = CultureInfo.GetCultureInfo("vi-VN");

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static PaymentSplitResult CalculateSplit(
            string? mode,
            PaymentSplitPayload? payload,
            IReadOnlyCollection<CartItem> cartItems,
            decimal totalAmount)
        {
            mode = NormalizeMode(mode);
            payload ??= new PaymentSplitPayload();

            var participants = EnsureParticipants(payload.Participants);
            List<PaymentSplitParticipantShare> shares = mode switch
            {
                ModeEqual => CalculateEqualShares(participants, totalAmount),
                ModePercentage => CalculatePercentageShares(participants, totalAmount),
                ModeItems => CalculateItemShares(participants, cartItems, totalAmount),
                _ => CalculateSingleShare(participants, totalAmount)
            };

            shares = NormalizeAmounts(shares, totalAmount);

            return new PaymentSplitResult
            {
                Mode = mode,
                Participants = shares,
                Notes = BuildSummary(mode, shares),
                AdditionalNote = string.IsNullOrWhiteSpace(payload.Notes) ? null : payload.Notes.Trim()
            };
        }

        public static string SerializePayload(PaymentSplitPayload payload)
            => JsonSerializer.Serialize(payload, SerializerOptions);

        public static PaymentSplitPayload? DeserializePayload(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PaymentSplitPayload>(raw, SerializerOptions);
            }
            catch
            {
                return null;
            }
        }

        public static string ComposeInvoiceNote(PaymentSplitResult splitResult, InvoiceRequestInfo? invoiceRequest)
        {
            var segments = new List<string>();

            if (!string.IsNullOrWhiteSpace(splitResult.Notes))
            {
                segments.Add($"Chia bill: {splitResult.Notes}");
            }

            if (!string.IsNullOrWhiteSpace(splitResult.AdditionalNote))
            {
                segments.Add($"Ghi chú chia bill: {splitResult.AdditionalNote}");
            }

            if (invoiceRequest != null && invoiceRequest.IsRequested)
            {
                var invoiceLines = new List<string>();
                if (!string.IsNullOrWhiteSpace(invoiceRequest.CompanyName))
                {
                    invoiceLines.Add($"Công ty: {invoiceRequest.CompanyName.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(invoiceRequest.TaxCode))
                {
                    invoiceLines.Add($"MST: {invoiceRequest.TaxCode.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(invoiceRequest.Email))
                {
                    invoiceLines.Add($"Email: {invoiceRequest.Email.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(invoiceRequest.Address))
                {
                    invoiceLines.Add($"Địa chỉ: {invoiceRequest.Address.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(invoiceRequest.Note))
                {
                    invoiceLines.Add($"Ghi chú: {invoiceRequest.Note.Trim()}");
                }

                if (invoiceLines.Count > 0)
                {
                    segments.Add("Xuất hóa đơn: " + string.Join("; ", invoiceLines));
                }
            }

            return string.Join(" | ", segments);
        }

        public static InvoiceRequestInfo? BuildInvoiceRequestInfo(bool requested, string? company, string? taxCode, string? email, string? address, string? note)
        {
            if (!requested)
            {
                return null;
            }

            return new InvoiceRequestInfo
            {
                IsRequested = true,
                CompanyName = company,
                TaxCode = taxCode,
                Email = email,
                Address = address,
                Note = note
            };
        }

        private static string NormalizeMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return ModeSingle;
            }

            mode = mode.Trim().ToLowerInvariant();
            return mode switch
            {
                ModeEqual => ModeEqual,
                ModePercentage => ModePercentage,
                ModeItems => ModeItems,
                _ => ModeSingle
            };
        }

        private static List<PaymentSplitParticipantShare> CalculateSingleShare(List<PaymentSplitParticipantPayload> participants, decimal totalAmount)
        {
            var name = participants.FirstOrDefault()?.Name;
            name = string.IsNullOrWhiteSpace(name) ? "Khách thanh toán" : name.Trim();

            return new List<PaymentSplitParticipantShare>
            {
                new()
                {
                    Name = name,
                    Amount = totalAmount,
                    Percentage = 100m,
                    Items = new List<string>()
                }
            };
        }

        private static List<PaymentSplitParticipantShare> CalculateEqualShares(List<PaymentSplitParticipantPayload> participants, decimal totalAmount)
        {
            var cleaned = participants
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToList();

            if (cleaned.Count == 0)
            {
                cleaned.Add(new PaymentSplitParticipantPayload { Name = "Khách 1" });
            }

            var shareValue = totalAmount / cleaned.Count;
            var result = new List<PaymentSplitParticipantShare>();

            foreach (var participant in cleaned)
            {
                var name = participant.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Khách {result.Count + 1}";
                }

                result.Add(new PaymentSplitParticipantShare
                {
                    Name = name,
                    Amount = shareValue,
                    Percentage = Math.Round(100m / cleaned.Count, 2),
                    Items = new List<string>()
                });
            }

            return result;
        }

        private static List<PaymentSplitParticipantShare> CalculatePercentageShares(List<PaymentSplitParticipantPayload> participants, decimal totalAmount)
        {
            var cleaned = participants
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.Percentage.HasValue && p.Percentage.Value > 0)
                .ToList();

            if (cleaned.Count == 0)
            {
                return CalculateSingleShare(participants, totalAmount);
            }

            var sumPercent = cleaned.Sum(p => p.Percentage!.Value);
            if (sumPercent <= 0)
            {
                return CalculateSingleShare(participants, totalAmount);
            }

            var factor = 100m / sumPercent;
            var result = new List<PaymentSplitParticipantShare>();
            foreach (var participant in cleaned)
            {
                var normalizedPercent = Math.Round(participant.Percentage!.Value * factor, 2);
                var amount = totalAmount * normalizedPercent / 100m;
                var name = participant.Name!.Trim();

                result.Add(new PaymentSplitParticipantShare
                {
                    Name = name,
                    Amount = amount,
                    Percentage = normalizedPercent,
                    Items = new List<string>()
                });
            }

            return result;
        }

        private static List<PaymentSplitParticipantShare> CalculateItemShares(List<PaymentSplitParticipantPayload> participants, IReadOnlyCollection<CartItem> cartItems, decimal totalAmount)
        {
            var cleanedParticipants = participants.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (cleanedParticipants.Count == 0)
            {
                cleanedParticipants.Add(new PaymentSplitParticipantPayload { Name = "Khách 1" });
            }

            var itemTotals = cartItems.ToDictionary(
                item => item.CartItemID.ToString(),
                item => item.UnitPrice * item.Quantity);

            var itemNames = cartItems.ToDictionary(
                item => item.CartItemID.ToString(),
                item => item.ProductName);

            var result = new List<PaymentSplitParticipantShare>();

            foreach (var participant in cleanedParticipants)
            {
                var name = participant.Name!.Trim();
                var amount = 0m;
                var assignedItems = new List<string>();

                if (participant.ItemKeys != null)
                {
                    foreach (var key in participant.ItemKeys)
                    {
                        if (key == null) continue;
                        if (!itemTotals.TryGetValue(key, out var total)) continue;

                        amount += total;
                        assignedItems.Add(itemNames.TryGetValue(key, out var itemName)
                            ? itemName
                            : $"Món #{key}");
                    }
                }

                result.Add(new PaymentSplitParticipantShare
                {
                    Name = name,
                    Amount = amount,
                    Percentage = totalAmount == 0 ? 0 : Math.Round(amount * 100m / totalAmount, 2),
                    Items = assignedItems
                });
            }

            var distributed = result.Sum(r => r.Amount);
            var remaining = totalAmount - distributed;
            if (Math.Abs(remaining) > 0.01m)
            {
                if (result.Count == 0)
                {
                    result.Add(new PaymentSplitParticipantShare
                    {
                        Name = "Khách",
                        Amount = totalAmount,
                        Percentage = 100m,
                        Items = new List<string>()
                    });
                }
                else
                {
                    var first = result[0];
                    first.Amount += remaining;
                    first.Percentage = totalAmount == 0 ? 0 : Math.Round(first.Amount * 100m / totalAmount, 2);
                }
            }

            return result;
        }

        private static List<PaymentSplitParticipantPayload> EnsureParticipants(List<PaymentSplitParticipantPayload>? participants)
        {
            if (participants == null)
            {
                return new List<PaymentSplitParticipantPayload>
                {
                    new() { Name = "Khách" }
                };
            }

            participants = participants.Where(p => p != null).ToList();

            if (participants.Count == 0)
            {
                return new List<PaymentSplitParticipantPayload>
                {
                    new() { Name = "Khách" }
                };
            }

            return participants;
        }

        private static List<PaymentSplitParticipantShare> NormalizeAmounts(List<PaymentSplitParticipantShare> shares, decimal totalAmount)
        {
            if (shares.Count == 0)
            {
                shares.Add(new PaymentSplitParticipantShare
                {
                    Name = "Khách",
                    Amount = totalAmount,
                    Percentage = 100m,
                    Items = new List<string>()
                });
                return shares;
            }

            var sum = shares.Sum(s => s.Amount);
            var diff = totalAmount - sum;
            if (Math.Abs(diff) > 0.02m)
            {
                var last = shares[^1];
                last.Amount += diff;
                if (totalAmount > 0)
                {
                    last.Percentage = Math.Round(last.Amount * 100m / totalAmount, 2);
                }
            }

            foreach (var share in shares)
            {
                share.Amount = decimal.Round(share.Amount, 2, MidpointRounding.AwayFromZero);
                if (totalAmount > 0)
                {
                    share.Percentage = Math.Round(share.Amount * 100m / totalAmount, 2);
                }
            }

            return shares;
        }

        private static string BuildSummary(string mode, IReadOnlyCollection<PaymentSplitParticipantShare> shares)
        {
            if (shares.Count == 0)
            {
                return string.Empty;
            }

            string modeLabel = mode switch
            {
                ModeEqual => "Chia đều",
                ModePercentage => "Chia theo %",
                ModeItems => "Chia theo món",
                _ => "Một người thanh toán"
            };

            var parts = shares.Select(share =>
            {
                var amountText = share.Amount.ToString("N0", VietnamCulture) + "₫";
                if (share.Items != null && share.Items.Count > 0)
                {
                    var items = string.Join(", ", share.Items);
                    return $"{share.Name}: {amountText} ({items})";
                }

                return share.Percentage.HasValue
                    ? $"{share.Name}: {amountText} ({share.Percentage.Value:0.#}%)"
                    : $"{share.Name}: {amountText}";
            });

            return $"{modeLabel} - {string.Join("; ", parts)}";
        }
    }

    public class PaymentSplitPayload
    {
        public List<PaymentSplitParticipantPayload> Participants { get; set; } = new();
        public string? Notes { get; set; }
    }

    public class PaymentSplitParticipantPayload
    {
        public string? Name { get; set; }
        public decimal? Percentage { get; set; }
        public List<string>? ItemKeys { get; set; }
    }

    public class PaymentSplitParticipantShare
    {
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal? Percentage { get; set; }
        public List<string> Items { get; set; } = new();
    }

    public class PaymentSplitResult
    {
        public string Mode { get; set; } = PaymentSplitService.ModeSingle;
        public List<PaymentSplitParticipantShare> Participants { get; set; } = new();
        public string? Notes { get; set; }
        public string? AdditionalNote { get; set; }
    }

    public class InvoiceRequestInfo
    {
        public bool IsRequested { get; set; }
        public string? CompanyName { get; set; }
        public string? TaxCode { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Note { get; set; }
    }
}
