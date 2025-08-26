namespace MarketingSpeedAPI.Models
{
    public class ContactResponse
    {
        public IEnumerable<SocialAccount> social_accounts { get; set; }
        public IEnumerable<DirectContact> direct_contacts { get; set; }
    }

}
