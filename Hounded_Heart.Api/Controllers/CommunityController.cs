using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.Dtos;
using Hounded_Heart.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CommunityController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly BlobStorageService _blobStorage;

        public CommunityController(AppDbContext context, BlobStorageService blobStorage)
        {
            _context = context;
            _blobStorage = blobStorage;
        }

        private Guid? GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? User.FindFirst("UserId")?.Value
                           ?? User.FindFirst("sub")?.Value;

            return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }

        // ───────────────────────────────────────────────
        // GET /api/Community/posts?page=1&pageSize=10
        // Returns paginated posts with user info, like status, top comments
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpGet("posts")]
        public async Task<IActionResult> GetPosts(
            [FromQuery] int page = 1, 
            [FromQuery] int pageSize = 10,
            [FromQuery] string searchQuery = null,
            [FromQuery] string category = null,
            [FromQuery] string sortBy = "newest")
        {
            try
            {
                var userId = GetUserId();
                if (userId == null)
                    return Unauthorized(new { success = false, message = "User ID not found in token." });

                var query = _context.CommunityPosts
                    .Where(p => !p.IsDeleted)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(searchQuery))
                {
                    query = query.Where(p => 
                        p.Content.Contains(searchQuery) || 
                        (p.User.FullName != null && p.User.FullName.Contains(searchQuery))
                    );
                }

                if (!string.IsNullOrWhiteSpace(category) && category != "All Posts")
                {
                    if (category == "Healing")
                        query = query.Where(p => p.Content.Contains("healing") || p.Content.Contains("ritual") || p.Content.Contains("soul") || p.Content.Contains("divine"));
                    else if (category == "Rituals")
                        query = query.Where(p => p.Content.Contains("ritual") || p.Content.Contains("ceremony") || p.Content.Contains("circle"));
                    else if (category == "Success Stories")
                        query = query.Where(p => p.Content.Contains("success") || p.Content.Contains("story") || p.Content.Contains("journey") || p.Content.Contains("completed"));
                    else if (category == "Questions")
                        query = query.Where(p => p.Content.Contains("?") || p.Content.Contains("how") || p.Content.Contains("what") || p.Content.Contains("why") || p.Content.Contains("help"));
                }

                if (sortBy == "top")
                {
                    query = query.OrderByDescending(p => p.LikeCount).ThenByDescending(p => p.CreatedOn);
                }
                else if (sortBy == "oldest")
                {
                    query = query.OrderBy(p => p.CreatedOn);
                }
                else
                {
                    query = query.OrderByDescending(p => p.CreatedOn);
                }

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var posts = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.PostId,
                        p.Content,
                        p.ImageUrl,
                        p.LikeCount,
                        p.CommentCount,
                        p.CreatedOn,
                        p.UserId,
                        // Author info
                        Author = new
                        {
                            p.User.UserId,
                            p.User.FullName,
                            p.User.ProfileName,
                            p.User.ProfilePhoto,
                            DogName = p.User.Dog != null ? p.User.Dog.DogName : null
                        },
                        // Whether the current user liked this post
                        IsLikedByMe = _context.CommunityLikes
                            .Any(l => l.PostId == p.PostId && l.UserId == userId.Value),
                        // Top 2 comments for preview
                        TopComments = _context.CommunityComments
                            .Where(c => c.PostId == p.PostId && !c.IsDeleted)
                            .OrderByDescending(c => c.CreatedOn)
                            .Take(2)
                            .Select(c => new
                            {
                                c.CommentId,
                                c.Content,
                                c.CreatedOn,
                                c.UserId,
                                c.ParentCommentId,
                                Author = new
                                {
                                    c.User.FullName,
                                    c.User.ProfilePhoto
                                }
                            })
                            .ToList()
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = "Posts retrieved.",
                    data = new
                    {
                        posts,
                        totalCount,
                        totalPages,
                        currentPage = page
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ───────────────────────────────────────────────
        // POST /api/Community/posts
        // Create a new community post
        // ───────────────────────────────────────────────
        public class CreatePostDto
        {
            public string Content { get; set; }
            public string? ImageUrl { get; set; }
        }

        [Authorize]
        [HttpPost("posts")]
        public async Task<IActionResult> CreatePost([FromBody] CreatePostDto dto)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null)
                    return Unauthorized(new { success = false, message = "User ID not found in token." });

                if (string.IsNullOrWhiteSpace(dto?.Content))
                    return BadRequest(new { success = false, message = "Post content cannot be empty." });

                if (dto.Content.Length > 2000)
                    return BadRequest(new { success = false, message = "Post content exceeds 2000 characters." });

                string finalImageUrl = null;
                if (!string.IsNullOrEmpty(dto.ImageUrl) && dto.ImageUrl.StartsWith("data:image"))
                {
                    try
                    {
                        var fileName = $"post_{Guid.NewGuid()}.png";
                        finalImageUrl = await _blobStorage.UploadBase64ImageAsync(dto.ImageUrl, fileName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Image upload failed: {ex.Message}");
                        // Optionally fail or continue without image
                    }
                }
                else
                {
                    finalImageUrl = dto.ImageUrl;
                }

                var post = new CommunityPost
                {
                    PostId = Guid.NewGuid(),
                    UserId = userId.Value,
                    Content = dto.Content.Trim(),
                    ImageUrl = finalImageUrl,
                    CreatedOn = DateTime.UtcNow,
                    IsDeleted = false,
                    LikeCount = 0,
                    CommentCount = 0
                };

                _context.CommunityPosts.Add(post);
                await _context.SaveChangesAsync();

                // Fetch back with author info
                var author = await _context.Users
                    .Where(u => u.UserId == userId.Value)
                    .Select(u => new
                    {
                        u.UserId,
                        u.FullName,
                        u.ProfileName,
                        u.ProfilePhoto,
                        DogName = u.Dog != null ? u.Dog.DogName : null
                    })
                    .FirstOrDefaultAsync();

                return Ok(new
                {
                    success = true,
                    message = "Post created successfully.",
                    data = new
                    {
                        post.PostId,
                        post.Content,
                        post.ImageUrl,
                        post.LikeCount,
                        post.CommentCount,
                        post.CreatedOn,
                        Author = author,
                        IsLikedByMe = false,
                        TopComments = new List<object>()
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ───────────────────────────────────────────────
        // POST /api/Community/posts/{id}/like
        // Toggle like on a post
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpPost("posts/{id}/like")]
        public async Task<IActionResult> ToggleLike(Guid id)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null)
                    return Unauthorized(new { success = false, message = "User ID not found in token." });

                var post = await _context.CommunityPosts
                    .FirstOrDefaultAsync(p => p.PostId == id && !p.IsDeleted);
                if (post == null)
                    return NotFound(new { success = false, message = "Post not found." });

                var existingLike = await _context.CommunityLikes
                    .FirstOrDefaultAsync(l => l.PostId == id && l.UserId == userId.Value);

                bool isLiked;
                if (existingLike != null)
                {
                    // Unlike
                    _context.CommunityLikes.Remove(existingLike);
                    post.LikeCount = Math.Max(0, post.LikeCount - 1);
                    isLiked = false;
                }
                else
                {
                    // Like
                    _context.CommunityLikes.Add(new CommunityLike
                    {
                        LikeId = Guid.NewGuid(),
                        PostId = id,
                        UserId = userId.Value,
                        CreatedOn = DateTime.UtcNow
                    });
                    post.LikeCount += 1;
                    isLiked = true;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = isLiked ? "Post liked." : "Post unliked.",
                    data = new
                    {
                        post.PostId,
                        post.LikeCount,
                        isLiked
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ───────────────────────────────────────────────
        // GET /api/Community/posts/{id}/comments?page=1&pageSize=20
        // Get paginated comments for a post
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpGet("posts/{id}/comments")]
        public async Task<IActionResult> GetComments(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var postExists = await _context.CommunityPosts
                    .AnyAsync(p => p.PostId == id && !p.IsDeleted);
                if (!postExists)
                    return NotFound(new { success = false, message = "Post not found." });

                var query = _context.CommunityComments
                    .Where(c => c.PostId == id && !c.IsDeleted)
                    .OrderByDescending(c => c.CreatedOn);

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var comments = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(c => new
                    {
                        c.CommentId,
                        c.Content,
                        c.CreatedOn,
                        c.UserId,
                        c.ParentCommentId,
                        Author = new
                        {
                            c.User.UserId,
                            c.User.FullName,
                            c.User.ProfileName,
                            c.User.ProfilePhoto
                        }
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    message = "Comments retrieved.",
                    data = new
                    {
                        comments,
                        totalCount,
                        totalPages,
                        currentPage = page
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ───────────────────────────────────────────────
        // POST /api/Community/posts/{id}/comments
        // Add a comment to a post
        // ───────────────────────────────────────────────
        public class AddCommentDto
        {
            public string Content { get; set; }
            public Guid? ParentCommentId { get; set; }
        }

        [Authorize]
        [HttpPost("posts/{id}/comments")]
        public async Task<IActionResult> AddComment(Guid id, [FromBody] AddCommentDto dto)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null)
                    return Unauthorized(new { success = false, message = "User ID not found in token." });

                if (string.IsNullOrWhiteSpace(dto?.Content))
                    return BadRequest(new { success = false, message = "Comment cannot be empty." });

                if (dto.Content.Length > 1000)
                    return BadRequest(new { success = false, message = "Comment exceeds 1000 characters." });

                var post = await _context.CommunityPosts
                    .FirstOrDefaultAsync(p => p.PostId == id && !p.IsDeleted);
                if (post == null)
                    return NotFound(new { success = false, message = "Post not found." });

                var comment = new CommunityComment
                {
                    CommentId = Guid.NewGuid(),
                    PostId = id,
                    UserId = userId.Value,
                    Content = dto.Content.Trim(),
                    ParentCommentId = dto.ParentCommentId,
                    CreatedOn = DateTime.UtcNow,
                    IsDeleted = false
                };

                _context.CommunityComments.Add(comment);
                post.CommentCount += 1;
                await _context.SaveChangesAsync();

                // Fetch author info for response
                var author = await _context.Users
                    .Where(u => u.UserId == userId.Value)
                    .Select(u => new
                    {
                        u.UserId,
                        u.FullName,
                        u.ProfileName,
                        u.ProfilePhoto
                    })
                    .FirstOrDefaultAsync();

                return Ok(new
                {
                    success = true,
                    message = "Comment added.",
                    data = new
                    {
                        comment.CommentId,
                        comment.Content,
                        comment.CreatedOn,
                        comment.ParentCommentId,
                        Author = author,
                        post.CommentCount
                    }
                });
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? $": {ex.InnerException.Message}" : "";
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}{innerMsg}" });
            }
        }

        // ───────────────────────────────────────────────
        // GET /api/Community/stats
        // Returns overall community statistics
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var activeMembers = await _context.Users.CountAsync();
                var storiesShared = await _context.CommunityPosts.CountAsync(p => !p.IsDeleted);
                var healingCircles = await _context.HealingCircles.CountAsync();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        activeMembers = activeMembers,
                        storiesShared = storiesShared,
                        healingCircles = healingCircles,
                        avgBondGrowth = "+12%" // Keeping this one as logic based on bonding scores might be complex for now
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // GET /api/Community/trending
        // Returns trending topics from DB
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpGet("trending")]
        public async Task<IActionResult> GetTrending()
        {
            try
            {
                var trending = await _context.TrendingTopics
                    .OrderByDescending(t => t.CreatedOn)
                    .Take(10)
                    .Select(t => new { t.TopicName, t.Count })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = trending
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // GET /api/Community/circles
        // Returns upcoming healing circles from DB
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpGet("circles")]
        public async Task<IActionResult> GetUpcomingCircles()
        {
            try
            {
                var circles = await _context.HealingCircles
                    .OrderBy(c => c.CreatedOn)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = circles
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // GET /api/Community/discussions
        // Returns community discussions from DB
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpGet("discussions")]
        public async Task<IActionResult> GetDiscussions()
        {
            try
            {
                var discussions = await _context.CommunityDiscussions
                    .OrderByDescending(d => d.IsPinned)
                    .ThenByDescending(d => d.CreatedOn)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = discussions
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
        // ───────────────────────────────────────────────
        // GET /api/Community/summary
        // Returns the 4 top metric cards
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            try
            {
                var activeMembers = await _context.Users.CountAsync();
                var storiesShared = await _context.CommunityPosts.CountAsync(p => !p.IsDeleted);
                var healingCircles = await _context.HealingCircles.CountAsync();

                // Calculate Avg. Bond Growth
                // Baseline score is 50.0 (as defined in Dog model)
                var avgCurrentScore = await _context.Dogs.AverageAsync(d => (double?)d.CurrentScore) ?? 50.0;
                var growthValue = ((avgCurrentScore - 50.0) / 50.0) * 100;
                var growth = growthValue >= 0 ? $"+{growthValue:F1}%" : $"{growthValue:F1}%";

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        activeMembers = activeMembers,
                        storiesShared = storiesShared,
                        healingCircles = healingCircles,
                        avgBondGrowth = growth
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // GET /api/Community/circles/next
        // Returns the closest upcoming healing circle
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpGet("circles/next")]
        public async Task<IActionResult> GetNextCircle()
        {
            try
            {
                // In a real DB, we'd filter by c.Date >= DateTime.UtcNow
                // For now, we take the earliest CreatedOn or just the top 1
                var nextCircle = await _context.HealingCircles
                    .OrderBy(c => c.CreatedOn)
                    .FirstOrDefaultAsync();

                if (nextCircle == null)
                    return NotFound(new { success = false, message = "No upcoming circles." });

                return Ok(new
                {
                    success = true,
                    data = nextCircle
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // POST /api/Community/circles/{id}/join
        // Register user for a circle
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpPost("circles/{id}/join")]
        public async Task<IActionResult> JoinCircle(Guid id)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null) return Unauthorized();

                var circle = await _context.HealingCircles.FindAsync(id);
                if (circle == null) return NotFound(new { message = "Circle not found." });

                // Check if already registered
                var existing = await _context.HealingCircleRegistrations
                    .AnyAsync(r => r.CircleId == id && r.UserId == userId.Value);

                if (existing)
                    return BadRequest(new { success = false, message = "Already registered for this circle." });

                var registration = new HealingCircleRegistration
                {
                    RegistrationId = Guid.NewGuid(),
                    CircleId = id,
                    UserId = userId.Value,
                    RegisteredOn = DateTime.UtcNow
                };

                _context.HealingCircleRegistrations.Add(registration);
                
                // Increment participant count
                circle.ParticipantsCount++;
                _context.HealingCircles.Update(circle);

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Successfully registered for the circle." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // PUT /api/Community/posts/{id}
        // Edit a community post
        // ───────────────────────────────────────────────
        public class EditPostDto
        {
            public string Content { get; set; }
        }

        [Authorize]
        [HttpPut("posts/{id}")]
        public async Task<IActionResult> EditPost(Guid id, [FromBody] EditPostDto dto)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null) return Unauthorized(new { success = false, message = "User ID not found in token." });

                var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.PostId == id && !p.IsDeleted);
                if (post == null) return NotFound(new { success = false, message = "Post not found." });

                if (post.UserId != userId.Value) return Forbid();

                if (string.IsNullOrWhiteSpace(dto?.Content))
                    return BadRequest(new { success = false, message = "Post content cannot be empty." });

                post.Content = dto.Content.Trim();
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Post updated." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // DELETE /api/Community/posts/{id}
        // Delete a community post
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpDelete("posts/{id}")]
        public async Task<IActionResult> DeletePost(Guid id)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null) return Unauthorized(new { success = false, message = "User ID not found in token." });

                var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.PostId == id && !p.IsDeleted);
                if (post == null) return NotFound(new { success = false, message = "Post not found." });

                if (post.UserId != userId.Value) return Forbid();

                post.IsDeleted = true;
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Post deleted." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // PUT /api/Community/comments/{id}
        // Edit a community comment
        // ───────────────────────────────────────────────
        public class EditCommentDto
        {
            public string Content { get; set; }
        }

        [Authorize]
        [HttpPut("comments/{id}")]
        public async Task<IActionResult> EditComment(Guid id, [FromBody] EditCommentDto dto)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null) return Unauthorized(new { success = false, message = "User ID not found in token." });

                var comment = await _context.CommunityComments.FirstOrDefaultAsync(c => c.CommentId == id && !c.IsDeleted);
                if (comment == null) return NotFound(new { success = false, message = "Comment not found." });

                if (comment.UserId != userId.Value) return Forbid();

                if (string.IsNullOrWhiteSpace(dto?.Content))
                    return BadRequest(new { success = false, message = "Comment content cannot be empty." });

                comment.Content = dto.Content.Trim();
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Comment updated." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // DELETE /api/Community/comments/{id}
        // Delete a community comment
        // ───────────────────────────────────────────────
        [Authorize]
        [HttpDelete("comments/{id}")]
        public async Task<IActionResult> DeleteComment(Guid id)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null) return Unauthorized(new { success = false, message = "User ID not found in token." });

                var comment = await _context.CommunityComments.FirstOrDefaultAsync(c => c.CommentId == id && !c.IsDeleted);
                if (comment == null) return NotFound(new { success = false, message = "Comment not found." });

                if (comment.UserId != userId.Value) return Forbid();

                comment.IsDeleted = true;

                // Decrease comment count on the post
                var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.PostId == comment.PostId);
                if (post != null) {
                    post.CommentCount = Math.Max(0, post.CommentCount - 1);
                }

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Comment deleted." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ───────────────────────────────────────────────
        // POST /api/Community/report
        // Report a community post or comment
        // ───────────────────────────────────────────────
        public class ReportDto
        {
            public Guid PostId { get; set; }
            public Guid? CommentId { get; set; }
            public string Reason { get; set; }
        }

        [Authorize]
        [HttpPost("report")]
        public async Task<IActionResult> ReportContent([FromBody] ReportDto dto)
        {
            try
            {
                var userId = GetUserId();
                if (userId == null) return Unauthorized(new { success = false, message = "User ID not found in token." });

                if (string.IsNullOrWhiteSpace(dto?.Reason))
                    return BadRequest(new { success = false, message = "Reason cannot be empty." });

                // 1. Check if post exists
                var postExists = await _context.CommunityPosts.AnyAsync(p => p.PostId == dto.PostId);
                if (!postExists) return BadRequest(new { success = false, message = $"Post not found: {dto.PostId}" });

                // 2. Check if reporter user exists
                var userExists = await _context.Users.AnyAsync(u => u.UserId == userId.Value);
                if (!userExists) return BadRequest(new { success = false, message = $"Reporter user not found: {userId.Value}" });

                // 3. Check if comment exists if provided
        Guid? reportedUserId = null;
        if (dto.CommentId.HasValue) {
            var comment = await _context.CommunityComments.FirstOrDefaultAsync(c => c.CommentId == dto.CommentId.Value);
            if (comment == null) return BadRequest(new { success = false, message = $"Comment not found: {dto.CommentId}" });
            reportedUserId = comment.UserId;
        } else {
            var post = await _context.CommunityPosts.FirstOrDefaultAsync(p => p.PostId == dto.PostId);
            reportedUserId = post?.UserId;
        }

        var report = new PostReport
        {
            ReportId = Guid.NewGuid(),
            PostId = dto.PostId,
            CommentId = dto.CommentId,
            ReporterUserId = userId.Value,
            ReportedUserId = reportedUserId,
            Reason = dto.Reason.Trim(),
            ReportedOn = DateTime.UtcNow,
            Status = "Pending",
            ReportType = "Content",
            Priority = "Medium"
        };

                _context.PostReports.Add(report);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Report submitted successfully." });
            }
            catch (Exception ex)
            {
                var fullError = ex.InnerException != null ? $"{ex.Message} Inner: {ex.InnerException.Message}" : ex.Message;
                return StatusCode(500, new { success = false, message = $"Error: {fullError}" });
            }
        }
    }
}
