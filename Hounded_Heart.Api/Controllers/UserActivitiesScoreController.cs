using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserActivitiesScoreController : ControllerBase
    {
        private readonly AppDbContext _context;
        public UserActivitiesScoreController(AppDbContext context) => _context = context;

        [HttpPost("save")]
        public async Task<IActionResult> SaveUserActivitiesScore([FromBody] SaveUserActivitiesScoreRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest("Request is null");

                if (request.UserId == Guid.Empty)
                    return BadRequest("Invalid UserId");

                if (request.Activities == null || !request.Activities.Any())
                    return BadRequest("No activities provided");

                // Use provided local date or fallback to UTC Today
                var activityDate = request.Date?.Date ?? DateTime.UtcNow.Date;
                var now = DateTime.UtcNow;
                
                List<UserActivitiesScore> scoreEntities = new();
                List<UserBondingActivity> bondingEntities = new();
                int skippedCount = 0;

                foreach (var item in request.Activities)
                {
                    // Check if already completed today (using UserBondingActivity as daily log)
                    bool exists = await _context.UserBondingActivities
                        .AnyAsync(x => x.UserId == request.UserId 
                                    && x.ActivityId == item.ActivityId 
                                    && x.ActivityDate == activityDate);

                    if (exists)
                    {
                        skippedCount++;
                        continue;
                    }

                    // 1. Add to Score Log (for points calculation)
                    scoreEntities.Add(new UserActivitiesScore
                    {
                        UserId = request.UserId,
                        ActivityId = item.ActivityId,
                        Score = item.Score,     // INDIVIDUAL SCORE
                        CreatedAt = now,
                        ActivityDate = activityDate,
                        ActivityDetails = "Daily Activity"
                    });

                    // 2. Add to Daily Log (to prevent double dipping)
                    bondingEntities.Add(new UserBondingActivity
                    {
                        UserId = request.UserId,
                        ActivityId = item.ActivityId,
                        ActivityDate = activityDate,
                        CreatedAt = now
                    });
                }

                if (scoreEntities.Any())
                {
                    await _context.UserActivitiesScores.AddRangeAsync(scoreEntities);
                    await _context.UserBondingActivities.AddRangeAsync(bondingEntities);
                    await _context.SaveChangesAsync();
                }

                return Ok(new
                {
                    success = true,
                    message = skippedCount > 0 
                        ? $"Saved {scoreEntities.Count} activities. Skipped {skippedCount} duplicates."
                        : "Activities saved successfully!",
                    totalSaved = scoreEntities.Count,
                    skipped = skippedCount
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        [HttpGet("get-all-by-user")]
        public async Task<IActionResult> GetAllByUser([FromQuery] Guid userId)
        {
            if (userId == Guid.Empty) return BadRequest("Invalid userId");

            var list = await _context.UserActivitiesScores
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            return Ok(list);
        }

        [HttpGet("total-score")]
        public async Task<IActionResult> GetTotalScore([FromQuery] Guid userId)
        {
            if (userId == Guid.Empty) return BadRequest("Invalid userId");

            var total = await _context.UserActivitiesScores
                .Where(x => x.UserId == userId)
                .SumAsync(x => x.Score ?? 0);

            return Ok(new { total });
        }

        [HttpGet("daily-score")]
        public async Task<IActionResult> GetDailyScore([FromQuery] Guid userId, [FromQuery] DateTime date)
        {
            if (userId == Guid.Empty) return BadRequest("Invalid userId");

            var start = date.Date;
            var end = start.AddDays(1);

            var total = await _context.UserActivitiesScores
                .Where(x => x.UserId == userId && x.CreatedAt >= start && x.CreatedAt < end)
                .SumAsync(x => x.Score ?? 0);

            return Ok(new { date = start, total });
        }

        [HttpGet("weekly-score")]
        public async Task<IActionResult> GetWeeklyScore([FromQuery] Guid userId)
        {
            if (userId == Guid.Empty) return BadRequest("Invalid userId");

            // Consider week start as Monday
            var today = DateTime.UtcNow.Date;
            var diff = (int)today.DayOfWeek;
            // convert Sunday(0) to 6 if you prefer Monday start:
            var startOfWeek = diff == 0 ? today.AddDays(-6) : today.AddDays(-(diff - 1));

            var total = await _context.UserActivitiesScores
                .Where(x => x.UserId == userId && x.CreatedAt >= startOfWeek)
                .SumAsync(x => x.Score ?? 0);

            return Ok(new { weekStart = startOfWeek, total });
        }

        [HttpGet("check-done-today")]
        public async Task<IActionResult> CheckDoneToday([FromQuery] Guid userId, [FromQuery] Guid activityId)
        {
            if (userId == Guid.Empty || activityId == Guid.Empty) return BadRequest("Invalid IDs");

            var today = DateTime.UtcNow.Date;
            var exists = await _context.UserActivitiesScores
                .AnyAsync(x => x.UserId == userId
                            && x.ActivityId == activityId
                            && x.CreatedAt >= today
                            && x.CreatedAt < today.AddDays(1));

            return Ok(new { done = exists });
        }

        [HttpGet("user/{userId}/today")]
        public async Task<IActionResult> GetUserActivitiesToday(Guid userId, [FromQuery] DateTime? clientDate = null)
        {
            if (userId == Guid.Empty) return BadRequest(new { message = "Invalid userId" });

            var today = clientDate?.Date ?? DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var activities = await _context.UserActivitiesScores
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.CreatedAt >= today && x.CreatedAt < tomorrow)
                .ToListAsync();

            var totalScore = activities.Sum(x => x.Score ?? 0);

            return Ok(new
            {
                message = "Today's activities fetched successfully",
                data = activities,
                totalScore = totalScore,
                count = activities.Count
            });
        }
    }
}
