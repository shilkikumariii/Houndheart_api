using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hounded_Heart.Models.DTOs
{
    public class SubmitExpertQuestionDto
    {
        public Guid UserId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string CompanionName { get; set; }
        public string Priority { get; set; }
        public string Category { get; set; }
        public string Subject { get; set; }
        public string Question { get; set; }
    }
}
