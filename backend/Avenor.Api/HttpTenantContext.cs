using System.Security.Claims;
using Avenor.Application;

namespace Avenor.Api;

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private Guid? systemTenant;
    public void SetSystemTenant(Guid id) => systemTenant = id;
    public Guid? TenantId => systemTenant ?? (Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("tenant"), out var id) ? id : null);
    public Guid? UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string Role => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ?? "";
    public string UserName => accessor.HttpContext?.User.Identity?.Name ?? "System";
    public bool IsCustomer => Role == "Customer";
}
