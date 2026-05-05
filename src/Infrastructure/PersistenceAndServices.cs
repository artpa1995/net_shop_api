using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application;
using Domain.Entities;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Stripe.Checkout;

namespace Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Product>().Property(x => x.Price).HasColumnType("decimal(18,2)");
        b.Entity<Order>().Property(x => x.TotalPrice).HasColumnType("decimal(18,2)");
        b.Entity<OrderItem>().Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
    }
}

public class EfRepository<T>(AppDbContext db) : IRepository<T> where T : BaseEntity
{
    public Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) => db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<List<T>> GetAllAsync(CancellationToken ct = default) => db.Set<T>().ToListAsync(ct);
    public Task<T?> FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken ct = default) => db.Set<T>().FirstOrDefaultAsync(predicate, ct);
    public Task AddAsync(T entity, CancellationToken ct = default) => db.Set<T>().AddAsync(entity, ct).AsTask();
    public void Update(T entity) => db.Set<T>().Update(entity);
    public void Remove(T entity) => db.Set<T>().Remove(entity);
}

public class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public IRepository<AppUser> Users => new EfRepository<AppUser>(db);
    public IRepository<Category> Categories => new EfRepository<Category>(db);
    public IRepository<Product> Products => new EfRepository<Product>(db);
    public IRepository<Order> Orders => new EfRepository<Order>(db);
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class PasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Convert.ToBase64String(KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, 10_000, 32));
        return $"{Convert.ToBase64String(salt)}.{hash}";
    }
    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var hash = Convert.ToBase64String(KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, 10_000, 32));
        return hash == parts[1];
    }
}

public class TokenService(IConfiguration config) : ITokenService
{
    public string CreateToken(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? "super-secret-key-super-secret-key"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class StripeService(IConfiguration config, IUnitOfWork uow) : IStripeService
{
    public async Task<CheckoutResponse> CreateCheckoutSessionAsync(CreateCheckoutRequest request)
    {
        Stripe.StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
        var products = await uow.Products.GetAllAsync();
        var lines = request.Items.Join(products, i => i.ProductId, p => p.Id, (i, p) => new SessionLineItemOptions
        {
            Quantity = i.Quantity,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "usd",
                UnitAmountDecimal = p.Price * 100,
                ProductData = new SessionLineItemPriceDataProductDataOptions { Name = p.Name, Images = new List<string> { p.MainImageUrl } }
            }
        }).ToList();

        var service = new SessionService();
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = request.SuccessUrl + "?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = request.CancelUrl,
            LineItems = lines
        });
        return new CheckoutResponse(session.Id, session.Url ?? "");
    }

    public async Task<string?> VerifySessionAsync(string sessionId)
    {
        Stripe.StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
        var service = new SessionService();
        var session = await service.GetAsync(sessionId);
        return session.PaymentStatus == "paid" ? session.PaymentIntentId : null;
    }
}

public interface IImageStorageService { Task<string> SaveAsync(Stream stream, string fileName, CancellationToken ct = default); }
public class LocalImageStorageService : IImageStorageService
{
    public async Task<string> SaveAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        Directory.CreateDirectory(root);
        var safe = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
        var path = Path.Combine(root, safe);
        await using var fs = File.Create(path);
        await stream.CopyToAsync(fs, ct);
        return $"/uploads/{safe}";
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = (config["Database:Provider"] ?? "MySql").Trim();
        services.AddDbContext<AppDbContext>((_, options) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing or empty. Set it in appsettings.json / appsettings.{Environment}.json, " +
                    "or use environment variable ConnectionStrings__DefaultConnection. " +
                    "If you run the published DLL from bin/, rebuild so appsettings.json is copied, or set the connection string explicitly.");
            }

            if (string.Equals(provider, "MySql", StringComparison.OrdinalIgnoreCase))
            {
                var versionString = config["Database:MySqlServerVersion"] ?? "8.0.21-mysql";
                options.UseMySql(connectionString, ServerVersion.Parse(versionString));
            }
            else
                options.UseSqlServer(connectionString);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IStripeService, StripeService>();
        services.AddScoped<IImageStorageService, LocalImageStorageService>();
        return services;
    }
}
