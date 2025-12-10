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
using Microsoft.AspNetCore.HttpOverrides;
using MongoDB.Bson;

var builder = WebApplication.CreateBuilder(args);

// Auth y cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always; // detrás de proxy
    });
builder.Services.AddAuthorization();

// Blazor Server / Razor Components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Encabezados reenviados (proxy)
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// MongoDB settings
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDbSettings"));

// Registro de Mongo en DI (cliente + base)
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var opt = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    if (string.IsNullOrWhiteSpace(opt.ConnectionString))
        throw new InvalidOperationException("MongoDbSettings:ConnectionString no configurado.");
    return new MongoClient(opt.ConnectionString);
});

builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var opt = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var client = sp.GetRequiredService<IMongoClient>();
    var dbName = string.IsNullOrWhiteSpace(opt.DatabaseName)
        ? new MongoUrl(opt.ConnectionString).DatabaseName
        : opt.DatabaseName;
    if (string.IsNullOrWhiteSpace(dbName))
        throw new InvalidOperationException("MongoDbSettings:DatabaseName no configurado.");
    return client.GetDatabase(dbName);
});

// Servicios de la app
builder.Services.AddSingleton<MealService>();
builder.Services.AddSingleton<CalendarService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<IOrderSettingsService, OrderSettingsService>();
builder.Services.AddSingleton<ReviewService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<InventoryService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<CustomerOrdersService>();
builder.Services.AddSingleton<MenuDayService>();
builder.Services.AddSingleton<CSE325_visioncoders.Services.MenuDayService>();
builder.Services.AddHttpContextAccessor();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// MUY ARRIBA: respeta encabezados del proxy
app.UseForwardedHeaders();

// Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Healthcheck (incluye ping a Mongo)
app.MapGet("/healthz", async (IMongoDatabase db) =>
{
    await db.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }");
    return Results.Ok(new { ok = true });
});

// ---------- AUTH APIs ----------
app.MapPost("/api/register", async (RegisterRequest req, UserService userService) =>
{
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

    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
        new Claim(ClaimTypes.Name, user.Name ?? user.Email),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    string redirect = "/homepage";
    if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        redirect = returnUrl!;
    return Results.Redirect(redirect);
})
.DisableAntiforgery();

app.MapPost("/auth/logout", async (HttpContext http, [FromQuery] string? returnUrl) =>
{
    await http.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

    var dest = "/login";
    if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        dest = returnUrl;
    return Results.Redirect(dest);
})
.DisableAntiforgery();

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

// Perfil
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

app.MapPut("/api/profile/me", async (HttpContext http, UpdateProfileRequest req, UserService userService) =>
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

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
