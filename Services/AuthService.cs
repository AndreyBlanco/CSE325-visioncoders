using System.Net.Http.Json;
using CSE325_visioncoders.Models;
using Microsoft.AspNetCore.Components;

namespace CSE325_visioncoders.Services
{
    public class AuthService
    {
        private readonly HttpClient _http;
        private readonly NavigationManager _nav;

        public LoginResponse? CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;
        public string? Role => CurrentUser?.Role;
        public string? UserId => CurrentUser?.UserId;

        public event Action? OnChange;

        public AuthService(HttpClient http, NavigationManager nav)
        {
            _http = http;
            _nav = nav;

            if (_http.BaseAddress == null)
            {
                _http.BaseAddress = new Uri(_nav.BaseUri);
            }
        }

        public async Task<bool> LoginAsync(string email, string password)
        {
            var request = new LoginRequest { Email = email, Password =  password };

            var response = await _http.PostAsJsonAsync("api/login",     request);

            if (!response.IsSuccessStatusCode)
            {
                CurrentUser = null;
                OnChange?.Invoke();
                return false;
            }

            var loginResponse = await response.Content. ReadFromJsonAsync<LoginResponse>();
            CurrentUser = loginResponse;
            OnChange?.Invoke();
            return loginResponse?.Success == true;
        }

        public async Task<bool> RegisterAsync(RegisterRequest request)
        {
            var response = await _http.PostAsJsonAsync("api/register",  request);
            return response.IsSuccessStatusCode;
        }

        public void Logout()
        {
            CurrentUser = null;
            OnChange?.Invoke();
            _nav.NavigateTo("/login");
        }

        private sealed class CookieLoginResponse
        {
            public bool success { get; set; }
            public string? redirect { get; set; }
        }

        public async Task HydrateAsync()
        {
            try
            {
                var res = await _http.GetAsync("/auth/me");
                if (!res.IsSuccessStatusCode)
                {
                    CurrentUser = null;
                    OnChange?.Invoke();
                    return;
                }

                var me = await res.Content.ReadFromJsonAsync<MeDto>();
                CurrentUser = new LoginResponse
                {
                    Success = true,
                    UserId = me?.userId,
                    Name = me?.name,
                    Role = me?.role
                };
                OnChange?.Invoke();
            }
            catch
            {
                CurrentUser = null;
                OnChange?.Invoke();
            }
        }

        private sealed class MeDto
        {
            public string? userId { get; set; }
            public string? name { get; set; }
            public string? role { get; set; }
        }

        public async Task<(bool ok, string? redirect)> LoginWithCookieAsync(string email, string password, string? returnUrl = null)
        {
            var endpoint = "/auth/login";
            if (!string.IsNullOrEmpty(returnUrl))
                endpoint += $"?returnUrl={Uri.EscapeDataString(returnUrl)}";

            var res = await _http.PostAsJsonAsync(endpoint, new { email, password });
            if (!res.IsSuccessStatusCode)
            {
                CurrentUser = null;
                OnChange?.Invoke();
                return (false, null);
            }

            var payload = await res.Content.ReadFromJsonAsync<CookieLoginResponse>();

            await HydrateAsync();

            return (payload?.success == true, payload?.redirect);
        }

        public async Task LogoutAsync()
        {
            try { await _http.PostAsync("/auth/logout", null); }
            catch { /* ignore */ }

            CurrentUser = null;
            OnChange?.Invoke();
            _nav.NavigateTo("/login", true);
        }
    }
}
