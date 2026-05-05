using AutoMapper;
using System.Linq.Expressions;
using Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public record RegisterRequest(string FullName, string Email, string Password, string Phone, string Address, int Age);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, Guid UserId, string FullName, string Email, string Role);

public record CategoryDto(Guid Id, string Name, string ImageUrl);
public record CategoryUpsertDto(string Name, string ImageUrl);

public record ProductImageDto(Guid Id, string ImageUrl);
public record ProductDto(Guid Id, string Name, string Description, decimal Price, string MainImageUrl, string CategoryName, string Status, List<ProductImageDto> Gallery);
public record ProductUpsertDto(string Name, string Description, decimal Price, string MainImageUrl, Guid CategoryId, List<string> GalleryUrls, string Status);

public record PagedResponse<T>(IEnumerable<T> Items, int Page, int PageSize, int TotalItems);

public record CartItemRequest(Guid ProductId, int Quantity);
public record CreateCheckoutRequest(List<CartItemRequest> Items, string SuccessUrl, string CancelUrl);
public record CheckoutResponse(string SessionId, string CheckoutUrl);

public record OrderItemDto(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);
public record OrderDto(Guid Id, string UserName, string UserEmail, decimal TotalPrice, string Status, string PaymentId, IEnumerable<OrderItemDto> Items);
public record UpdateOrderStatusDto(string Status);

public record ProfileDto(Guid Id, string FullName, string Email, string Phone, string Address, int Age, IEnumerable<OrderDto> Orders);

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<T>> GetAllAsync(CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
}

public interface IUnitOfWork
{
    IRepository<AppUser> Users { get; }
    IRepository<Category> Categories { get; }
    IRepository<Product> Products { get; }
    IRepository<Order> Orders { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IPasswordHasher { string Hash(string password); bool Verify(string password, string hash); }
public interface ITokenService { string CreateToken(AppUser user); }
public interface IStripeService { Task<CheckoutResponse> CreateCheckoutSessionAsync(CreateCheckoutRequest request); Task<string?> VerifySessionAsync(string sessionId); }

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}

public interface ICategoryService
{
    Task<IEnumerable<CategoryDto>> GetAllAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CategoryUpsertDto dto, CancellationToken ct = default);
    Task<CategoryDto> UpdateAsync(Guid id, CategoryUpsertDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IProductService
{
    Task<PagedResponse<ProductDto>> GetPublicAsync(int page, int pageSize, Guid? categoryId, CancellationToken ct = default);
    Task<PagedResponse<ProductDto>> GetAdminAsync(int page, int pageSize, Guid? categoryId, CancellationToken ct = default);
    Task<ProductDto> GetByIdAsync(Guid id, bool adminView, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(ProductUpsertDto dto, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(Guid id, ProductUpsertDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IOrderService
{
    Task<IEnumerable<OrderDto>> GetMyOrdersAsync(Guid userId, CancellationToken ct = default);
    Task<IEnumerable<OrderDto>> GetAllAsync(CancellationToken ct = default);
    Task<OrderDto> UpdateStatusAsync(Guid orderId, string status, CancellationToken ct = default);
    Task<OrderDto> CreatePaidOrderAsync(Guid userId, List<CartItemRequest> items, string paymentId, CancellationToken ct = default);
}

public interface IProfileService { Task<ProfileDto> GetAsync(Guid userId, CancellationToken ct = default); }

public class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MinimumLength(6);
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Address).NotEmpty();
        RuleFor(x => x.Age).GreaterThanOrEqualTo(18).WithMessage("Registration is allowed only for users aged 18 and above.");
    }
}

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Category, CategoryDto>();
        CreateMap<ProductImage, ProductImageDto>();
        CreateMap<Product, ProductDto>()
            .ForCtorParam("CategoryName", m => m.MapFrom(s => s.Category != null ? s.Category.Name : ""))
            .ForCtorParam("Status", m => m.MapFrom(s => s.Status.ToString()));
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());
        services.AddScoped<IValidator<RegisterRequest>, RegisterValidator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IProfileService, ProfileService>();
        return services;
    }
}

public class AuthService(IUnitOfWork uow, IPasswordHasher hasher, ITokenService tokenService, IValidator<RegisterRequest> registerValidator) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        await registerValidator.ValidateAndThrowAsync(request, ct);
        var existing = await uow.Users.FirstOrDefaultAsync(x => x.Email == request.Email, ct);
        if (existing is not null) throw new InvalidOperationException("Email already exists.");

        var user = new AppUser
        {
            FullName = request.FullName,
            Email = request.Email.ToLowerInvariant(),
            PasswordHash = hasher.Hash(request.Password),
            Phone = request.Phone,
            Address = request.Address,
            Age = request.Age
        };

        await uow.Users.AddAsync(user, ct);
        await uow.SaveChangesAsync(ct);
        var token = tokenService.CreateToken(user);
        return new AuthResponse(token, user.Id, user.FullName, user.Email, user.Role.ToString());
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var emailLower = request.Email.ToLowerInvariant();
        var user = await uow.Users.FirstOrDefaultAsync(x => x.Email == emailLower, ct)
                   ?? throw new UnauthorizedAccessException("Invalid credentials.");
        if (!hasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials.");
        return new AuthResponse(tokenService.CreateToken(user), user.Id, user.FullName, user.Email, user.Role.ToString());
    }
}

public class CategoryService(IUnitOfWork uow, IMapper mapper) : ICategoryService
{
    public async Task<IEnumerable<CategoryDto>> GetAllAsync(CancellationToken ct = default) => mapper.Map<IEnumerable<CategoryDto>>(await uow.Categories.GetAllAsync(ct));
    public async Task<CategoryDto> CreateAsync(CategoryUpsertDto dto, CancellationToken ct = default)
    {
        var entity = new Category { Name = dto.Name, ImageUrl = dto.ImageUrl };
        await uow.Categories.AddAsync(entity, ct); await uow.SaveChangesAsync(ct);
        return mapper.Map<CategoryDto>(entity);
    }
    public async Task<CategoryDto> UpdateAsync(Guid id, CategoryUpsertDto dto, CancellationToken ct = default)
    {
        var entity = await uow.Categories.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Category not found.");
        entity.Name = dto.Name; entity.ImageUrl = dto.ImageUrl; uow.Categories.Update(entity); await uow.SaveChangesAsync(ct);
        return mapper.Map<CategoryDto>(entity);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await uow.Categories.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Category not found.");
        uow.Categories.Remove(entity); await uow.SaveChangesAsync(ct);
    }
}

public class ProductService(IUnitOfWork uow, IMapper mapper) : IProductService
{
    public Task<PagedResponse<ProductDto>> GetPublicAsync(int page, int pageSize, Guid? categoryId, CancellationToken ct = default)
        => BuildPageAsync(page, pageSize, categoryId, false, ct);
    public Task<PagedResponse<ProductDto>> GetAdminAsync(int page, int pageSize, Guid? categoryId, CancellationToken ct = default)
        => BuildPageAsync(page, pageSize, categoryId, true, ct);

    private async Task<PagedResponse<ProductDto>> BuildPageAsync(int page, int pageSize, Guid? categoryId, bool admin, CancellationToken ct)
    {
        var all = await uow.Products.GetAllAsync(ct);
        var query = all.AsQueryable();
        if (!admin) query = query.Where(x => x.Status == ProductStatus.Published);
        if (categoryId.HasValue) query = query.Where(x => x.CategoryId == categoryId.Value);
        var total = query.Count();
        var pageData = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResponse<ProductDto>(mapper.Map<List<ProductDto>>(pageData), page, pageSize, total);
    }

    public async Task<ProductDto> GetByIdAsync(Guid id, bool adminView, CancellationToken ct = default)
    {
        var p = await uow.Products.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Product not found.");
        if (!adminView && p.Status != ProductStatus.Published) throw new KeyNotFoundException("Product not found.");
        return mapper.Map<ProductDto>(p);
    }
    public async Task<ProductDto> CreateAsync(ProductUpsertDto dto, CancellationToken ct = default)
    {
        var status = Enum.TryParse<ProductStatus>(dto.Status, true, out var s) ? s : ProductStatus.Pending;
        var p = new Product
        {
            Name = dto.Name, Description = dto.Description, Price = dto.Price,
            MainImageUrl = dto.MainImageUrl, CategoryId = dto.CategoryId, Status = status,
            Gallery = dto.GalleryUrls.Select(x => new ProductImage { ImageUrl = x }).ToList()
        };
        await uow.Products.AddAsync(p, ct); await uow.SaveChangesAsync(ct); return mapper.Map<ProductDto>(p);
    }
    public async Task<ProductDto> UpdateAsync(Guid id, ProductUpsertDto dto, CancellationToken ct = default)
    {
        var p = await uow.Products.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Product not found.");
        p.Name = dto.Name; p.Description = dto.Description; p.Price = dto.Price; p.MainImageUrl = dto.MainImageUrl; p.CategoryId = dto.CategoryId;
        p.Status = Enum.TryParse<ProductStatus>(dto.Status, true, out var s) ? s : ProductStatus.Pending;
        p.Gallery = dto.GalleryUrls.Select(x => new ProductImage { ProductId = p.Id, ImageUrl = x }).ToList();
        uow.Products.Update(p); await uow.SaveChangesAsync(ct); return mapper.Map<ProductDto>(p);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await uow.Products.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Product not found.");
        uow.Products.Remove(p); await uow.SaveChangesAsync(ct);
    }
}

public class OrderService(IUnitOfWork uow) : IOrderService
{
    public async Task<OrderDto> CreatePaidOrderAsync(Guid userId, List<CartItemRequest> items, string paymentId, CancellationToken ct = default)
    {
        var products = await uow.Products.GetAllAsync(ct);
        var selected = items.Join(products, i => i.ProductId, p => p.Id, (i, p) => new { i, p }).ToList();
        var total = selected.Sum(x => x.i.Quantity * x.p.Price);
        var order = new Order
        {
            UserId = userId, PaymentId = paymentId, Status = OrderStatus.Paid, TotalPrice = total,
            Items = selected.Select(x => new OrderItem { ProductId = x.p.Id, Quantity = x.i.Quantity, UnitPrice = x.p.Price }).ToList()
        };
        await uow.Orders.AddAsync(order, ct); await uow.SaveChangesAsync(ct);
        return await MapOrderAsync(order, ct);
    }
    public async Task<IEnumerable<OrderDto>> GetAllAsync(CancellationToken ct = default) => (await uow.Orders.GetAllAsync(ct)).Select(MapOrder);
    public async Task<IEnumerable<OrderDto>> GetMyOrdersAsync(Guid userId, CancellationToken ct = default) => (await uow.Orders.GetAllAsync(ct)).Where(x => x.UserId == userId).Select(MapOrder);
    public async Task<OrderDto> UpdateStatusAsync(Guid orderId, string status, CancellationToken ct = default)
    {
        var order = await uow.Orders.GetByIdAsync(orderId, ct) ?? throw new KeyNotFoundException("Order not found.");
        order.Status = Enum.TryParse<OrderStatus>(status, true, out var s) ? s : order.Status;
        uow.Orders.Update(order); await uow.SaveChangesAsync(ct); return MapOrder(order);
    }
    private Task<OrderDto> MapOrderAsync(Order order, CancellationToken _) => Task.FromResult(MapOrder(order));
    private static OrderDto MapOrder(Order x) => new(x.Id, x.User?.FullName ?? "", x.User?.Email ?? "", x.TotalPrice, x.Status.ToString(), x.PaymentId,
        x.Items.Select(i => new OrderItemDto(i.ProductId, i.Product?.Name ?? "", i.Quantity, i.UnitPrice)));
}

public class ProfileService(IUnitOfWork uow, IOrderService orderService) : IProfileService
{
    public async Task<ProfileDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await uow.Users.GetByIdAsync(userId, ct) ?? throw new KeyNotFoundException("User not found.");
        var orders = await orderService.GetMyOrdersAsync(userId, ct);
        return new ProfileDto(user.Id, user.FullName, user.Email, user.Phone, user.Address, user.Age, orders);
    }
}
