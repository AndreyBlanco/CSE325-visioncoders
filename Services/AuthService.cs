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
    }
}
