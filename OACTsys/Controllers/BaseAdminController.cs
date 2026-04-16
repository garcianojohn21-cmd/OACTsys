using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OACTsys.Controllers
{
    /// <summary>
    /// Apply this attribute to any action that should bypass the admin auth check.
    /// Use it on Login (GET and POST) only.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class SkipAdminAuthAttribute : Attribute { }

    /// <summary>
    /// Base controller for all admin pages.
    /// Automatically redirects unauthenticated users to the Login page.
    /// Inheriting controllers do NOT need to call IsAdminLoggedIn() manually.
    /// </summary>
    public class BaseAdminController : Controller
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // Skip auth for actions marked with [SkipAdminAuth] (i.e. Login)
            bool shouldSkip = context.ActionDescriptor.EndpointMetadata
                .Any(m => m is SkipAdminAuthAttribute);

            if (!shouldSkip)
            {
                var role = HttpContext.Session.GetString("AdminRole");

                if (string.IsNullOrEmpty(role))
                {
                    // Include the token so the redirect lands on the login page, not a 401
                    var token = HttpContext.RequestServices
                        .GetRequiredService<IConfiguration>()["AdminToken"];

                    context.Result = new RedirectToActionResult(
                        "Login", "Admin", new { token });

                    return;
                }

                // Only populate layout data when the user is authenticated
                SetLayoutData();
            }

            // Base call goes LAST to prevent side effects before auth check completes
            base.OnActionExecuting(context);
        }

        /// <summary>
        /// Populates ViewBag values used by the admin layout (_AdminLayout.cshtml).
        /// </summary>
        protected void SetLayoutData()
        {
            ViewBag.AdminName = HttpContext.Session.GetString("AdminName") ?? "Administrator";
            ViewBag.AdminRole = HttpContext.Session.GetString("AdminRole") ?? "Admin";

            var permissionsString = HttpContext.Session.GetString("Permissions");
            ViewBag.Permissions = !string.IsNullOrEmpty(permissionsString)
                ? permissionsString.Split(',').ToList()
                : new List<string>();
        }

        /// <summary>
        /// Returns true if the current session has the specified permission.
        /// </summary>
        protected bool HasPermission(string permission)
        {
            var permissionsString = HttpContext.Session.GetString("Permissions");
            if (string.IsNullOrEmpty(permissionsString)) return false;
            return permissionsString.Split(',').Contains(permission);
        }

        /// <summary>
        /// Returns true if the admin session is active.
        /// </summary>
        protected bool IsAdminLoggedIn() =>
            !string.IsNullOrEmpty(HttpContext.Session.GetString("AdminRole"));

        /// <summary>
        /// Returns true if the current user is a SuperAdmin.
        /// </summary>
        protected bool IsSuperAdmin()
        {
            var role = HttpContext.Session.GetString("UserRole");
            return role == "SuperAdmin" || role == "Super Administrator";
        }
    }
}