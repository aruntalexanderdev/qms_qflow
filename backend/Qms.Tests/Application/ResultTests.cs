using Qms.Application.Common;

namespace Qms.Tests.Application;

public sealed class ResultTests
{
    [Fact]
    public void Success_has_value_and_no_error()
    {
        var result = Result<int>.Success(7);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
        result.Value.ShouldBe(7);
    }

    [Fact]
    public void Failure_has_error_and_reading_value_throws()
    {
        var error = Error.NotFound("thing.not_found", "missing");

        var result = Result<int>.Failure(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        error.Type.ShouldBe(ErrorType.NotFound);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Implicit_conversions_create_success_and_failure()
    {
        Result<string> success = "ok";
        Result<string> failure = Error.Conflict("x.conflict", "no");

        success.Value.ShouldBe("ok");
        failure.Error!.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Non_generic_result_reports_state()
    {
        Result.Success().IsSuccess.ShouldBeTrue();
        Result.Failure(Error.Forbidden("a.forbidden", "no")).Error!.Type.ShouldBe(ErrorType.Forbidden);
        Error.Unauthorized("a.unauthorized", "no").Type.ShouldBe(ErrorType.Unauthorized);
    }
}
