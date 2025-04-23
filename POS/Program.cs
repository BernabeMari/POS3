using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POS.Data;
using POS.Models;
using POS.Services;

var builder = WebApplication.CreateBuilder(args);

// Add database connection
var connectionString = "workstation id=cybersec-pos.mssql.somee.com;packet size=4096;user id=zyber202003_SQLLogin_1;pwd=3rm4crqfsd;data source=cybersec-pos.mssql.somee.com;persist security info=False;initial catalog=cybersec-pos;TrustServerCertificate=True;Connect Timeout=30;";

// Register connection string with the name "DefaultConnection"
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;

// Add Identity services
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Add custom services
builder.Services.AddScoped<IPageTemplateService, PageTemplateService>();
builder.Services.AddScoped<IPageElementService, PageElementService>();
builder.Services.AddScoped<ILoginAttemptService, LoginAttemptService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IPayPalService, PayPalService>();
builder.Services.AddScoped<ICartService, CartService>();

// Add PayPal configuration
builder.Configuration["PayPal:ClientId"] = "AXX4_-PsWrRCWUkF0PF6tPa12WyNGL3-MtOZlYC6DxFJjwoxUUssSdRfjNd7wFNkGKUdB9oXSq8I6ePr";
builder.Configuration["PayPal:ClientSecret"] = "EHnlE2LugBOJ3KrsqyMz7WPxQdVZenC9_d9QB5Ri62DH2AU3OlKUeZd4gqONSma9xf-EjxnX13Rk-cwi";
   builder.Configuration["PayPal:ReturnUrl"] = "http://localhost:5050/Test/Success";
   builder.Configuration["PayPal:CancelUrl"] = "http://localhost:5050/Test/Cancel";

// Add session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add Razor pages with areas support
builder.Services.AddRazorPages(options =>
{
    // Authorize the Admin area
    options.Conventions.AuthorizeAreaFolder("Admin", "/", "RequireAdministratorRole");
    
    // Authorize the Employee area
    options.Conventions.AuthorizeAreaFolder("Employee", "/", "RequireEmployeeRole");
})
.AddNewtonsoftJson();

// Add Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministratorRole", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireEmployeeRole", policy => policy.RequireRole("Employee"));
});

// Configure cookie policy
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Login";
    options.AccessDeniedPath = "/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Add development-specific middleware
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Use session middleware
app.UseSession();

// Map area routes with explicit mappings
app.MapAreaControllerRoute(
    name: "admin_area",
    areaName: "Admin",
    pattern: "Admin/{controller=Home}/{action=Index}/{id?}");

app.MapAreaControllerRoute(
    name: "user_area",
    areaName: "User",
    pattern: "User/{controller=Home}/{action=Index}/{id?}");

app.MapAreaControllerRoute(
    name: "employee_area",
    areaName: "Employee",
    pattern: "Employee/{controller=Home}/{action=Index}/{id?}");

// Map default controller route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Map Razor Pages
app.MapRazorPages();

// Seed the database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
