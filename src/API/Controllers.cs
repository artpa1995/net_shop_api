using Application;
using API;
using Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct) => Ok(await authService.RegisterAsync(request, ct));
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) => Ok(await authService.LoginAsync(request, ct));
}

[ApiController]
[Route("api/categories")]
public class CategoriesController(ICategoryService service, IImageStorageService imageStorageService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> Get(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Post(CategoryUpsertDto dto, CancellationToken ct) => Ok(await service.CreateAsync(dto, ct));

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Put(Guid id, CategoryUpsertDto dto, CancellationToken ct) => Ok(await service.UpdateAsync(id, dto, ct));

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await service.DeleteAsync(id, ct); return NoContent(); }

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost("upload-image")]
    public async Task<ActionResult<object>> Upload([FromForm] IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var path = await imageStorageService.SaveAsync(stream, file.FileName, ct);
        return Ok(new { imageUrl = path });
    }
}

[ApiController]
[Route("api/products")]
public class ProductsController(IProductService service, IImageStorageService imageStorageService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ProductDto>>> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 12, [FromQuery] Guid? categoryId = null, CancellationToken ct = default)
        => Ok(await service.GetPublicAsync(page, pageSize, categoryId, ct));

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpGet("admin")]
    public async Task<ActionResult<PagedResponse<ProductDto>>> GetAdmin([FromQuery] int page = 1, [FromQuery] int pageSize = 12, [FromQuery] Guid? categoryId = null, CancellationToken ct = default)
        => Ok(await service.GetAdminAsync(page, pageSize, categoryId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetById(Guid id, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        return Ok(await service.GetByIdAsync(id, isAdmin, ct));
    }

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost]
    public async Task<ActionResult<ProductDto>> Post(ProductUpsertDto dto, CancellationToken ct) => Ok(await service.CreateAsync(dto, ct));

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Put(Guid id, ProductUpsertDto dto, CancellationToken ct) => Ok(await service.UpdateAsync(id, dto, ct));

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await service.DeleteAsync(id, ct); return NoContent(); }

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost("upload-image")]
    public async Task<ActionResult<object>> Upload([FromForm] IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var path = await imageStorageService.SaveAsync(stream, file.FileName, ct);
        return Ok(new { imageUrl = path });
    }
}

[Authorize]
[ApiController]
[Route("api/profile")]
public class ProfileController(IProfileService profileService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get(CancellationToken ct) => Ok(await profileService.GetAsync(User.GetUserId(), ct));
}

[Authorize]
[ApiController]
[Route("api/orders")]
public class OrdersController(IOrderService service) : ControllerBase
{
    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetMine(CancellationToken ct) => Ok(await service.GetMyOrdersAsync(User.GetUserId(), ct));
}

[Authorize(Roles = nameof(UserRole.Admin))]
[ApiController]
[Route("api/admin/orders")]
public class AdminOrdersController(IOrderService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderDto>>> Get(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OrderDto>> UpdateStatus(Guid id, UpdateOrderStatusDto dto, CancellationToken ct) => Ok(await service.UpdateStatusAsync(id, dto.Status, ct));
}

[Authorize]
[ApiController]
[Route("api/payments")]
public class PaymentsController(IStripeService stripeService, IOrderService orderService) : ControllerBase
{
    public record VerifyAndCreateOrderRequest(string SessionId, List<CartItemRequest> Items);

    [HttpPost("create-checkout-session")]
    public async Task<ActionResult<CheckoutResponse>> Create(CreateCheckoutRequest request)
        => Ok(await stripeService.CreateCheckoutSessionAsync(request));

    [HttpPost("verify-and-create-order")]
    public async Task<ActionResult<OrderDto>> VerifyAndCreate(VerifyAndCreateOrderRequest request, CancellationToken ct)
    {
        var paymentId = await stripeService.VerifySessionAsync(request.SessionId);
        if (string.IsNullOrWhiteSpace(paymentId)) return BadRequest(new { message = "Payment is not completed." });
        return Ok(await orderService.CreatePaidOrderAsync(User.GetUserId(), request.Items, paymentId, ct));
    }
}
