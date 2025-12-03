namespace CSE325_visioncoders.Models
{
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? Name { get; set; }
        public string? Role { get; set; }
    }
}
