using System;
using System.Collections.Concurrent;
using System.Linq;
using ASM_1.Data;
using ASM_1.Models.Food;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ASM_1.Services
{
    public class UserSessionService
    {
        private const string SessionKey = "UserSessionId";
        private const string CookieKey = "UserSessionId";

        private static readonly ConcurrentDictionary<string, SessionState> SessionStates = new();
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(2);

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<UserSessionService>? _logger;

        public UserSessionService(
            IHttpContextAccessor httpContextAccessor,
            ApplicationDbContext dbContext,
            ILogger<UserSessionService>? logger = null)
        {
            _httpContextAccessor = httpContextAccessor;
            _dbContext = dbContext;
            _logger = logger;
        }

        public string GetOrCreateUserSessionId(string tableCode)
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null)
            {
                return $"{tableCode}_{Guid.NewGuid():N}";
            }

            var sessionId = context.Session.GetString(SessionKey);

            if (!string.IsNullOrEmpty(sessionId) && !sessionId.StartsWith($"{tableCode}_", StringComparison.Ordinal))
            {
                ResetSession(context, sessionId);
                sessionId = null;
            }

            var nowUtc = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(sessionId))
            {
                if (ShouldResetSession(sessionId, tableCode, nowUtc))
                {
                    ResetSession(context, sessionId);
                    sessionId = null;
                }
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = CreateNewSession(context, tableCode, nowUtc);
            }

            TouchSession(context, sessionId, tableCode, nowUtc);

            return sessionId;
        }

        private string CreateNewSession(HttpContext context, string tableCode, DateTime nowUtc)
        {
            var sessionId = $"{tableCode}_{Guid.NewGuid():N}";

            context.Session.SetString(SessionKey, sessionId);
            WriteSessionCookie(context, sessionId, nowUtc);
            SessionStates[sessionId] = new SessionState(tableCode, nowUtc);

            return sessionId;
        }

        private void TouchSession(HttpContext context, string sessionId, string tableCode, DateTime nowUtc)
        {
            WriteSessionCookie(context, sessionId, nowUtc);

            SessionStates.AddOrUpdate(
                sessionId,
                _ => new SessionState(tableCode, nowUtc),
                (_, existing) =>
                {
                    existing.TableCode = tableCode;
                    existing.LastActivityUtc = nowUtc;
                    return existing;
                });
        }

        private static void WriteSessionCookie(HttpContext context, string sessionId, DateTime nowUtc)
        {
            context.Response.Cookies.Append(CookieKey, sessionId, new CookieOptions
            {
                Expires = nowUtc.Add(CookieLifetime),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private void ResetSession(HttpContext context, string sessionId)
        {
            try
            {
                context.Session.Remove(SessionKey);
                context.Response.Cookies.Delete(CookieKey);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to clear existing session state for {SessionId}", sessionId);
            }

            SessionStates.TryRemove(sessionId, out _);
        }

        private bool ShouldResetSession(string sessionId, string tableCode, DateTime nowUtc)
        {
            SessionStates.AddOrUpdate(
                sessionId,
                _ => new SessionState(tableCode, nowUtc),
                (_, existing) =>
                {
                    if (!string.Equals(existing.TableCode, tableCode, StringComparison.Ordinal))
                    {
                        existing.TableCode = tableCode;
                    }

                    return existing;
                });

            try
            {
                var orderStates = _dbContext.Orders
                    .Where(o => o.UserSessionId == sessionId)
                    .Select(o => new OrderSnapshot
                    {
                        Status = o.Status,
                        InvoiceStatus = o.Invoice != null ? o.Invoice.Status : null
                    })
                    .ToList();

                bool hasOrders = orderStates.Count > 0;
                bool hasActiveOrders = orderStates.Any(o => !IsOrderSettled(o));

                if (hasActiveOrders)
                {
                    return false;
                }

                if (hasOrders)
                {
                    return true;
                }

                if (SessionStates.TryGetValue(sessionId, out var info))
                {
                    var idle = nowUtc - info.LastActivityUtc;
                    if (idle >= IdleTimeout)
                    {
                        return true;
                    }

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to evaluate session lifecycle for {SessionId}", sessionId);
                return false;
            }
        }

        private static bool IsOrderSettled(OrderSnapshot snapshot)
        {
            if (snapshot.Status == OrderStatus.Paid || snapshot.Status == OrderStatus.Canceled)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.InvoiceStatus) &&
                string.Equals(snapshot.InvoiceStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private sealed class SessionState
        {
            public SessionState(string tableCode, DateTime lastActivityUtc)
            {
                TableCode = tableCode;
                LastActivityUtc = lastActivityUtc;
            }

            public string TableCode { get; set; }
            public DateTime LastActivityUtc { get; set; }
        }

        private sealed class OrderSnapshot
        {
            public OrderStatus Status { get; set; }
            public string? InvoiceStatus { get; set; }
        }
    }
}
