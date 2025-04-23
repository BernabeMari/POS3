using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using POS.Models;
using POS.Services;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POS.Data;

namespace POS.Controllers
{
    [Authorize]
    public class DiscountController : Controller
    {
        private readonly IOrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<DiscountController> _logger;
        private readonly ApplicationDbContext _context;

        public DiscountController(
            IOrderService orderService,
            UserManager<ApplicationUser> userManager,
            ILogger<DiscountController> logger,
            ApplicationDbContext context)
        {
            _orderService = orderService;
            _userManager = userManager;
            _logger = logger;
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> RequestDiscount(int orderId, string discountType)
        {
            _logger.LogInformation($"Discount request for order {orderId}, type: {discountType}");
            
            // Get the order
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning($"Order not found: {orderId}");
                return NotFound(new { success = false, message = "Order not found" });
            }
            
            // Request discount
            var updatedOrder = await _orderService.RequestDiscountAsync(orderId, discountType);
            
            // Check if this is an AJAX request
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { 
                    success = true, 
                    message = "Discount request submitted. Please wait for manager approval.", 
                    order = updatedOrder 
                });
            }
            
            TempData["SuccessMessage"] = "Discount request submitted. Please wait for manager approval.";
            return RedirectToAction("DiscountPending", "Payment", new { orderId = orderId });
        }
        
        [HttpGet]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> PendingApprovals()
        {
            var orders = await _orderService.GetOrdersAwaitingDiscountApprovalAsync();
            return View(orders);
        }
        
        [HttpPost]
        [Authorize(Roles = "Admin,Manager,Employee")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveDiscount(int orderId)
        {
            _logger.LogInformation($"DiscountController.ApproveDiscount called with orderId: {orderId}");
            
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                
                // Check if user has Manager/Admin role OR Manager position
                bool isAuthorized = User.IsInRole("Admin") || User.IsInRole("Manager");
                
                if (!isAuthorized && User.IsInRole("Employee"))
                {
                    // Check if this employee has Manager position
                    var user = await _context.Users
                        .Include(u => u.Position)
                        .FirstOrDefaultAsync(u => u.Id == userId);
                        
                    isAuthorized = user?.Position?.Name == "Manager";
                }
                
                if (!isAuthorized)
                {
                    _logger.LogWarning($"User {userId} is not authorized to approve discounts");
                    return Forbid();
                }
                
                _logger.LogInformation($"Approving discount for order {orderId} by manager {userId}");
                
                // Get the order
                var order = await _orderService.GetOrderByIdAsync(orderId);
                if (order == null)
                {
                    _logger.LogWarning($"Order not found: {orderId}");
                    return NotFound(new { success = false, message = "Order not found" });
                }
                
                // Approve discount (default 20%)
                var updatedOrder = await _orderService.ApproveDiscountAsync(orderId, userId);
                
                if (updatedOrder == null)
                {
                    _logger.LogError($"Failed to approve discount for order {orderId}");
                    return StatusCode(500, new { success = false, message = "Failed to approve discount" });
                }
                
                _logger.LogInformation($"Successfully approved discount for order {orderId}");
                
                // Check if this is an AJAX request
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { 
                        success = true, 
                        message = "Discount approved.", 
                        order = updatedOrder 
                    });
                }
                
                TempData["SuccessMessage"] = "Discount approved.";
                return RedirectToAction("PendingApprovals");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error approving discount for order {orderId}");
                return StatusCode(500, new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }
        
        [HttpPost]
        [Authorize(Roles = "Admin,Manager,Employee")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DenyDiscount(int orderId)
        {
            _logger.LogInformation($"DiscountController.DenyDiscount called with orderId: {orderId}");
            
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                
                // Check if user has Manager/Admin role OR Manager position
                bool isAuthorized = User.IsInRole("Admin") || User.IsInRole("Manager");
                
                if (!isAuthorized && User.IsInRole("Employee"))
                {
                    // Check if this employee has Manager position
                    var user = await _context.Users
                        .Include(u => u.Position)
                        .FirstOrDefaultAsync(u => u.Id == userId);
                        
                    isAuthorized = user?.Position?.Name == "Manager";
                }
                
                if (!isAuthorized)
                {
                    _logger.LogWarning($"User {userId} is not authorized to deny discounts");
                    return Forbid();
                }
                
                _logger.LogInformation($"Denying discount for order {orderId} by manager {userId}");
                
                // Get the order
                var order = await _orderService.GetOrderByIdAsync(orderId);
                if (order == null)
                {
                    _logger.LogWarning($"Order not found: {orderId}");
                    return NotFound(new { success = false, message = "Order not found" });
                }
                
                // Deny discount
                var updatedOrder = await _orderService.DenyDiscountAsync(orderId, userId);
                
                if (updatedOrder == null)
                {
                    _logger.LogError($"Failed to deny discount for order {orderId}");
                    return StatusCode(500, new { success = false, message = "Failed to deny discount" });
                }
                
                _logger.LogInformation($"Successfully denied discount for order {orderId}");
                
                // Check if this is an AJAX request
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { 
                        success = true, 
                        message = "Discount denied.", 
                        order = updatedOrder 
                    });
                }
                
                TempData["SuccessMessage"] = "Discount denied.";
                return RedirectToAction("PendingApprovals");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error denying discount for order {orderId}");
                return StatusCode(500, new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }
        
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> CheckDiscountStatus(int orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                return NotFound(new { success = false, message = "Order not found" });
            }
            
            // Check if order has been approved and needs redirection
            if (order.IsDiscountApproved && order.IsDiscountRequested)
            {
                // Return redirect info in JSON - client side will handle the redirection
                return Json(new { 
                    success = true, 
                    status = order.Status.ToString(),
                    isDiscountRequested = order.IsDiscountRequested,
                    isDiscountApproved = order.IsDiscountApproved,
                    discountAmount = order.DiscountAmount,
                    totalPrice = order.TotalPrice,
                    originalPrice = order.OriginalTotalPrice,
                    redirectMethod = "POST"
                });
            }
            
            return Json(new { 
                success = true, 
                status = order.Status.ToString(),
                isDiscountRequested = order.IsDiscountRequested,
                isDiscountApproved = order.IsDiscountApproved,
                discountAmount = order.DiscountAmount,
                totalPrice = order.TotalPrice,
                originalPrice = order.OriginalTotalPrice
            });
        }
    }
} 