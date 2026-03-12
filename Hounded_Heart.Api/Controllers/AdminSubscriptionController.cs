using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.DTOs;
using Hounded_Heart.Services.ServiceResult;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    //[Authorize] // Admin authorization should be added
    public class AdminSubscriptionController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminSubscriptionController(AppDbContext context)
        {
            _context = context;
        }


        [HttpGet("plans")]
        public IActionResult GetPlans()
        {
            try
            {
                var service = new PriceService();

                var options = new PriceListOptions
                {
                    Active = true,
                    Type = "recurring",
                    Expand = new List<string> { "data.product" }
                };

                var prices = service.List(options);

                var result = prices.Data.Select(p => new
                {
                    PriceId = p.Id,
                    ProductName = ((Product)p.Product).Name,
                    Amount = p.UnitAmount / 100,
                    Currency = p.Currency,
                    Interval = p.Recurring?.Interval
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching plans from Stripe: {ex.Message}");
                // Return an empty list or a meaningful error instead of crashing
                return Ok(new List<object>()); 
            }
        }
        // ═══════════════════════════════════════════════════════════════════
        // GET /api/AdminSubscription/all
        // Get all subscriptions with user details
        // ═══════════════════════════════════════════════════════════════════
        [HttpGet("all")]
        public async Task<IActionResult> GetAllSubscriptions(
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var query = _context.Subscriptions
                    .Include(s => s.User)
                    .AsQueryable();

                // Filter by status if provided
                if (!string.IsNullOrEmpty(status) && status.ToLower() != "all")
                {
                    query = query.Where(s => s.Status.ToLower() == status.ToLower());
                }

                // Get total count
                var totalCount = await query.CountAsync();

                // Pagination
                var subscriptions = await query
                    .OrderByDescending(s => s.CreatedOn)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(s => new AdminSubscriptionDto
                    {
                        SubscriptionId = s.SubscriptionId,
                        UserId = s.UserId,
                        UserEmail = s.User.Email,
                        UserName = s.User.FullName,
                        PlanName = s.PlanName,
                        Status = s.Status,
                        CurrentPeriodStart = s.CurrentPeriodStart,
                        CurrentPeriodEnd = s.CurrentPeriodEnd,
                        Amount = s.Amount,
                        Currency = s.Currency,
                        CreatedOn = s.CreatedOn,
                        CancelAtPeriodEnd = s.CancelAtPeriodEnd,
                        StripeCustomerId = s.StripeCustomerId,
                        StripeSubscriptionId = s.StripeSubscriptionId
                    })
                    .ToListAsync();

                return Ok(ResponseHelper.Success(new
                {
                    subscriptions,
                    totalCount,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }, "Subscriptions retrieved successfully", 200));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Get all subscriptions error: {ex.Message}");
                return StatusCode(500, ResponseHelper.Fail<object>($"Error retrieving subscriptions: {ex.Message}"));
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // GET /api/AdminSubscription/user/{userId}
        // Get specific user's subscriptions
        // ═══════════════════════════════════════════════════════════════════
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserSubscriptions(Guid userId)
        {
            try
            {
                var subscriptions = await _context.Subscriptions
                    .Include(s => s.User)
                    .Where(s => s.UserId == userId)
                    .OrderByDescending(s => s.CreatedOn)
                    .Select(s => new AdminSubscriptionDto
                    {
                        SubscriptionId = s.SubscriptionId,
                        UserId = s.UserId,
                        UserEmail = s.User.Email,
                        UserName = s.User.FullName,
                        PlanName = s.PlanName,
                        Status = s.Status,
                        CurrentPeriodStart = s.CurrentPeriodStart,
                        CurrentPeriodEnd = s.CurrentPeriodEnd,
                        Amount = s.Amount,
                        Currency = s.Currency,
                        CreatedOn = s.CreatedOn,
                        CancelAtPeriodEnd = s.CancelAtPeriodEnd,
                        StripeCustomerId = s.StripeCustomerId,
                        StripeSubscriptionId = s.StripeSubscriptionId
                    })
                    .ToListAsync();

                return Ok(ResponseHelper.Success(subscriptions, "User subscriptions retrieved successfully", 200));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Get user subscriptions error: {ex.Message}");
                return StatusCode(500, ResponseHelper.Fail<object>($"Error retrieving user subscriptions: {ex.Message}"));
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // GET /api/AdminSubscription/stats
        // Get subscription statistics and revenue
        // ═══════════════════════════════════════════════════════════════════
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var totalSubscriptions = await _context.Subscriptions.CountAsync();
                var activeSubscriptions = await _context.Subscriptions.CountAsync(s => s.Status == "active");
                var canceledSubscriptions = await _context.Subscriptions.CountAsync(s => s.Status == "canceled");
                var pastDueSubscriptions = await _context.Subscriptions.CountAsync(s => s.Status == "past_due");
                var trialingSubscriptions = await _context.Subscriptions.CountAsync(s => s.Status == "trialing");

                // Calculate MRR (Monthly Recurring Revenue)
                var activeMonthlySubscriptions = await _context.Subscriptions
                    .Where(s => s.Status == "active" && s.PlanName.Contains("Monthly"))
                    .SumAsync(s => s.Amount ?? 0);

                var activeYearlySubscriptions = await _context.Subscriptions
                    .Where(s => s.Status == "active" && s.PlanName.Contains("Yearly"))
                    .SumAsync(s => s.Amount ?? 0);

                // For yearly subscriptions, divide by 12 to get monthly value
                var monthlyRecurringRevenue = activeMonthlySubscriptions + (activeYearlySubscriptions / 12);

                // Calculate total revenue from all completed subscriptions
                var totalRevenue = await _context.Subscriptions
                    .Where(s => s.Status == "active" || s.Status == "canceled")
                    .SumAsync(s => s.Amount ?? 0);

                var stats = new SubscriptionStatsDto
                {
                    TotalSubscriptions = totalSubscriptions,
                    ActiveSubscriptions = activeSubscriptions,
                    CanceledSubscriptions = canceledSubscriptions,
                    PastDueSubscriptions = pastDueSubscriptions,
                    TrialingSubscriptions = trialingSubscriptions,
                    MonthlyRecurringRevenue = Math.Round(monthlyRecurringRevenue, 2),
                    YearlyRecurringRevenue = Math.Round(monthlyRecurringRevenue * 12, 2),
                    TotalRevenue = Math.Round(totalRevenue, 2)
                };

                return Ok(ResponseHelper.Success(stats, "Statistics retrieved successfully", 200));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Get stats error: {ex.Message}");
                return StatusCode(500, ResponseHelper.Fail<object>($"Error retrieving statistics: {ex.Message}"));
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // GET /api/AdminSubscription/logs
        // Get subscription event logs
        // ═══════════════════════════════════════════════════════════════════
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs(
            [FromQuery] string? eventType = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                var query = _context.SubscriptionLogs
                    .Include(l => l.User)
                    .AsQueryable();

                // Filter by event type if provided
                if (!string.IsNullOrEmpty(eventType))
                {
                    query = query.Where(l => l.EventType.ToLower() == eventType.ToLower());
                }

                // Get total count
                var totalCount = await query.CountAsync();

                // Pagination
                var logs = await query
                    .OrderByDescending(l => l.CreatedOn)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(l => new
                    {
                        l.LogId,
                        l.SubscriptionId,
                        l.UserId,
                        UserEmail = l.User != null ? l.User.Email : null,
                        l.EventType,
                        l.CreatedOn
                    })
                    .ToListAsync();

                return Ok(ResponseHelper.Success(new
                {
                    logs,
                    totalCount,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }, "Logs retrieved successfully", 200));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Get logs error: {ex.Message}");
                return StatusCode(500, ResponseHelper.Fail<object>($"Error retrieving logs: {ex.Message}"));
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // GET /api/AdminSubscription/search
        // Search subscriptions by email or name
        // ═══════════════════════════════════════════════════════════════════
        [HttpGet("search")]
        public async Task<IActionResult> SearchSubscriptions([FromQuery] string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return BadRequest(ResponseHelper.Fail<object>("Search query is required"));
                }

                var searchTerm = query.ToLower();

                var subscriptions = await _context.Subscriptions
                    .Include(s => s.User)
                    .Where(s => s.User.Email.ToLower().Contains(searchTerm) ||
                                s.User.FullName.ToLower().Contains(searchTerm))
                    .OrderByDescending(s => s.CreatedOn)
                    .Take(50)
                    .Select(s => new AdminSubscriptionDto
                    {
                        SubscriptionId = s.SubscriptionId,
                        UserId = s.UserId,
                        UserEmail = s.User.Email,
                        UserName = s.User.FullName,
                        PlanName = s.PlanName,
                        Status = s.Status,
                        CurrentPeriodStart = s.CurrentPeriodStart,
                        CurrentPeriodEnd = s.CurrentPeriodEnd,
                        Amount = s.Amount,
                        Currency = s.Currency,
                        CreatedOn = s.CreatedOn,
                        CancelAtPeriodEnd = s.CancelAtPeriodEnd,
                        StripeCustomerId = s.StripeCustomerId,
                        StripeSubscriptionId = s.StripeSubscriptionId
                    })
                    .ToListAsync();

                return Ok(ResponseHelper.Success(subscriptions, $"Found {subscriptions.Count} subscriptions", 200));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Search subscriptions error: {ex.Message}");
                return StatusCode(500, ResponseHelper.Fail<object>($"Error searching subscriptions: {ex.Message}"));
            }
        }
    }
}
