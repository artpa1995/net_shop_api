using System.Text;
using Application;
using Domain.Entities;
using FluentValidation;
using FluentValidation.AspNetCore;
using Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddPolicy("web", p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

var key = builder.Configuration["Jwt:Key"] ?? "super-secret-key-super-secret-key";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (ValidationException ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { message = "Validation failed.", errors = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }) });
    }
    catch (UnauthorizedAccessException ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
    catch (Exception ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var dbProvider = (cfg["Database:Provider"] ?? "MySql").Trim();
    // Migrations in this repo are generated for SQL Server; MySQL uses the same model via EnsureCreated for local/dev.
    if (string.Equals(dbProvider, "MySql", StringComparison.OrdinalIgnoreCase))
        db.Database.EnsureCreated();
    else
        db.Database.Migrate();
    if (!db.Users.Any())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Users.Add(new AppUser
        {
            FullName = "Admin User",
            Email = "admin@prodnet.local",
            PasswordHash = hasher.Hash("Admin123!"),
            Phone = "+1-555-9999",
            Address = "Admin Address",
            Age = 30,
            Role = UserRole.Admin
        });
        db.SaveChanges();
    }
}

app.Run();
