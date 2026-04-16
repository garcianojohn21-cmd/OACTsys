using Microsoft.EntityFrameworkCore;
using OACTsys.Data;
using OACTsys.Services;
using OACTsys.Helpers;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// 🔧 SERVICES CONFIGURATION
// ========================================

// Add MVC
builder.Services.AddControllersWithViews();

// ✅ Enable Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ========================================
// 🗄️ DATABASE (PostgreSQL - Render)
// ========================================

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Optional fallback if using Render ENV variable
if (string.IsNullOrEmpty(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// ========================================
// 📧 EMAIL CONFIGURATION
// ========================================

builder.Services.Configure<SmtpSettings>(
    builder.Configuration.GetSection("SmtpSettings"));

// ========================================
// 🧠 CUSTOM SERVICES
// ========================================

builder.Services.AddScoped<EnrollmentService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<AdmissionFormService>();
builder.Services.AddScoped<EmailAcknowledgementService>();
builder.Services.AddScoped<PaymentConfirmationEmailService>();

// ========================================
// 🌐 HTTP CLIENT (PayMongo, APIs)
// ========================================

builder.Services.AddHttpClient();

// ========================================
// 📌 ACCESSORS
// ========================================

builder.Services.AddHttpContextAccessor();

// ========================================
// 👑 SUPER ADMIN INIT
// ========================================

AdminHelper.Initialize(
    builder.Configuration["SuperAdmin:Username"],
    builder.Configuration["SuperAdmin:Password"],
    builder.Configuration["SuperAdmin:FullName"]
);

// ========================================
// 🚀 BUILD APP
// ========================================

var app = builder.Build();

// ========================================
// ⚙️ MIDDLEWARE PIPELINE
// ========================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// ✅ Session MUST be before Authorization
app.UseSession();

app.UseAuthorization();

// ========================================
// 🧭 ROUTING
// ========================================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ========================================
// 🌱 DATABASE SEEDING
// ========================================

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        // Apply migrations automatically (VERY IMPORTANT for Render)
        context.Database.Migrate();

        // Seed data
        DbSeeder.SeedDatabase(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error during database migration/seeding.");
    }
}

// ========================================
// ▶️ RUN APP
// ========================================

app.Run();