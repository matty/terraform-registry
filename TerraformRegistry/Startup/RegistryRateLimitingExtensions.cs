using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TerraformRegistry.Startup;

public sealed class RegistryRateLimitMetrics : IDisposable
{
    private readonly Meter _meter = new("TerraformRegistry.RateLimiting");
    private readonly Counter<long> _rejections;

    public RegistryRateLimitMetrics()
    {
        _rejections = _meter.CreateCounter<long>("terraform_registry.rate_limit.rejections");
    }

    public void RecordRejection(string policy, string partitionCategory)
    {
        _rejections.Add(1,
            new KeyValuePair<string, object?>("policy", policy),
            new KeyValuePair<string, object?>("partition", partitionCategory));
    }

    public void Dispose() => _meter.Dispose();
}

public static class RegistryRateLimiterFactory
{
    public static RateLimiter Create(RegistryRateLimitPolicyOptions policy)
    {
        var fixedWindow = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
            QueueLimit = policy.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
        var concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = policy.ConcurrencyLimit,
            QueueLimit = policy.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
        return new OwnedRateLimiter(RateLimiter.CreateChained([fixedWindow, concurrency]), fixedWindow, concurrency);
    }

    private sealed class OwnedRateLimiter(RateLimiter inner, params RateLimiter[] owned) : RateLimiter
    {
        public override TimeSpan? IdleDuration => inner.IdleDuration;

        public override RateLimiterStatistics? GetStatistics() => inner.GetStatistics();

        protected override RateLimitLease AttemptAcquireCore(int permitCount) => inner.AttemptAcquire(permitCount);

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
            inner.AcquireAsync(permitCount, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                foreach (var limiter in owned)
                {
                    limiter.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}

public static class RegistryRateLimitingExtensions
{
    private const string PolicyItemKey = "terraform-registry.rate-limit-policy";

    public static IServiceCollection AddRegistryRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = new RegistryRateLimitOptions();
        configuration.GetSection(RegistryRateLimitOptions.SectionName).Bind(configured);
        configured.Validate();

        services.AddSingleton(configured);
        services.AddSingleton<RegistryRateLimitMetrics>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                var policy = httpContext.Items[PolicyItemKey] as string ?? "unknown";
                var partitionCategory = RateLimitPartitionKey.CategoryFor(RateLimitPartitionKey.For(httpContext));
                httpContext.RequestServices.GetRequiredService<RegistryRateLimitMetrics>()
                    .RecordRejection(policy, partitionCategory);

                httpContext.Response.ContentType = "application/problem+json";
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc6585#section-4",
                    title = "Too Many Requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "The request exceeded the configured rate limit.",
                    policy
                }, cancellationToken);
            };

            foreach (var (name, policy) in configured.Policies)
            {
                options.AddPolicy(name, httpContext =>
                {
                    httpContext.Items[PolicyItemKey] = name;
                    var partition = RateLimitPartitionKey.For(httpContext);
                    return RateLimitPartition.Get(partition, _ => RegistryRateLimiterFactory.Create(policy));
                });
            }
        });

        return services;
    }
}
