using Hounded_Heart.Services.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Hounded_Heart.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController : ControllerBase
    {
        private readonly BlobStorageService _blobService;
        private readonly ChakraService _chakraService;

        public AudioController(BlobStorageService blobService, ChakraService chakraService)
        {
            _blobService = blobService;
            _chakraService = chakraService;
        }

        /// <summary>
        /// Upload audio file for a specific chakra
        /// POST /api/audio/upload
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadChakraAudio([FromBody] AudioUploadRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.ChakraType) || string.IsNullOrEmpty(request.Base64Audio))
                return BadRequest("ChakraType and Base64Audio are required.");

            // Validate chakra type
            var validChakras = new[] { "Root", "Sacral", "Solar Plexus", "Heart", "Throat", "Third Eye", "Crown" };
            if (!Array.Exists(validChakras, c => c.Equals(request.ChakraType, StringComparison.OrdinalIgnoreCase)))
                return BadRequest($"Invalid ChakraType. Valid values: {string.Join(", ", validChakras)}");

            try
            {
                // Generate unique filename
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var fileName = $"{request.ChakraType.Replace(" ", "_")}_{timestamp}.mp3";

                // Upload to Azure Blob Storage
                var audioUrl = await _blobService.UploadBase64AudioAsync(request.Base64Audio, fileName);

                if (string.IsNullOrEmpty(audioUrl))
                    return StatusCode(500, "Failed to upload audio to blob storage.");

                // Update Chakra table with audio URL
                var updated = await _chakraService.UpdateChakraAudioUrlAsync(request.ChakraType, audioUrl);

                if (!updated)
                    return NotFound($"Chakra '{request.ChakraType}' not found in database. Please insert chakra data first.");

                return Ok(new
                {
                    message = "Audio uploaded successfully.",
                    chakraType = request.ChakraType,
                    audioUrl = audioUrl,
                    uploadedAt = DateTime.UtcNow
                });
            }
            catch (FormatException ex)
            {
                return BadRequest($"Invalid Base64 format: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error uploading audio: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all chakras with their audio URLs
        /// GET /api/audio/chakras
        /// </summary>
        [HttpGet("chakras")]
        public async Task<IActionResult> GetAllChakras()
        {
            try
            {
                var chakras = await _chakraService.GetAllChakrasAsync();
                return Ok(chakras);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching chakras: {ex.Message}");
            }
        }

        public class AudioUploadRequest
        {
            public string ChakraType { get; set; } // e.g., "Root", "Sacral", etc.
            public string Base64Audio { get; set; } // Base64 encoded audio file
        }
    }
}
