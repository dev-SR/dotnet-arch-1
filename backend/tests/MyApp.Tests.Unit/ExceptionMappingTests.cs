using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Shared.Common.Exceptions.Http;
using AspStatus = Microsoft.AspNetCore.Http.StatusCodes;

namespace MyApp.Tests.Unit;

public class ExceptionMappingTests
{
    [Fact]
    public void Map_BadHttpRequest_Uses_Status_From_Exception()
    {
        var (status, code) = ExceptionMapping.Map(new BadHttpRequestException("bad json", 400));
        status.Should().Be(400);
        code.Should().Be("BAD_REQUEST");
    }

    [Fact]
    public void Map_ArgumentException_Is_500()
    {
        var (status, code) = ExceptionMapping.Map(new ArgumentException("programmer error"));
        status.Should().Be(AspStatus.Status500InternalServerError);
        code.Should().Be("INTERNAL_ERROR");
    }

    [Fact]
    public void Map_InvalidOperationException_Is_500()
    {
        var (status, code) = ExceptionMapping.Map(new InvalidOperationException("boom"));
        status.Should().Be(AspStatus.Status500InternalServerError);
        code.Should().Be("INTERNAL_ERROR");
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException), true)]
    [InlineData(typeof(BadHttpRequestException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(ArgumentException), false)]
    public void IsExpected_Matches_Status_Band(Type exceptionType, bool expected)
    {
        Exception ex = exceptionType.Name switch
        {
            nameof(OperationCanceledException) => new OperationCanceledException(),
            nameof(BadHttpRequestException) => new BadHttpRequestException("bad", 400),
            nameof(InvalidOperationException) => new InvalidOperationException(),
            nameof(ArgumentException) => new ArgumentException(),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionType)),
        };

        ExceptionMapping.IsExpected(ex).Should().Be(expected);
    }
}
