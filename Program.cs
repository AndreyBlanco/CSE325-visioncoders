using CSE325_visioncoders.Components;
using CSE325_visioncoders.Models;
using CSE325_visioncoders.Services;
using System.Net.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;

using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register existing app services
builder.Services.AddSingleton<MealService>();
builder.Services.AddSingleton<CalendarService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<IOrderSettingsService, OrderSettingsService>();
builder.Services.AddSingleton<ReviewService>();
// MongoDB settings + UserService for auth
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDbSettings"));

builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<InventoryService>();

builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<CustomerOrdersService>();
builder.Services.AddSingleton<MenuDayService>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<ICloudinaryService, CloudinaryService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();
app.MapStaticAssets();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();


// ---------- AUTH APIs ----------

// POST: /api/register
app.MapPost("/api/register", async (RegisterRequest req, UserService userService) =>
{
    // Check if email already exists
    var existing = await userService.GetByEmailAsync(req.Email);
    if (existing != null)
    {
        return Results.BadRequest("Email already exists.");
    }

    var user = new User
    {
        Name = req.Name,
        Email = req.Email,
        PasswordHash = PasswordHasher.Hash(req.Password),
        Role = string.IsNullOrWhiteSpace(req.Role) ? "customer" : req.Role,
        Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone,
        Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address
    };

    await userService.CreateUserAsync(user);

    return Results.Ok(new
    {
        message = "Registered successfully.",
        userId = user.Id,
        user.Name,
        user.Role
    });
});

// POST: /api/login
app.MapPost("/api/login", async (LoginRequest req, UserService userService) =>
{
    var user = await userService.GetByEmailAsync(req.Email);
    if (user == null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
    {
        return Results.BadRequest(new LoginResponse
        {
            Success = false,
            Message = "Invalid email or password."
        });
    }

    return Results.Ok(new LoginResponse
    {
        Success = true,
        Message = "Login successful.",
        UserId = user.Id,
        Name = user.Name,
        Role = user.Role
    });
});

app.MapPost("/auth/login-form", async (HttpContext http,
                                       UserService userService,
                                       [FromForm] LoginRequest req,
                                       [FromQuery] string? returnUrl) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
    {
        var back = "/login?error=missing";
        if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
            back += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.Redirect(back);
    }

    var user = await userService.GetByEmailAsync(req.Email);
    if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
    {
        var back = "/login?error=invalid";
        if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
            back += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.Redirect(back);
    }

    // credenciales OK: crea cookie
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
        new Claim(ClaimTypes.Name, user.Name ?? user.Email),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    // destino por rol
    string redirect = "/homepage";

    // returnUrl relativo tiene prioridad
    if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        redirect = returnUrl!;

    return Results.Redirect(redirect);
})
.DisableAntiforgery();

// POST: /auth/logout
app.MapPost("/auth/logout", async (HttpContext http, [FromQuery] string? returnUrl) =>
{
    await http.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

    // Redirige a login por defecto, o al returnUrl si viene y es relativo
    var dest = "/login";
    if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        dest = returnUrl;
    return Results.Redirect(dest);
})
.DisableAntiforgery();

// GET: /auth/me
app.MapGet("/auth/me", (HttpContext http) =>
{
    var user = http.User;
    if (user?.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    var name = user.FindFirstValue(ClaimTypes.Name) ?? "";
    var email = user.FindFirstValue(ClaimTypes.Email) ?? "";
    var role = user.FindFirstValue(ClaimTypes.Role) ?? "customer";

    return Results.Ok(new
    {
        success = true,
        userId = id,
        name,
        role
    });
});

// GET: /api/profile/me
app.MapGet("/api/profile/me", async (HttpContext http, UserService userService) =>
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

    var user = await userService.GetByIdAsync(userId);
    if (user is null)
        return Results.NotFound();

    var dto = new UserProfileDto
    {
        Id = user.Id ?? string.Empty,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role,
        Phone = user.Phone,
        Address = user.Address
    };

    return Results.Ok(dto);
})
.RequireAuthorization();

// PUT: /api/profile/me  (update name/phone/address)
app.MapPut("/api/profile/me", async (HttpContext http, UpdateProfileRequest req, UserService userService) =>
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

    // Basic validation
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    await userService.UpdateProfileAsync(
        id: userId,
        name: req.Name,
        phone: string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone,
        address: string.IsNullOrWhiteSpace(req.Address) ? null : req.Address
    );

    return Results.Ok(new { message = "Profile updated successfully." });
})
.RequireAuthorization();

// POST: /api/profile/change-password
app.MapPost("/api/profile/change-password", async (HttpContext http, ChangePasswordRequest req, UserService userService) =>
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

    var user = await userService.GetByIdAsync(userId);
    if (user is null)
        return Results.NotFound("User not found.");

    if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
        return Results.BadRequest("Both current and new password are required.");

    if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
        return Results.BadRequest("Current password is incorrect.");

    var newHash = PasswordHasher.Hash(req.NewPassword);
    await userService.UpdatePasswordAsync(userId, newHash);

    return Results.Ok(new { message = "Password changed successfully." });
})
.RequireAuthorization();

app.Run();
