using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Data;
using POS.Models;

namespace POS.Services
{
    public class OrderService : IOrderService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<OrderService> _logger;
        private readonly IStockService _stockService;

        public OrderService(ApplicationDbContext context, ILogger<OrderService> logger, IStockService stockService)
        {
            _context = context;
            _logger = logger;
            _stockService = stockService;
        }

        public async Task<IEnumerable<Order>> GetAllOrdersAsync()
        {
            return await _context.Orders
                .Include(o => o.User)
                .Include(o => o.AssignedEmployee)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Order>> GetOrdersByUserIdAsync(string userId)
        {
            return await _context.Orders
                .Include(o => o.AssignedEmployee)
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Order>> GetOrdersByEmployeeIdAsync(string employeeId)
        {
            return await _context.Orders
                .Include(o => o.User)
                .Where(o => o.AssignedToEmployeeId == employeeId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Order>> GetPendingOrdersAsync()
        {
            return await _context.Orders
                .Include(o => o.User)
                .Where(o => o.Status == OrderStatus.Pending)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<Order> GetOrderByIdAsync(int id)
        {
            return await _context.Orders
                .Include(o => o.User)
                .Include(o => o.AssignedEmployee)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task<Order> CreateOrderAsync(Order order)
        {
            // Calculate total price if not set
            if (order.TotalPrice == 0)
            {
                order.TotalPrice = order.Price * order.Quantity;
            }
            
            order.CreatedAt = DateTime.Now;
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            return order;
        }

        public async Task<Order> UpdateOrderAsync(Order order)
        {
            order.UpdatedAt = DateTime.Now;
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return order;
        }

        public async Task<bool> DeleteOrderAsync(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
                return false;

            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Order> AssignOrderToEmployeeAsync(int orderId, string employeeId)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null)
                return null;
                
            order.AssignedToEmployeeId = employeeId;
            order.Status = OrderStatus.OrderReceived;
            order.UpdatedAt = DateTime.Now;
            
            await _context.SaveChangesAsync();
            return order;
        }

        public async Task<Order> UpdateOrderStatusAsync(int orderId, OrderStatus status)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null)
                return null;

            order.Status = status;
            order.UpdatedAt = DateTime.Now;
            
            await _context.SaveChangesAsync();
            return order;
        }
        
        public async Task<IEnumerable<Order>> GetNewOrdersAsync()
        {
            _logger.LogInformation("Fetching new orders...");
            
            try
            {
                // Get all pending orders that aren't assigned to anyone
                var orders = await _context.Orders
                    .Include(o => o.User)
                    .Where(o => o.Status == OrderStatus.Pending && o.AssignedToEmployeeId == null)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();
                
                _logger.LogInformation($"Found {orders.Count} new orders");
                
                return orders;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching new orders");
                return Enumerable.Empty<Order>();
            }
        }
        
        public async Task<IEnumerable<Order>> GetAssignedOrdersAsync(string employeeId)
        {
            return await _context.Orders
                .Include(o => o.User)
                .Where(o => o.AssignedToEmployeeId == employeeId && 
                          (o.Status == OrderStatus.Processing || 
                           o.Status == OrderStatus.OrderReceived || 
                           o.Status == OrderStatus.OnGoing))
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }
        
        public async Task<IEnumerable<Order>> GetOrderHistoryAsync(string employeeId = null)
        {
            var query = _context.Orders
                .Include(o => o.User)
                .Include(o => o.AssignedEmployee)
                .Where(o => o.Status == OrderStatus.Completed || 
                          o.Status == OrderStatus.Complete || 
                          o.Status == OrderStatus.Cancelled);
                
            if (!string.IsNullOrEmpty(employeeId))
            {
                query = query.Where(o => o.AssignedToEmployeeId == employeeId);
            }
            
            return await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }
        
        public async Task<Order> CompleteOrderAsync(int orderId)
        {
            var order = await _context.Orders
                .Include(o => o.User)  // Include user for reference
                .FirstOrDefaultAsync(o => o.Id == orderId);
                
            if (order == null)
                return null;
            
            // Begin transaction to ensure both order status update and stock deduction succeed or fail together
            using var transaction = await _context.Database.BeginTransactionAsync();
            
            try
            {
                // Update order status
                order.Status = OrderStatus.Completed;
                order.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();
                
                // Deduct stock for ingredients based on the product in this order
                await DeductStockForCompletedOrderAsync(order);
                
                // Commit the transaction
                await transaction.CommitAsync();
                _logger.LogInformation($"Order {orderId} marked as completed and stock updated");
                
                return order;
            }
            catch (Exception ex)
            {
                // If anything goes wrong, roll back the transaction
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error completing order and updating stock: {ex.Message}");
                throw;
            }
        }

        // New method to handle stock deduction for completed orders
        private async Task DeductStockForCompletedOrderAsync(Order order)
        {
            // Find the product element based on the order's product name
            // This is a simplified approach - in a real system, you might want to store the product element ID in the order
            var productElement = await _context.PageElements
                .Include(e => e.Ingredients)
                .FirstOrDefaultAsync(e => e.IsProduct && e.ProductName == order.ProductName);
            
            if (productElement == null)
            {
                _logger.LogWarning($"Could not find product element for order {order.Id} with product name '{order.ProductName}'");
                return;
            }
            
            if (productElement.Ingredients == null || !productElement.Ingredients.Any())
            {
                _logger.LogWarning($"Product {productElement.ProductName} does not have any ingredients configured. No stock will be deducted.");
                return;
            }
            
            // Log the ingredients that will be deducted
            foreach (var ingredient in productElement.Ingredients)
            {
                _logger.LogInformation($"Deducting {ingredient.Quantity * order.Quantity} {ingredient.Unit} of {ingredient.IngredientName} for completed order {order.Id}");
            }
            
            // Update stock for all ingredients
            bool stockUpdated = await _stockService.UpdateStockForOrderAsync(
                productElement, 
                order.Quantity, 
                order.UserId
            );
            
            if (!stockUpdated)
            {
                _logger.LogWarning($"Failed to update stock for some ingredients in completed order {order.Id}. Check stock levels and ingredient names.");
            }
            else
            {
                _logger.LogInformation($"Successfully deducted stock for all ingredients in order {order.Id}");
            }
        }

        public async Task<IEnumerable<Order>> GetLatestOrdersAsync(int sinceId = 0)
        {
            return await _context.Orders
                .Include(o => o.User)
                .Where(o => o.Id > sinceId && o.Status == OrderStatus.Pending)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }
        
        // Discount management methods
        public async Task<Order> RequestDiscountAsync(int orderId, string discountType)
        {
            _logger.LogInformation($"Requesting {discountType} discount for order ID: {orderId}");
            
            var order = await _context.Orders
                .Include(o => o.User)
                .FirstOrDefaultAsync(o => o.Id == orderId);
                
            if (order == null)
            {
                _logger.LogWarning($"Order not found: {orderId}");
                return null;
            }
            
            // Set discount request properties
            order.IsDiscountRequested = true;
            order.DiscountType = discountType;
            order.Status = OrderStatus.AwaitingDiscountApproval;
            order.OriginalTotalPrice = order.TotalPrice;
            order.UpdatedAt = DateTime.Now;
            
            await _context.SaveChangesAsync();
            return order;
        }
        
        public async Task<Order> ApproveDiscountAsync(int orderId, string managerId, decimal discountPercentage = 20)
        {
            _logger.LogInformation($"Approving discount for order ID: {orderId} by manager ID: {managerId}");
            
            var order = await _context.Orders
                .Include(o => o.User)
                .FirstOrDefaultAsync(o => o.Id == orderId);
                
            if (order == null)
            {
                _logger.LogWarning($"Order not found: {orderId}");
                return null;
            }
            
            if (!order.IsDiscountRequested)
            {
                _logger.LogWarning($"No discount was requested for order: {orderId}");
                return order;
            }
            
            // Calculate discount
            order.IsDiscountApproved = true;
            order.DiscountApprovedById = managerId;
            order.DiscountPercentage = discountPercentage;
            
            // Store original price if not already stored
            if (order.OriginalTotalPrice <= 0)
            {
                order.OriginalTotalPrice = order.TotalPrice;
            }
            
            // Calculate discount amount
            order.DiscountAmount = Math.Round(order.OriginalTotalPrice * (discountPercentage / 100), 2);
            
            // Update total price with discount
            order.TotalPrice = order.OriginalTotalPrice - order.DiscountAmount;
            
            // Reset status to Pending so customer can proceed with payment
            order.Status = OrderStatus.Pending;
            order.UpdatedAt = DateTime.Now;
            
            await _context.SaveChangesAsync();
            return order;
        }
        
        public async Task<Order> DenyDiscountAsync(int orderId, string managerId)
        {
            _logger.LogInformation($"Denying discount for order ID: {orderId} by manager ID: {managerId}");
            
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId);
                
            if (order == null)
            {
                _logger.LogWarning($"Order not found: {orderId}");
                return null;
            }
            
            // Reset discount properties
            order.IsDiscountRequested = false;
            order.IsDiscountApproved = false;
            order.DiscountType = null;
            order.DiscountAmount = 0;
            order.DiscountPercentage = 0;
            
            // Ensure price is back to original
            if (order.OriginalTotalPrice > 0)
            {
                order.TotalPrice = order.OriginalTotalPrice;
                order.OriginalTotalPrice = 0;
            }
            
            // Reset status to Pending
            order.Status = OrderStatus.Pending;
            order.UpdatedAt = DateTime.Now;
            
            await _context.SaveChangesAsync();
            return order;
        }
        
        public async Task<IEnumerable<Order>> GetOrdersAwaitingDiscountApprovalAsync()
        {
            return await _context.Orders
                .Include(o => o.User)
                .Where(o => o.Status == OrderStatus.AwaitingDiscountApproval)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<Order> CreateOrderAndUpdateStockAsync(Order order, PageElement productElement)
        {
            _logger.LogInformation($"Creating order for product: {productElement.ProductName}");
            
            // Make sure we have a fully loaded element with all its ingredients for validation
            if (productElement.Ingredients == null || !productElement.Ingredients.Any())
            {
                _logger.LogWarning($"Product element didn't have ingredients loaded. Reloading from database.");
                
                // Reload the product element with its ingredients
                var reloadedElement = await _context.PageElements
                    .Include(e => e.Ingredients)
                    .FirstOrDefaultAsync(e => e.Id == productElement.Id);
                    
                if (reloadedElement != null)
                {
                    productElement = reloadedElement;
                    _logger.LogInformation($"Successfully reloaded product element. Found {productElement.Ingredients?.Count ?? 0} ingredients.");
                }
            }
            
            // Check if product element has ingredients after reloading
            if (productElement.Ingredients == null || !productElement.Ingredients.Any())
            {
                _logger.LogWarning($"Product {productElement.ProductName} does not have any ingredients configured. Creating order without ingredient validation.");
                
                // Just create the order without stock validation since there are no ingredients to validate
                return await CreateOrderAsync(order);
            }
            else
            {
                // Log the ingredients for reference (but don't deduct stock yet)
                foreach (var ingredient in productElement.Ingredients)
                {
                    _logger.LogInformation($"Order includes {ingredient.Quantity * order.Quantity} {ingredient.Unit} of {ingredient.IngredientName} (will be deducted when completed)");
                }
                
                // Just create the order without updating stock
                // Stock will be updated when the order is marked as completed
                return await CreateOrderAsync(order);
            }
        }
    }
} 