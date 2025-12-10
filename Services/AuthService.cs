using System.Security.Claims;
using CSE325_visioncoders.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CSE325_visioncoders.Services
{
    public class AuthService
    {
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly UserService _users;
        private readonly NavigationManager _nav;


        public LoginResponse? CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;
        public string? Role => CurrentUser?.Role;
        public string? UserId => CurrentUser?.UserId;

        public event Action? OnChange;

        public AuthService(AuthenticationStateProvider authStateProvider, UserService users, NavigationManager nav)
        {
            _authStateProvider = authStateProvider;
            _users = users;
            _nav = nav;
        }

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

        public void Logout()
        {

            CurrentUser = null;
            OnChange?.Invoke();
            _nav.NavigateTo("/login", forceLoad: true);
        }

        public async Task<bool> RegisterAsync(RegisterRequest request)
        {

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Name))
                return false;

            var existing = await _users.GetByEmailAsync(request.Email);
            if (existing != null)
                return false;

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