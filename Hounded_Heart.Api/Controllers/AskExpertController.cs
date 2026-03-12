using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.DTOs;
using Hounded_Heart.Services.ServiceResult;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AskExpertController : ControllerBase
    {

        private readonly AppDbContext _context;
        public AskExpertController(AppDbContext context)
        {
            _context = context;
        }
        [HttpPost("submit-question")]
        public async Task<IActionResult> SubmitQuestion([FromBody] SubmitExpertQuestionDto request)
        {
            try
                                {
                // Validate request
                if (request.UserId == Guid.Empty)
                    return Ok(ResponseHelper.Fail<object>("Invalid user ID."));

                if (string.IsNullOrWhiteSpace(request.Name))
                    return Ok(ResponseHelper.Fail<object>("Name is required."));

                if (string.IsNullOrWhiteSpace(request.Email))
                    return Ok(ResponseHelper.Fail<object>("Valid email is required."));

                if (string.IsNullOrWhiteSpace(request.Category))
                    return Ok(ResponseHelper.Fail<object>("Category is required."));

                if (string.IsNullOrWhiteSpace(request.Subject))
                    return Ok(ResponseHelper.Fail<object>("Subject is required."));

                if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length < 50)
                    return Ok(ResponseHelper.Fail<object>("Question must be at least 50 characters long."));

                // Priority validation
                var validPriorities = new[] { "normal", "urgent", "high" };
                if (!validPriorities.Contains(request.Priority?.ToLower()))
                    request.Priority = "normal";

                // Create entity
                var expertQuestion = new ExpertQuestion
                {
                    ExpertQuestionId = Guid.NewGuid(),
                    UserId = request.UserId,
                    Name = request.Name.Trim(),
                    Email = request.Email.Trim().ToLower(),

                    CompanionName = string.IsNullOrWhiteSpace(request.CompanionName)
                        ? null
                        : request.CompanionName.Trim(),

                    Priority = request.Priority.ToLower(),
                    Category = request.Category.Trim(),
                    Subject = request.Subject.Trim(),
                    Question = request.Question.Trim(),

                    Status = "Pending", // DEFAULT
                    CreatedOn = DateTime.Now,
                    UpdatedOn = null,
                    IsActive = true
                };

                // Save
                _context.ExpertQuestions.Add(expertQuestion);
                await _context.SaveChangesAsync();

                var responseData = new
                {
                    expertQuestion.ExpertQuestionId,
                    expertQuestion.Status,
                    expertQuestion.CreatedOn
                };

                return Ok(ResponseHelper.Success(
                    responseData,
                    "Your question has been submitted successfully."
                ));
            }
            catch (Exception ex)
            {
                return Ok(ResponseHelper.Fail<object>($"Error: {ex.Message}"));
            }
        }

        [HttpGet("get-user-questions")]
        public async Task<IActionResult> GetUserQuestions(Guid userId)
        {
            try
            {
                if (userId == Guid.Empty)
                    return Ok(ResponseHelper.Fail<object>("Invalid user ID."));

                var questions = await _context.ExpertQuestions
                    .Where(q => q.UserId == userId && q.IsActive == true)
                    .OrderByDescending(q => q.CreatedOn)
                    .Select(q => new
                    {
                        q.ExpertQuestionId,
                        q.Category,
                        q.Subject,
                        q.Status,
                        q.Priority,
                        q.CreatedOn,
                        q.UpdatedOn
                    })
                    .ToListAsync();

                return Ok(ResponseHelper.Success(
                    questions,
                    "User questions retrieved successfully."
                ));
            }
            catch (Exception ex)
            {
                return Ok(ResponseHelper.Fail<object>($"Error: {ex.Message}"));
            }
        }
    }
}
