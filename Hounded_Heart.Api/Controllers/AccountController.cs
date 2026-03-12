using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.Dtos;
using Hounded_Heart.Models.DTOs;
using Hounded_Heart.Models.Models;
using Hounded_Heart.Services.ServiceResult;
using Hounded_Heart.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using static Azure.Core.HttpHeader;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly BlobStorageService _blobService;
        private readonly AuthService _authService;
        private readonly ChangePasswordService _changePassword;
        public AccountController(AppDbContext context, IConfiguration configuration, BlobStorageService blobService,
            AuthService authService ,ChangePasswordService changePassword)
        {
            _context = context;
            _configuration = configuration;
            _blobService = blobService;
            _authService = authService;
            _changePassword = changePassword;
        }
        #region Add User
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterAccountDto dto)
        {
            try
            {
                if (_context.Users.Any(u => u.Email.ToLower() == dto.Email.ToLower()))
                {
                    return BadRequest(ResponseHelper.Fail<string>("User already exists with this email.", 400));
                }

                if (!dto.IsTermsAccepted)
                {
                    return BadRequest(ResponseHelper.Fail<string>("You must accept the terms and conditions to register.", 400));
                }

                //if (dto.Password != dto.ConfirmPassword)
                //{
                //    return BadRequest(ResponseHelper.Fail<string>("Password and Confirm Password do not match.", 400));
                //}

                var user = new User
                {
                    UserId = Guid.NewGuid(),
                    Email = dto.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    FullName = dto.FullName,
                    RoleId = 2,
                    IsActive = true,
                    IsDeleted = false,
                    IsTermAccepted = dto.IsTermsAccepted,
                    CreatedOn = DateTime.UtcNow,
                    UpdatedOn = DateTime.UtcNow
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                var token = GenerateJwtToken(user.UserId, user.Email);
                return Ok(ResponseHelper.Success(new
                {
                    UserId = user.UserId,
                    Token = token,
                }, "User registered successfully", 200));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ResponseHelper.Fail<string>($"Registration failed: {ex.Message}", 500));
            }
        }

        #endregion

        [HttpPost("add-userprofile")]
        public async Task<IActionResult> AddUserProfile([FromBody] AddUserProfileDto dto)
        {
            try
            {
                if (dto.UserId == Guid.Empty)
                    return BadRequest(ResponseHelper.Fail<string>("UserId is required.", 400));

                var user = await _context.Users.FindAsync(dto.UserId);
                if (user == null)
                    return NotFound(ResponseHelper.Fail<string>("User not found.", 404));

                // Update name
                if (!string.IsNullOrEmpty(dto.ProfileName))
                    user.ProfileName = dto.ProfileName;

                // Save blob URL
                if (!string.IsNullOrEmpty(dto.ProfilePhotoUrl))
                {
                    var blobUrl = await _blobService.UploadBase64ImageAsync(dto.ProfilePhotoUrl, $"{dto.UserId}.jpg");
                    user.ProfilePhoto = blobUrl;
                }

                user.UpdatedOn = DateTime.UtcNow;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok(ResponseHelper.Success(new
                {
                    UserId = user.UserId,
                    ProfileName = user.FullName,
                    ProfilePhoto = user.ProfilePhoto
                }, "Profile completed successfully.", 200));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ResponseHelper.Fail<string>($"Profile update failed: {ex.Message}", 500));
            }
        }

        [HttpPost("add-dogprofile")]
        public async Task<IActionResult> AddDog([FromBody] AddDogProfileDto dto)
        {
            try
            {
                if (dto.UserId == Guid.Empty)
                    return BadRequest(ResponseHelper.Fail<string>("UserId is required.", 400));

                if (string.IsNullOrWhiteSpace(dto.DogName))
                    return BadRequest(ResponseHelper.Fail<string>("Dog name is required.", 400));

                var userExists = await _context.Users.AnyAsync(u => u.UserId == dto.UserId);
                if (!userExists)
                    return NotFound(ResponseHelper.Fail<string>("User not found.", 404));

                var existingDog = await _context.Dogs.FirstOrDefaultAsync(d => d.UserId == dto.UserId);

                string? blobUrl = null;
                if (!string.IsNullOrEmpty(dto.DogPhotoUrl))
                {
                    blobUrl = await _blobService.UploadBase64ImageAsync(dto.DogPhotoUrl, $"Dog_{dto.UserId}.jpg");
                }

                if (existingDog != null)
                {
                    existingDog.DogName = dto.DogName;
                    if (blobUrl != null) existingDog.ProfilePhoto = blobUrl;
                    existingDog.UpdatedOn = DateTime.UtcNow;
                    _context.Dogs.Update(existingDog);
                }
                else
                {
                    existingDog = new Dog
                    {
                        DogId = Guid.NewGuid(),
                        UserId = dto.UserId,
                        DogName = dto.DogName,
                        ProfilePhoto = blobUrl,
                        CreatedOn = DateTime.UtcNow,
                        UpdatedOn = DateTime.UtcNow
                    };
                    _context.Dogs.Add(existingDog);
                }

                await _context.SaveChangesAsync();

                return Ok(ResponseHelper.Success(new
                {
                    existingDog.DogId,
                    existingDog.DogName,
                    existingDog.ProfilePhoto
                }, "Dog profile saved successfully.", 200));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ResponseHelper.Fail<string>($"Failed to save dog profile: {ex.Message}", 500));
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                {
                    return BadRequest(ResponseHelper.Fail<string>("Email and Password are required", 400));
                }
                var user = _context.Users
                    .Where(x => x.Email.ToLower() == dto.Email.ToLower() && !x.IsDeleted && x.IsActive)
                    .FirstOrDefault();
                if (user == null)
                {
                    return NotFound(ResponseHelper.Fail<string>($"Invalid email or user not found: {dto.Email}", 404));
                }
                if (user.IsGoogleSignIn)
                {
                    return NotFound(ResponseHelper.Fail<object>(
                        "This account is registered via Google Sign-In. Please use Google to log in."
                    ));
                }

                bool isPasswordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);

                if (!isPasswordValid)
                {
                    return Unauthorized(ResponseHelper.Fail<string>("Invalid password", 401));
                }

                if (user.Status == "Suspended")
                {
                    return StatusCode(403, ResponseHelper.Fail<object>("Your account is suspended. Please contact support."));
                }
                if (user.Status == "Banned")
                {
                    return StatusCode(403, ResponseHelper.Fail<object>("Your account is banned."));
                }
                var token = GenerateJwtToken(user.UserId, user.Email);
                var response = new
                {
                    Token = token,
                    UserId = user.UserId,
                    Email = user.Email,
                    RoleId = user.RoleId,
                };
                return Ok(ResponseHelper.Success(response, " Login Successful", 200));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ResponseHelper.Fail<string>($"Login failed: {ex.Message}", 500));
            }
        }

        [HttpGet("profile/{userId}")]
        public async Task<IActionResult> GetUserProfile(Guid userId)
        {
            var user = await _context.Users
                .Include(u => u.Dog)
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted);
            if (user == null)
                return NotFound(new { Message = "User not found." });
            var userTraits = await _context.UserSelectedTraits
        .Where(t => t.UserId == userId)
        .Include(t => t.Trait)
        .Select(t => new UserTraitDto
        {
            TraitId = t.TraitId,
            TraitName = t.Trait.TraitName
        })
        .ToListAsync();
            var now = DateTime.UtcNow;
            var journalEntryCount = await _context.JournalEntries
                .Where(x => x.UserId == userId
                    && x.CreatedOn.Year == now.Year
                    && x.CreatedOn.Month == now.Month
                    && !x.IsDeleted)
                .CountAsync();
            //var journalEntryCount = await _context.JournalEntry.Where(x => x.UserId == userId).CountAsync();
            List<DogTraitDto> dogTraits = new List<DogTraitDto>();
            if (user.Dog != null)
            {
                dogTraits = await _context.DogSelectedTraits
                    .Where(t => t.UserId == userId)
                    .Include(t => t.Trait)
                    .Select(t => new DogTraitDto
                    {
                        TraitId = t.TraitId,
                        TraitName = t.Trait.TraitName
                    })
                    .ToListAsync();
            }

            var profile = new UserProfileDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                ProfilePhoto = user.ProfilePhoto,
                ProfileName = user.ProfileName,
                IsProfileSetupCompleted = user.IsProfileSetupCompleted,
                JournalEntryCount= journalEntryCount,
                IsGoogleSignIn =user.IsGoogleSignIn,
                Dog = user.Dog == null ? null : new DogDto
                {
                    DogId = user.Dog.DogId,
                    DogName = user.Dog.DogName,
                    ProfilePhoto = user.Dog.ProfilePhoto
                },
                UserSelectedTraits = userTraits,
                DogSelectedTraits = dogTraits
            };

            return Ok(profile);
        }


        [HttpPost("Google-LoginSignup")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
        {
            try
            {
                var result = await _authService.GoogleLoginAsync(dto);
                return StatusCode(result.StatusCode, result);
            }
            catch (Exception)
            {
                return StatusCode(500, ResponseHelper.Fail<string>("Unexpected error occurred.", 500));
            }
        }

        [HttpPost("MailSendchangespassword")]
        public async Task<IActionResult> MailSendchangespassword( EmailSendModel emailSendModel)
        {
            var result = await _changePassword.SendMailchangepassword(emailSendModel);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("VerifyOtp")]
        public async Task<IActionResult> VerifyOtp(VerifyOtpModel model)
        {
            var otpRecord = await _context.UserOtps
                .Where(x => x.Email == model.Email)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (otpRecord == null)
                return StatusCode(404, ResponseHelper.Fail<string>("OTP not found", 404));

            if (otpRecord.IsUsed)
                return StatusCode(400, ResponseHelper.Fail<string>("OTP already used", 400));

            if (otpRecord.ExpiryTime < DateTime.UtcNow)
                return StatusCode(400, ResponseHelper.Fail<string>("OTP expired", 400));

            if (otpRecord.OtpCode != model.OtpCode)
                return StatusCode(400, ResponseHelper.Fail<string>("Invalid OTP", 400));

            otpRecord.IsUsed = true;
            await _context.SaveChangesAsync();

            return StatusCode(200, ResponseHelper.Success<string>(null, "OTP verified successfully", 200));
        }
        [HttpPost("update-password")]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordModel dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(ResponseHelper.Fail<string>("Invalid request.", 400));
                }

                if (string.IsNullOrWhiteSpace(dto.Email))
                {
                    return BadRequest(ResponseHelper.Fail<string>("Email is required.", 400));
                }

                if (string.IsNullOrWhiteSpace(dto.NewPassword) ||
                    string.IsNullOrWhiteSpace(dto.ConfirmPassword))
                {
                    return BadRequest(ResponseHelper.Fail<string>("New password and confirm password are required.", 400));
                }

                if (dto.NewPassword != dto.ConfirmPassword)
                {
                    return BadRequest(ResponseHelper.Fail<string>("Passwords do not match.", 400));
                }

                var user = await _context.Users
                    .Where(x => x.Email.ToLower() == dto.Email.ToLower() && !x.IsDeleted)
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    return NotFound(ResponseHelper.Fail<string>("User not found.", 404));
                }

                // Update password
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
                user.UpdatedOn = DateTime.UtcNow;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok(ResponseHelper.Success<string>(null, "Password updated successfully", 200));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ResponseHelper.Fail<string>($"Password update failed: {ex.Message}", 500));
            }
        }
        [HttpPost("changepassword")]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequestModel request)
        {

            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.NewPassword))
                {
                    return BadRequest(ResponseHelper.Fail<string>("Invalid password change request.", 400));
                }

                // Fetch user by Email or UserId
                var userdetails = await _context.Users
                    .Where(x => x.UserId == request.UserId || x.Email.ToLower() == request.Email.ToLower())
                    .FirstOrDefaultAsync();

                if (userdetails == null)
                {
                    return NotFound(ResponseHelper.Fail<string>("User not found.", 404));
                }
                // Validate Current Password if provided
                if (!string.IsNullOrWhiteSpace(request.CurrentPassword))
                {
                    bool isPasswordValid = BCrypt.Net.BCrypt.Verify(request.CurrentPassword, userdetails.PasswordHash);

                    if (!isPasswordValid)
                    {
                        return StatusCode(400, ResponseHelper.Fail<string>("Current password is incorrect.", 400));
                    }
                }

                // Hash new password before saving
                userdetails.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                userdetails.UpdatedOn = DateTime.UtcNow;

                _context.Users.Update(userdetails);
                await _context.SaveChangesAsync();


                return StatusCode(200, ResponseHelper.Success<string>(null, "Password changed successfully.", 200));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ResponseHelper.Fail<string>($"Password change failed: {ex.Message}", 500));
            }
        }

        [HttpPost("setup-profile")]
        public async Task<IActionResult> SetupProfile([FromBody] UserAndDogProfileDto dto)
        {
            if (dto == null || dto.UserId == Guid.Empty)
                return BadRequest(ResponseHelper.Fail<string>("UserId is required.", 400));

            try
            {
                var user = await _context.Users
                    .Include(u => u.Dog)
                    .FirstOrDefaultAsync(u => u.UserId == dto.UserId && !u.IsDeleted);

                if (user == null)
                    return NotFound(ResponseHelper.Fail<string>("User not found.", 404));

                // === Update only provided user fields ===
                if (!string.IsNullOrWhiteSpace(dto.ProfileName))
                    user.ProfileName = dto.ProfileName.Trim();

                if (!string.IsNullOrWhiteSpace(dto.Email))
                    user.Email = dto.Email.Trim();

                if (!string.IsNullOrEmpty(dto.Base64Image))
                {
                    var fileName = $"user_{user.UserId}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
                    var blobUrl = await _blobService.UploadBase64ImageAsync(dto.Base64Image, fileName);
                    user.ProfilePhoto = blobUrl;
                }
                user.UpdatedOn = DateTime.UtcNow;
                // === Handle Dog info only if provided ===
                if (!string.IsNullOrWhiteSpace(dto.DogName) || !string.IsNullOrEmpty(dto.DogBase64Image))
                {
                    var dog = user.Dog;

                    if (dog == null)
                    {
                        dog = new Dog
                        {
                            DogId = Guid.NewGuid(),
                            UserId = user.UserId,
                            DogName = dto.DogName ?? "My Dog",
                            CreatedOn = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        };
                        _context.Dogs.Add(dog);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(dto.DogName))
                            dog.DogName = dto.DogName.Trim();

                        dog.UpdatedOn = DateTime.UtcNow;
                    }

                    if (!string.IsNullOrEmpty(dto.DogBase64Image))
                    {
                        var dogFileName = $"dog_{user.UserId}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
                        var dogBlobUrl = await _blobService.UploadBase64ImageAsync(dto.DogBase64Image, dogFileName);
                        dog.ProfilePhoto = dogBlobUrl;
                    }
                }
                await _context.SaveChangesAsync();
                var response = new
                {
                    UserId = user.UserId,
                    ProfileName = user.ProfileName,
                    Email = user.Email,
                    ProfilePhoto = user.ProfilePhoto,
                    Dog = user.Dog == null ? null : new
                    {
                        DogId = user.Dog.DogId,
                        DogName = user.Dog.DogName,
                        DogProfilePhoto = user.Dog.ProfilePhoto
                    }
                };

                return Ok(ResponseHelper.Success(response, "Profile updated successfully", 200));
            }
            catch (FormatException ex)
            {
                return BadRequest(ResponseHelper.Fail<string>($"Invalid image format: {ex.Message}", 400));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ResponseHelper.Fail<string>($"Profile update failed: {ex.Message}", 500));
            }
        }


        [HttpGet("user-details/{userId}")]
        public async Task<IActionResult> GetUserDetails(Guid userId)
        {
            try
            {
                var user = await _context.Users
                    .Where(u => u.UserId == userId && !u.IsDeleted && u.IsActive)
                    .Select(u => new
                    {
                        u.UserId,
                        u.FullName,
                        u.Email,
                        u.ProfileName,
                        u.ProfilePhoto,
                        u.RoleId,
                        u.IsProfileSetupCompleted,
                        u.IsGoogleSignIn,
                        u.CreatedOn,
                        u.UpdatedOn
                    })
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    return NotFound(ResponseHelper.Fail<string>("User not found.", 404));
                }

                return Ok(ResponseHelper.Success(user, "User details retrieved successfully.", 200));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ResponseHelper.Fail<string>($"Failed to retrieve user details: {ex.Message}", 500));
            }
        }

        private string GenerateJwtToken(Guid id, string emailAddress)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),     // ✅ gives you user ID from token
                new Claim(ClaimTypes.Email, emailAddress),               // ✅ allows accessing user's email
                new Claim(ClaimTypes.Name, emailAddress)                 // (optional) for User.Identity.Name
            };
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(double.Parse(_configuration["Jwt:DurationInHours"] ?? "8")),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"]
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

    }
}
