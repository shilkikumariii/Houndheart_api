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
    public class RitualsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public RitualsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("suggestions")]
        public async Task<IActionResult> GetSuggestions([FromQuery] Guid userId)
        {
            if (userId == Guid.Empty) return BadRequest("UserId is required.");

            var today = DateTime.UtcNow.Date;

            // Get all rituals
            var rituals = await _context.Rituals.AsNoTracking().ToListAsync();

            // Get today's logs for this user
            var todayLogs = await _context.RitualLogs
                .AsNoTracking()
                .Where(l => l.UserId == userId && l.CompletedAt >= today && l.CompletedAt < today.AddDays(1))
                .ToListAsync();

            // Check if user already got the +2 bonus today
            var bonusEarned = todayLogs.Any(l => l.BonusAwarded);

            var result = rituals.Select(r => new
            {
                r.Id,
                r.Title,
                r.Description,
                r.Duration,
                r.Category,
                r.IconType,
                IsCompleted = todayLogs.Any(l => l.RitualId == r.Id)
            });

            return Ok(new
            {
                dailyBonusEarned = bonusEarned,
                rituals = result
            });
        }

        [HttpPost("complete")]
        public async Task<IActionResult> CompleteRitual([FromBody] CompleteRitualRequest request)
        {
            if (request == null || request.UserId == Guid.Empty || request.RitualId == Guid.Empty)
                return BadRequest("Invalid request.");

            var today = DateTime.UtcNow.Date;

            // 1. Check if already completed today
            var existingLog = await _context.RitualLogs
                .FirstOrDefaultAsync(l => l.UserId == request.UserId 
                                       && l.RitualId == request.RitualId 
                                       && l.CompletedAt >= today 
                                       && l.CompletedAt < today.AddDays(1));

            if (existingLog != null)
            {
                return Ok(new { message = "Ritual already completed today." });
            }

            // 2. Check global bonus status
            bool bonusEarnedToday = await _context.RitualLogs
                .AnyAsync(l => l.UserId == request.UserId 
                            && l.BonusAwarded 
                            && l.CompletedAt >= today 
                            && l.CompletedAt < today.AddDays(1));

            bool awardBonus = !bonusEarnedToday;

            // 3. Create Log
            var newLog = new RitualLog
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                RitualId = request.RitualId,
                CompletedAt = DateTime.UtcNow,
                BonusAwarded = awardBonus
            };
            _context.RitualLogs.Add(newLog);

            // 4. Award Points if eligible
            double newScore = 0;
            if (awardBonus)
            {
                var dog = await _context.Dogs.FirstOrDefaultAsync(d => d.UserId == request.UserId);
                if (dog != null)
                {
                    dog.CurrentScore = Math.Min(100, dog.CurrentScore + 2.0);
                    dog.UpdatedOn = DateTime.UtcNow;
                    _context.Dogs.Update(dog);
                    newScore = dog.CurrentScore;
                }
            }
            else
            {
                // Retrieve current score strictly for return value
                var dog = await _context.Dogs.FirstOrDefaultAsync(d => d.UserId == request.UserId);
                newScore = dog?.CurrentScore ?? 0;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Ritual completed.",
                bonusAwarded = awardBonus,
                newScore = newScore
            });
        }

        public class CompleteRitualRequest
        {
            public Guid UserId { get; set; }
            public Guid RitualId { get; set; }
        }
    }
}
