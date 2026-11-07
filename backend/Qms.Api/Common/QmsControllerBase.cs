using Microsoft.AspNetCore.Mvc;

namespace Qms.Api.Common;

/// <summary>Base for all API controllers: <c>/api/v1/...</c> and problem details for errors. Keep controllers thin.</summary>
[ApiController]
[Route(ApiRoutes.Prefix + "/[controller]")]
public abstract class QmsControllerBase : ControllerBase
{
}

public static class ApiRoutes
{
    public const string Prefix = "api/v1";
}
