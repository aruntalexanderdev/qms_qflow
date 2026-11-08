using Microsoft.Extensions.Options;

namespace Qms.Infrastructure.Identity;

/// <summary>Fails host start-up with a message that names the missing or weak setting.</summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var error = JwtOptions.Validate(options);
        return error is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
    }
}
