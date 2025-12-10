/*
  File: AuthService.cs
  Description: Client-side authentication helper for managing current user state, profile retrieval
               and updates, password change, logout navigation, and registration workflow.
*/

using System.Security.Claims;
using CSE325_visioncoders.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CSE325_visioncoders.Services
{
    /// <summary>
    /// Class: AuthService
    /// Purpose: Manages authentication state, profile access, password changes, logout, and registration.
    /// </summary>
    public class AuthService
    {
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly UserService _users;
        private readonly NavigationManager _nav;

        // State
        public LoginResponse? CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;
        public string? Role => CurrentUser?.Role;
        public string? UserId => CurrentUser?.UserId;

        public event Action? OnChange;

        /// <summary>
        /// Constructor: AuthService
        /// Purpose: Initializes dependencies for auth state, user data access, and navigation.
        /// </summary>
        public AuthService(AuthenticationStateProvider authStateProvider, UserService users, NavigationManager nav)
        {
            _authStateProvider = authStateProvider;
            _users = users;
            _nav = nav;
        }

        // Hydration

        /// <summary>
        /// Function: HydrateAsync
        /// Purpose: Populates CurrentUser from authentication claims without making HTTP calls.
        /// </summary>
        public async Task HydrateAsync()
        {
            var state = await _authStateProvider.GetAuthenticationStateAsync();
            var principal = state.User;

            if (principal?.Identity?.IsAuthenticated == true)
            {
                CurrentUser = new LoginResponse
                {
                    Success = true,
                    UserId = principal.FindFirstValue(ClaimTypes.NameIdentifier),
                    Name = principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue(ClaimTypes.Email),
                    Role = principal.FindFirstValue(ClaimTypes.Role)
                };
            }
            else
            {
                CurrentUser = null;
            }
            OnChange?.Invoke();
        }

        // Profile

        /// <summary>
        /// Function: GetProfileAsync
        /// Purpose: Retrieves the current user's profile directly from the data layer.
        /// </summary>
        public async Task<UserProfileDto?> GetProfileAsync()
        {
            var me = CurrentUser;
            if (me?.UserId is null) return null;

            var user = await _users.GetByIdAsync(me.UserId);
            if (user is null) return null;

            return new UserProfileDto
            {
                Id = user.Id ?? string.Empty,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                Phone = user.Phone,
                Address = user.Address
            };
        }

        /// <summary>
        /// Function: UpdateProfileAsync
        /// Purpose: Updates the current user's profile fields after basic validation.
        /// </summary>
        public async Task<(bool ok, string? error)> UpdateProfileAsync(UpdateProfileRequest req)
        {
            var me = CurrentUser;
            if (me?.UserId is null) return (false, "Not authenticated");
            if (string.IsNullOrWhiteSpace(req.Name)) return (false, "Name is required.");

            await _users.UpdateProfileAsync(
                id: me.UserId,
                name: req.Name,
                phone: string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone,
                address: string.IsNullOrWhiteSpace(req.Address) ? null : req.Address
            );
            return (true, null);
        }

        // Password

        /// <summary>
        /// Function: ChangePasswordAsync
        /// Purpose: Validates current password and updates to a new password for the current user.
        /// </summary>
        public async Task<(bool ok, string? error)> ChangePasswordAsync(ChangePasswordRequest req)
        {
            var me = CurrentUser;
            if (me?.UserId is null) return (false, "Not authenticated");

            var user = await _users.GetByIdAsync(me.UserId);
            if (user is null) return (false, "User not found.");

            if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
                return (false, "Both current and new password are required.");

            if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
                return (false, "Current password is incorrect.");

            var newHash = PasswordHasher.Hash(req.NewPassword);
            await _users.UpdatePasswordAsync(me.UserId, newHash);

            return (true, null);
        }

        // Session

        /// <summary>
        /// Function: Logout
        /// Purpose: Clears the current session state and navigates to the login page.
        /// </summary>
        public void Logout()
        {
            CurrentUser = null;
            OnChange?.Invoke();
            _nav.NavigateTo("/login", forceLoad: true);
        }

        // Registration

        /// <summary>
        /// Function: RegisterAsync
        /// Purpose: Creates a new user account after basic validation and duplicate check.
        /// </summary>
        public async Task<bool> RegisterAsync(RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Name))
                return false;

            // Already exists?
            var existing = await _users.GetByEmailAsync(request.Email);
            if (existing != null)
                return false;

            // Create user
            var user = new User
            {
                Name = request.Name,
                Email = request.Email,
                PasswordHash = PasswordHasher.Hash(request.Password),
                Role = string.IsNullOrWhiteSpace(request.Role) ? "customer" : request.Role,
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone,
                Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address
            };

            await _users.CreateUserAsync(user);
            return true;
        }
    }
}