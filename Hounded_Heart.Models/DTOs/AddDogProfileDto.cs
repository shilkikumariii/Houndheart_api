using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hounded_Heart.Models.DTOs
{
    public class AddDogProfileDto
    {
        public Guid UserId { get; set; }           // Logged-in user
        public string DogName { get; set; }        // Dog’s name
        public string? DogPhotoUrl { get; set; }
    }
}
