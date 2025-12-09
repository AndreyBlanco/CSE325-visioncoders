namespace CSE325_visioncoders.Models
{
    // What we send to the client for the profile
    public class UserProfileDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "customer";
        public string? Phone { get; set; }
        public string? Address { get; set; }
    }

    // What the client sends when updating profile info
    public class UpdateProfileRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Address { get; set; }
    }

    // For changing password
    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
