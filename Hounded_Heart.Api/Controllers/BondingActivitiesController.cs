using Hounded_Heart.Models.Data;
using Hounded_Heart.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BondingActivitiesController : ControllerBase
    {

        private readonly AppDbContext _context;
        public BondingActivitiesController(AppDbContext context)
        {
            _context = context;
        }
        [HttpGet("GetAllBondingActivities")]
        public async Task<IActionResult> GetAllBondingActivities()
        {
            try
            {
                var activities = await _context.BondingActivities
                    .OrderBy(a => a.ActivityName)
                    .ToListAsync();

                if (activities == null || activities.Count == 0)
                    return NotFound(new { message = "No bonding activities found." });

                return Ok(activities);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching bonding activities.", error = ex.Message });
            }
        }

        [HttpGet("GetTodayActivities/{userId}")]
        public async Task<IActionResult> GetTodayActivities(Guid userId)
        {
            try
            {
                var today = DateTime.UtcNow.Date;

                var activities = await _context.UserBondingActivities
                    .Where(x => x.UserId == userId && x.ActivityDate == today)
                    .Include(x => x.Activity)
                    .ToListAsync();

                if (activities == null || activities.Count == 0)
                    return Ok(new { message = "No activities done today.", data = new List<object>() });

                return Ok(activities);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching todays activities.", error = ex.Message });
            }
        }


    }
}
