namespace Domain.Entities;

public enum ProductStatus { Pending = 0, Published = 1 }
public enum OrderStatus { Pending = 0, Paid = 1, Shipped = 2, Cancelled = 3 }
public enum UserRole { User = 0, Admin = 1 }

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppUser : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Age { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string MainImageUrl { get; set; } = string.Empty;
    public ProductStatus Status { get; set; } = ProductStatus.Pending;
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public ICollection<ProductImage> Gallery { get; set; } = new List<ProductImage>();
}

public class ProductImage : BaseEntity
{
    public string ImageUrl { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}

public class Order : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public decimal TotalPrice { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string PaymentId { get; set; } = string.Empty;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
