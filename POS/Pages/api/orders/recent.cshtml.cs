using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using POS.Models;
using POS.Services;
using System.Security.Claims;
using System.Text.Json;

namespace POS.Pages.api.orders
{
    [Authorize(Roles = "Employee,Admin")]
    public class RecentModel : PageModel
    {
        private readonly IOrderService _orderService;
        private readonly ILogger<RecentModel> _logger;

        public RecentModel(IOrderService orderService, ILogger<RecentModel> logger)
        {
            _orderService = orderService;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // Check if the user is authenticated before proceeding
            if (!User.Identity.IsAuthenticated)
            {
                Response.StatusCode = 401; // Unauthorized
                return new JsonResult(new { error = "Unauthorized access" });
            }

            // Check if the user has the required role
            if (!User.IsInRole("Employee") && !User.IsInRole("Admin"))
            {
                Response.StatusCode = 403; // Forbidden
                return new JsonResult(new { error = "Access denied. Insufficient permissions." });
            }
            
            try
            {
                _logger.LogInformation("Fetching recent orders for Manager dashboard");
                
                // Get all orders (we'll format them for display)
                var orders = await _orderService.GetAllOrdersAsync();
                
                // Format the orders for the dashboard display
                var formattedOrders = orders.Take(10).Select(order => new {
                    id = order.Id,
                    customerName = order.User?.FullName ?? "Anonymous",
                    itemCount = 1, // Consider using order items when available
                    total = $"${order.TotalPrice:F2}",
                    timeAgo = GetTimeAgo(order.CreatedAt),
                    status = order.Status.ToString()
                }).ToList();
                
                return new JsonResult(formattedOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching recent orders");
                Response.StatusCode = 500;
                return new JsonResult(new { error = "Failed to fetch recent orders" });
            }
        }
        
        private string GetTimeAgo(DateTime dateTime)
        {
            var span = DateTime.Now - dateTime;
            
            if (span.Days > 0)
                return $"{span.Days} day{(span.Days > 1 ? "s" : "")} ago";
            
            if (span.Hours > 0)
                return $"{span.Hours} hour{(span.Hours > 1 ? "s" : "")} ago";
                
            if (span.Minutes > 0)
                return $"{span.Minutes} min{(span.Minutes > 1 ? "s" : "")} ago";
                
            return "Just now";
        }
    }
} 