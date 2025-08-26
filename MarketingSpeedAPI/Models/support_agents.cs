namespace MarketingSpeedAPI.Models
{
    public class support_agents
    {
        public int Id { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? BankName { get; set; }
        public string? BankAccountNumber { get; set; }
        public string? Role { get; set; }
        public string? Password_Hash { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

            public ICollection<Conversation> Conversations { get; set; }

    }
}

