using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hounded_Heart.Models.Dtos
{
    public class User
    {
        [Key]
        public Guid UserId { get; set; }

        [Required, MaxLength(150)]
        public string FullName { get; set; }

        [Required, MaxLength(150)]
        public string Email { get; set; }

        [Required, MaxLength(500)]
        public string? PasswordHash { get; set; }

        public int? RoleId { get; set; }

        public DateTime CreatedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
        public bool IsTermAccepted { get; set; } = false;
        public bool IsGoogleSignIn { get; set; } = false;
        public bool? IsProfileSetupCompleted { get; set; } = false;

        [MaxLength(500)]
        public string? ProfilePhoto { get; set; }

        public string?  ProfileName { get; set; }
        public string Status { get; set; } = "Active";
        public bool IsPremium { get; set; } = false;

        [MaxLength(255)]
        public string? StripeCustomerId { get; set; }

        // Navigation
        //public ICollection<Dog> Dogs { get; set; }
        public Dog Dog { get; set; }
        public ICollection<UserSelectedTrait> SelectedTraits { get; set; }
        public ICollection<UserChakraProgress> ChakraProgresses { get; set; }
        public ICollection<UserCheckIn> UserCheckIns { get; set; }
    }
}
