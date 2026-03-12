using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JournalEntryController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Hounded_Heart.Services.Services.BlobStorageService _blobService;

        public JournalEntryController(AppDbContext context, Hounded_Heart.Services.Services.BlobStorageService blobService)
        {
            _context = context;
            _blobService = blobService;
        }

        [HttpGet("GetAlltags")]
        public async Task<IActionResult> GetAlltags()
        {
            try
            {
                var tags = await _context.Tags
                    .ToListAsync();

                if (tags == null)
                    return NotFound(new { message = "No tags found." });

                return Ok(tags);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while fetching tags.", error = ex.Message });
            }
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddJournalEntry([FromForm] JournalEntryDto dto, IFormFile? audioFile, IFormFile? imageFile)
        {
            if (dto == null || dto.UserId == Guid.Empty || string.IsNullOrWhiteSpace(dto.EntryType))
                return BadRequest(new { Message = "Invalid data provided." });

            string? mediaUrl = null;
            string mediaType = dto.MediaType ?? "Text";
            string? imageUrl = null;

            // Handle Audio Upload
            if (audioFile != null && audioFile.Length > 0)
            {
                try 
                {
                    // Convert stream to byte array
                    using var memoryStream = new MemoryStream();
                    await audioFile.CopyToAsync(memoryStream);
                    var audioBytes = memoryStream.ToArray();
                    var fileName = $"journal_{dto.UserId}_{Guid.NewGuid()}.wav";
                    
                    mediaUrl = await _blobService.UploadAudioFileAsync(audioBytes, fileName);
                    mediaType = "Audio";
                }
                catch (Exception ex)
                {
                     return StatusCode(500, new { Message = "Audio upload failed.", Error = ex.Message });
                }
            }

            // Handle Image Upload
            if (imageFile != null && imageFile.Length > 0)
            {
                try
                {
                    using var memoryStream = new MemoryStream();
                    await imageFile.CopyToAsync(memoryStream);
                    var imageBytes = memoryStream.ToArray();
                    var extension = Path.GetExtension(imageFile.FileName) ?? ".jpg";
                    var fileName = $"journal_{dto.UserId}_{Guid.NewGuid()}{extension}";

                    imageUrl = await _blobService.UploadImageFileAsync(imageBytes, fileName);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { Message = "Image upload failed.", Error = ex.Message });
                }
            }

            var entry = new JournalEntry
            {
                EntryId = Guid.NewGuid(),
                UserId = dto.UserId,
                EntryType = dto.EntryType,
                Content = dto.Content,
                Tags = dto.Tags,
                CreatedOn = DateTime.UtcNow,
                IsDeleted = false,
                IsArchive = dto.IsArchive ?? false,
                LettrTo = dto.LettrTo,
                MediaType = mediaType,
                MediaUrl = mediaUrl,
                ImageUrl = imageUrl
            };

            try
            {
                _context.JournalEntries.Add(entry);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    Message = "Journal entry saved successfully.",
                    Entry = entry
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred while saving the journal entry.", Error = ex.Message });
            }
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetEntriesByUser(Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? entryType = null)
        {
            if (userId == Guid.Empty)
                return BadRequest(new { Message = "Invalid user ID." });

            try
            {
                var query = _context.JournalEntries
                    .Where(e => e.UserId == userId && !e.IsDeleted);

                if (!string.IsNullOrEmpty(entryType))
                {
                    query = query.Where(e => e.EntryType == entryType);
                }

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var entries = await query
                    .OrderByDescending(e => e.CreatedOn)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                if (entries == null || entries.Count == 0)
                    return Ok(new { 
                        Message = "No journal entries found.", 
                        Entries = new List<JournalEntry>(),
                        TotalCount = totalCount,
                        TotalPages = totalPages,
                        CurrentPage = page
                    });

                return Ok(new { 
                    Message = "Journal entries fetched successfully.", 
                    Entries = entries,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    CurrentPage = page
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Error fetching journal entries.", Error = ex.Message });
            }
        }

    }
}
