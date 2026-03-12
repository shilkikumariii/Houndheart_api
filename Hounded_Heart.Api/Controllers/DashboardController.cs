using Hounded_Heart.Models.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("Stats")]
        public async Task<IActionResult> GetDashboardStats([FromQuery] Guid userId, [FromQuery] DateTime? clientDate)
        {
            try
            {
                if (userId == Guid.Empty)
                    return BadRequest(new { message = "Invalid user ID" });

                // Use Client's local date/time if provided to avoid the "Midnight Bug"
                var baseDate = clientDate?.Date ?? DateTime.UtcNow.Date;
                
                // Calculate Monday of the current week (Calendar Logic)
                int diff = ((int)baseDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var startOfCurrentWeek = baseDate.AddDays(-1 * diff);
                
                // For "Last 7 Days" logic used in progress calculation, we still want to look back 
                // but the user specifically asked for "Current Week (Monday to Sunday)" for progress/consistency displays.
                // However, scoring often looks back. Let's align exactly with the user's request:
                // "This Week's Progress (Monday to Sunday)"
                
                var startOfMonth = new DateTime(baseDate.Year, baseDate.Month, 1);

                // A. This Week's Progress (Points joined in the current Mondy-Sunday week)
                var weeklyCheckIns = await _context.UserCheckIns
                    .AsNoTracking()
                    .Include(x => x.CheckIn)
                    .Where(x => x.UserId == userId && x.CreatedOn >= startOfCurrentWeek)
                    .ToListAsync();

                double estimatedWeeklyGain = 0;
                
                // Group by day to apply daily logic (prioritize ActivityDate)
                var daysWithCheckIns = weeklyCheckIns.GroupBy(x => x.ActivityDate ?? x.CreatedOn.Date).ToDictionary(g => g.Key, g => g.ToList());
                
                // Iterate from startOfCurrentWeek (Monday) to today
                int daysSoFar = (baseDate - startOfCurrentWeek).Days + 1;
                for (int i = 0; i < daysSoFar; i++)
                {
                    var loopDate = startOfCurrentWeek.AddDays(i);
                    double dayPoints = 0;
                    
                    if (daysWithCheckIns.ContainsKey(loopDate))
                    {
                        var uciList = daysWithCheckIns[loopDate];
                        double positive = 1.0; // Base Sync Reward
                        double penalty = 0;

                        foreach(var uci in uciList) {
                            if (uci.CheckIn == null) continue;
                            string q = uci.CheckIn.Questions ?? "";
                            int rating = uci.Rating ?? 0;

                            if (q.Contains("hours", StringComparison.OrdinalIgnoreCase))
                               positive += Math.Min(10, rating);
                            
                            if (q.Contains("peaceful", StringComparison.OrdinalIgnoreCase) && rating >= 7)
                               positive += 1.0;
                            
                            if (q.Contains("energy", StringComparison.OrdinalIgnoreCase))
                               positive += 2.0; 

                            // Emergency/Neglect Penalty
                            if ((q.Contains("Emergency", StringComparison.OrdinalIgnoreCase) || q.Contains("Neglect", StringComparison.OrdinalIgnoreCase)) && rating >= 7)
                                penalty += 5.0;
                        }
                         
                        // Rituals Positive (Check localized date)
                        bool didRitual = await _context.UserActivitiesScores
                            .AnyAsync(x => x.UserId == userId && (x.ActivityDate == loopDate || (x.ActivityDate == null && x.CreatedAt.Date == loopDate))) 
                            || await _context.RitualLogs.AnyAsync(x => x.UserId == userId && x.CompletedAt.Date == loopDate);
                        
                        if (didRitual) positive += 2.0;

                        dayPoints = Math.Min(15, positive) - penalty;
                    } 
                    else 
                    {
                        // Check if missed check-in penalty applies
                         var userJoinedDate = await _context.Users
                            .Where(u => u.UserId == userId)
                            .Select(u => u.CreatedOn)
                            .FirstOrDefaultAsync();

                         // Only apply if user was a member on this day and it's strictly before today
                         if (userJoinedDate.Date < loopDate && loopDate < baseDate)
                         {
                             // Penalty -3 for missing check-in
                             dayPoints = -3.0;
                         }
                    }
                    
                    estimatedWeeklyGain += dayPoints;
                }
                
                double weeklyProgressValue = estimatedWeeklyGain;

                // B. Ritual Consistency (Count distinct days from Monday)
                // Includes: RitualLogs, UserCheckIns, ChakraLogs, and UserActivitiesScores
                var ritualDays = await _context.RitualLogs
                    .Where(x => x.UserId == userId && x.CompletedAt >= startOfCurrentWeek)
                    .Select(x => x.CompletedAt.Date)
                    .Distinct()
                    .ToListAsync();

                var activityDays = await _context.UserActivitiesScores
                    .Where(x => x.UserId == userId && (x.ActivityDate >= startOfCurrentWeek || (x.ActivityDate == null && x.CreatedAt >= startOfCurrentWeek)))
                    .Select(x => x.ActivityDate ?? x.CreatedAt.Date)
                    .Distinct()
                    .ToListAsync();

                var checkInDays = await _context.UserCheckIns
                    .Where(x => x.UserId == userId && (x.ActivityDate >= startOfCurrentWeek || (x.ActivityDate == null && x.CreatedOn >= startOfCurrentWeek)))
                    .Select(x => x.ActivityDate ?? x.CreatedOn.Date)
                    .Distinct()
                    .ToListAsync();
                
                var chakraDays = await _context.ChakraLogs
                    .Where(x => x.UserId == userId && (x.LogDate >= startOfCurrentWeek || (x.LogDate == null && x.CreatedAt >= startOfCurrentWeek)))
                    .Select(x => x.LogDate ?? x.CreatedAt.Date)
                    .Distinct()
                    .ToListAsync();

                var allTogether = ritualDays.Concat(activityDays).Concat(checkInDays).Concat(chakraDays).Distinct().Count();

                // C. Journal Entries (Current Local Month)
                var monthEntriesCount = await _context.JournalEntries
                    .Where(x => x.UserId == userId && x.CreatedOn >= startOfMonth && !x.IsDeleted)
                    .CountAsync();

                var dog = await _context.Dogs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.UserId == userId);

                return Ok(new
                {
                    weeklyProgress = weeklyProgressValue,
                    ritualConsistency = new { count = allTogether, total = 7 },
                    journalEntries = new { count = monthEntriesCount, label = $"{monthEntriesCount} this month" },
                    bondedScore = (dog?.CurrentScore ?? 50)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching dashboard stats", error = ex.Message });
            }
        }
    }
}
