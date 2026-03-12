using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Hounded_Heart.Api.Data
{
    public static class DbInitializer
    {
        public static async Task Initialize(AppDbContext context)
        {
            // Ensure database is created
            // context.Database.EnsureCreated(); // Or use migrations

            // Check if "Dog Behavior" check-in exists
            var checkInText = "How is your dog's behavior today? (0/10)";
            var exists = await context.CheckIns.AnyAsync(c => c.Questions == checkInText);

            if (!exists)
            {
                var newCheckIn = new CheckIn
                {
                    CheckInId = Guid.NewGuid(),
                    Questions = checkInText, // Matches user request
                    Rating = 0, // Default in DB, but frontend handles user interaction
                    LowEnergyLabel = "Restless / Stressed",
                    HighEnergyLabel = "Calm / Playful",
                    CreatedOn = DateTime.UtcNow,
                    IsDeleted = false
                };

                await context.CheckIns.AddAsync(newCheckIn);
                await context.SaveChangesAsync();
            }
        }
    }
}
