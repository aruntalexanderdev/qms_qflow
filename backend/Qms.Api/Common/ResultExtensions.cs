using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Qms.Application.Common;

namespace Qms.Api.Common;

/// <summary>Maps application <see cref="Result"/> values to HTTP responses (ADR-002).</summary>
public static class ResultExtensions
{
    public static IActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);
        return result.IsSuccess ? controller.NoContent() : ToProblem(result.Error!, controller);
    }

    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);
        return result.IsSuccess ? controller.Ok(result.Value) : ToProblem(result.Error!, controller);
    }

    /// <summary>201 Created with a <c>Location</c> header and the value as body.</summary>
    public static IActionResult ToCreatedResult<T>(this Result<T> result, ControllerBase controller, Func<T, string> location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(location);
        return result.IsSuccess ? controller.Created(location(result.Value), result.Value) : ToProblem(result.Error!, controller);
    }

    /// <summary>204 No Content for a command whose value is not returned (delete).</summary>
    public static IActionResult ToNoContentResult<T>(this Result<T> result, ControllerBase controller) =>
        ((Result)result).ToActionResult(controller);

    public static ObjectResult ToProblem(this Error error, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(controller);

        var status = StatusFor(error.Type);
        ProblemDetails problem;
        if (error.Type == ErrorType.Validation && error.Details is { Count: > 0 })
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in error.Details)
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError(ErrorKeyNaming.Normalize(field), message);
                }
            }

            problem = controller.ProblemDetailsFactory.CreateValidationProblemDetails(
                controller.HttpContext, modelState, status, detail: error.Message);
        }
        else
        {
            problem = controller.ProblemDetailsFactory.CreateProblemDetails(
                controller.HttpContext, status, detail: error.Message);
        }

        problem.Extensions["code"] = error.Code;
        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
}
