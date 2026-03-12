using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hounded_Heart.Models.Data
{
    public class ExpertQuestion
    {
        [Key]
        public Guid ExpertQuestionId { get; set; }

        public Guid UserId { get; set; }   // FK to Users table

        [Required, MaxLength(100)]
        public string Name { get; set; }

        [Required, MaxLength(255)]
        public string Email { get; set; }

        [MaxLength(100)]
        public string? CompanionName { get; set; }

        [Required, MaxLength(20)]
        public string Priority { get; set; }   // normal / urgent / high

        [Required, MaxLength(100)]
        public string Category { get; set; }

        [Required, MaxLength(200)]
        public string Subject { get; set; }

        [Required]
        public string Question { get; set; }   

        [Required, MaxLength(20)]
        public string Status { get; set; }     // Pending / Answered / Closed

        [Required]
        public DateTime CreatedOn { get; set; }

        public DateTime? UpdatedOn { get; set; }

        [Required]
        public bool IsActive { get; set; }
    }
}
