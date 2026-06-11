namespace FCatFlasher.Models
{
    internal class CurrentUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsOffline { get; set; }
    }
}
