using EReader_API.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EReader_API.Infrastructure;

public class GlobalExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, errors) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Recurso não encontrado", null),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Operação não permitida", null),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Não autorizado", null),
            ConflictException => (StatusCodes.Status409Conflict, "Conflito", null),
            ValidationException ve => (StatusCodes.Status400BadRequest, "Erro de validação",
                (IReadOnlyCollection<string>?)ve.Errors),
            _ => (0, null, null),
        };

        if (status == 0)
            return false; // deixa o handler default (500 problem+json, sem stack) tratar

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
        };
        if (errors is not null)
            problem.Extensions["errors"] = new Dictionary<string, string[]> { [""] = errors.ToArray() };

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
        return true;
    }
}
