using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.Dtos;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Hounded_Heart.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class BreathingController : ControllerBase
    {
        private readonly AppDbContext _context;
        public BreathingController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("patterns")]
        public async Task<IActionResult> GetPatterns()
        {
            try
            {
                var patterns = await _context.BreathingPatterns
                    .Where(p => p.IsActive && !p.IsDeleted)
                    .Select(p => new
                    {
                        p.Id,
                        p.Name,
                        p.Description,
                        Timings = new
                        {
                            Inhale = p.InhaleDuration,
                            Exhale = p.ExhaleDuration,
                            Hold = p.HoldDuration,
                            HoldAfterExhale = p.HoldAfterExhaleDuration
                        }
                    })
                    .ToListAsync();

                return Ok(patterns);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("cycles")]
        public async Task<IActionResult> GetTargetCycles()
        {
            try
            {
                var cycles = await _context.TargetCycles
                    .Where(c => c.IsActive && !c.IsDeleted)
                    .OrderBy(c => c.Cycles)
                    .Select(c => new
                    {
                        c.Id,
                        c.Cycles,
                        c.DurationDescription
                    })
                    .ToListAsync();

                return Ok(cycles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        public class CompleteBreathingSessionRequest
        {
            public Guid PatternId { get; set; }
            public string PatternName { get; set; }
            public int TargetCycles { get; set; }
            public int CompletedCycles { get; set; }
            public int DurationSeconds { get; set; }
        }

        [HttpPost("complete")]
        public async Task<IActionResult> CompleteSession([FromBody] CompleteBreathingSessionRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
                {
                    return Unauthorized("User ID not found in token.");
                }

                // Award points logic
                var today = DateTime.UtcNow.Date;
                var activityName = "Synchronized Breathing";
                
                var activity = await _context.BondingActivities.FirstOrDefaultAsync(a => a.ActivityName == activityName);
                if (activity == null)
                {
                    // Fallback if activity not found (should be seeded)
                    return Ok(new { message = "Session completed. (Activity configuration missing)" });
                }

                // Check for daily limit (2 points max)
                bool alreadyCompleted = await _context.UserActivitiesScores
                    .AnyAsync(uas => uas.UserId == userId && uas.ActivityId == activity.ActivityId && uas.CreatedAt.Date == today);

                if (!alreadyCompleted)
                {
                    var activityDetailsJson = System.Text.Json.JsonSerializer.Serialize(request);

                    var score = new UserActivitiesScore
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        ActivityId = activity.ActivityId,
                        Score = activity.Points,
                        ActivityDetails = activityDetailsJson,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.UserActivitiesScores.Add(score);
                    await _context.SaveChangesAsync();
                    
                    return Ok(new { message = "Session completed.", points = activity.Points });
                }

                return Ok(new { message = "Session completed. Daily limit reached.", points = 0 });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // ===== NEW: Breathing Preferences API =====

        public class SaveBreathingPreferenceRequest
        {
            public Guid? PatternId { get; set; }
            public string PatternName { get; set; }
            public int TargetCycles { get; set; } = 10;
        }

        /// <summary>
        /// GET /api/Breathing/preferences — Fetch user's saved breathing preferences
        /// </summary>
        [HttpGet("preferences")]
        public async Task<IActionResult> GetPreferences()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
                {
                    return Unauthorized("User ID not found in token.");
                }

                var pref = await _context.UserBreathingPreferences
                    .AsNoTracking()
                    .Where(p => p.UserId == userId)
                    .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
                    .FirstOrDefaultAsync();

                if (pref == null)
                {
                    // Return defaults for new users
                    return Ok(new
                    {
                        patternId = (Guid?)null,
                        patternName = "4-7-8",
                        targetCycles = 10,
                        isDefault = true
                    });
                }

                return Ok(new
                {
                    patternId = pref.PatternId,
                    patternName = pref.PatternName,
                    targetCycles = pref.TargetCycles,
                    isDefault = false
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        /// <summary>
        /// POST /api/Breathing/preferences — Save or update user's breathing preferences
        /// </summary>
        [HttpPost("preferences")]
        public async Task<IActionResult> SavePreferences([FromBody] SaveBreathingPreferenceRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
                {
                    return Unauthorized("User ID not found in token.");
                }

                // Find existing preference (upsert logic)
                var existing = await _context.UserBreathingPreferences
                    .FirstOrDefaultAsync(p => p.UserId == userId);

                if (existing != null)
                {
                    // Update existing
                    existing.PatternId = request.PatternId;
                    existing.PatternName = request.PatternName;
                    existing.TargetCycles = request.TargetCycles;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _context.UserBreathingPreferences.Update(existing);
                }
                else
                {
                    // Create new
                    var newPref = new UserBreathingPreference
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        PatternId = request.PatternId,
                        PatternName = request.PatternName,
                        TargetCycles = request.TargetCycles,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.UserBreathingPreferences.Add(newPref);
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Breathing preferences saved successfully.",
                    patternName = request.PatternName,
                    targetCycles = request.TargetCycles
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
