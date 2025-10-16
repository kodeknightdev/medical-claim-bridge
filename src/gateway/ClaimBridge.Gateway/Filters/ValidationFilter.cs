using FluentValidation;

namespace ClaimBridge.Gateway.Filters;

/// <summary>.NET 9 Minimal APIs don't validate on their own; this runs the FluentValidation validator for T.</summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
            return TypedResults.Problem($"Missing {typeof(T).Name}", statusCode: StatusCodes.Status400BadRequest);

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (!result.IsValid)
            return TypedResults.ValidationProblem(result.ToDictionary());
        return await next(context);
    }
}
