using CSE325_visioncoders.Components;
using CSE325_visioncoders.Models;
using CSE325_visioncoders.Services;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register existing app services
builder.Services.AddSingleton<MealService>();
builder.Services.AddSingleton<CalendarService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<IOrderSettingsService, OrderSettingsService>(); 

// MongoDB settings + UserService for auth
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDbSettings"));

builder.Services.AddSingleton<UserService>();

builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient();  

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
        Role = string.IsNullOrWhiteSpace(req.Role) ? "customer" : req.Role
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

app.Run();
