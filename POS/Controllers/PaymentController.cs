using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using POS.Models;
using POS.Services;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace POS.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly IPayPalService _paypalService;
        private readonly IOrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IPayPalService paypalService,
            IOrderService orderService,
            UserManager<ApplicationUser> userManager,
            ILogger<PaymentController> logger)
        {
            _paypalService = paypalService;
            _orderService = orderService;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreatePayment(int orderId)
        {
            _logger.LogInformation($"CreatePayment action called with orderId: {orderId}");
            
            // Get the order
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning($"Order not found: {orderId}");
                return NotFound();
            }
            
            // Check if user is eligible for discount and it hasn't been requested yet
            if (!order.IsDiscountRequested)
            {
                var user = await _userManager.FindByIdAsync(order.UserId);
                if (user != null && (user.IsSeniorCitizen || user.IsPWD))
                {
                    // User is eligible for discount - show discount request page
                    return RedirectToAction("DiscountRequest", new { orderId = orderId });
                }
            }
            
            // If discount is requested but not yet approved, redirect to a waiting page
            if (order.IsDiscountRequested && !order.IsDiscountApproved && 
                order.Status == OrderStatus.AwaitingDiscountApproval)
            {
                return RedirectToAction("DiscountPending", new { orderId = orderId });
            }

            // Create PayPal order
            try
            {
                _logger.LogInformation($"Creating PayPal order for orderId: {orderId}, amount: {order.TotalPrice}");
                var approvalUrl = await _paypalService.CreateOrder(order.TotalPrice);
                
                _logger.LogInformation($"PayPal approval URL: {approvalUrl}");
                
                // Store order ID in session for retrieval after payment
                HttpContext.Session.SetInt32("PaymentOrderId", orderId);
                
                // Check if this is an AJAX request
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    // Return JSON response with the redirect URL
                    return Json(new { redirectUrl = approvalUrl });
                }
                
                // Redirect to PayPal for approval (normal form submit)
                return Redirect(approvalUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError($"PayPal Error: {ex.Message}");
                TempData["ErrorMessage"] = $"PayPal Error: {ex.Message}";
                
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return BadRequest(new { error = ex.Message });
                }
                
                return RedirectToAction("Details", "Orders", new { id = orderId });
            }
        }
        
        [HttpGet]
        public async Task<IActionResult> DiscountRequest(int orderId)
        {
            // Get the order
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning($"Order not found: {orderId}");
                return NotFound();
            }
            
            // Get user
            var user = await _userManager.FindByIdAsync(order.UserId);
            if (user == null)
            {
                return NotFound();
            }
            
            // Display discount request page
            ViewBag.Order = order;
            ViewBag.User = user;
            ViewBag.DiscountOptions = new List<string>();
            
            if (user.IsSeniorCitizen)
            {
                ViewBag.DiscountOptions.Add("SeniorCitizen");
            }
            
            if (user.IsPWD)
            {
                ViewBag.DiscountOptions.Add("PWD");
            }
            
            return View();
        }
        
        [HttpGet]
        public async Task<IActionResult> DiscountPending(int orderId)
        {
            // Get the order
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning($"Order not found: {orderId}");
                return NotFound();
            }
            
            // Display waiting page
            ViewBag.Order = order;
            return View();
        }
        
        [HttpPost]
        public async Task<IActionResult> GetPayPalRedirectUrl(int orderId)
        {
            _logger.LogInformation($"GetPayPalRedirectUrl called for orderId: {orderId}");
            
            try
            {
                // Get the order
                var order = await _orderService.GetOrderByIdAsync(orderId);
                if (order == null)
                {
                    _logger.LogWarning($"Order not found: {orderId}");
                    return NotFound(new { error = "Order not found" });
                }
                
                // Create PayPal order and get the approval URL
                var approvalUrl = await _paypalService.CreateOrder(order.TotalPrice);
                
                _logger.LogInformation($"PayPal approval URL: {approvalUrl}");
                
                // Store order ID in session for retrieval after payment
                HttpContext.Session.SetInt32("PaymentOrderId", orderId);
                
                // Return the URL as JSON
                return Json(new { redirectUrl = approvalUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError($"PayPal Error in GetPayPalRedirectUrl: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> CreatePaymentForm(int orderId)
        {
            // For GET requests, we'll render a form that will submit to the POST version
            // This is a workaround for HTTP 405 Method Not Allowed errors when redirecting
            
            _logger.LogInformation($"GET CreatePaymentForm action called with orderId: {orderId}");
            
            // Get the order
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning($"Order not found: {orderId}");
                return NotFound();
            }
            
            // Check if user is eligible for discount and it hasn't been requested yet
            if (!order.IsDiscountRequested)
            {
                var user = await _userManager.FindByIdAsync(order.UserId);
                if (user != null && (user.IsSeniorCitizen || user.IsPWD))
                {
                    // User is eligible for discount - show discount request page
                    return RedirectToAction("DiscountRequest", new { orderId = orderId });
                }
            }
            
            // If discount is requested but not yet approved, redirect to a waiting page
            if (order.IsDiscountRequested && !order.IsDiscountApproved && 
                order.Status == OrderStatus.AwaitingDiscountApproval)
            {
                return RedirectToAction("DiscountPending", new { orderId = orderId });
            }

            // Render a view with a form that will POST to the CreatePayment action
            ViewBag.Order = order;
            return View("PaymentRedirect");
        }

        public async Task<IActionResult> Success(string token)
        {
            // Get order ID from session
            var orderId = HttpContext.Session.GetInt32("PaymentOrderId");
            if (!orderId.HasValue)
            {
                return RedirectToAction("Index", "Home");
            }

            try
            {
                // Capture the payment
                var paypalOrder = await _paypalService.CaptureOrder(token);
                
                // Update the order status in database
                await _orderService.UpdateOrderStatusAsync(orderId.Value, OrderStatus.Paid);
                
                // Clear the session
                HttpContext.Session.Remove("PaymentOrderId");
                
                // Add success message to TempData
                TempData["SuccessMessage"] = "Payment completed successfully!";
                
                // Return a view with the order ID and payment token
                return View("Success", new PaymentSuccessViewModel
                {
                    OrderId = orderId.Value,
                    PaymentToken = token
                });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to process payment: {ex.Message}";
                return RedirectToAction("Details", "Orders", new { id = orderId.Value });
            }
        }

        public async Task<IActionResult> Cancel()
        {
            var orderId = HttpContext.Session.GetInt32("PaymentOrderId");
            
            _logger.LogInformation($"Payment cancelled for orderId: {orderId}");
            
            if (orderId.HasValue)
            {
                // Update the order status to Cancelled
                await _orderService.UpdateOrderStatusAsync(orderId.Value, OrderStatus.Cancelled);
                
                // Clean up session
                HttpContext.Session.Remove("PaymentOrderId");
                TempData["ErrorMessage"] = "Payment was cancelled. Your order will not be processed.";
                TempData["CancelledCheckout"] = "true"; // Flag to notify frontend to clear the overlay
                TempData["PreventCartClear"] = "true"; // Flag to prevent cart from being cleared
                
                // Add JavaScript to immediately fix the overlay (executed before page fully loads)
                TempData["FixOverlayScript"] = @"
                    window.onload = function() {
                        const overlay = document.getElementById('checkoutOverlay');
                        if (overlay) {
                            overlay.style.display = 'none';
                            overlay.style.visibility = 'hidden';
                            overlay.style.opacity = '0';
                            console.log('Overlay hidden by direct script');
                        }

                        // Clear any navigation blockers
                        window.removeEventListener('beforeunload', window.preventNavigation);
                        sessionStorage.removeItem('checkoutInProgress');
                    };
                ";
                
                // Redirect back to the user dashboard instead of cart
                return RedirectToAction("Index", "User", new { area = "User" });
            }
            
            return RedirectToAction("Index", "Home");
        }
    }
} 