using Spectra.Contracts.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class RunContextTests
{
    [Fact]
    public void Anonymous_has_null_identity()
    {
        var ctx = RunContext.Anonymous;

        Assert.Null(ctx.TenantId);
        Assert.Null(ctx.UserId);
        Assert.Empty(ctx.Claims);
        Assert.Empty(ctx.Metadata);
    }

    [Fact]
    public void Can_set_identity_properties()
    {
        var ctx = new RunContext
        {
            TenantId = "acme",
            UserId = "u-1",
            CorrelationId = "corr-1"
        };

        Assert.Equal("acme", ctx.TenantId);
        Assert.Equal("u-1", ctx.UserId);
        Assert.Equal("corr-1", ctx.CorrelationId);
    }

    [Fact]
    public void Metadata_is_mutable_dictionary()
    {
        var ctx = new RunContext();
        ctx.Metadata["env"] = "staging";

        Assert.Equal("staging", ctx.Metadata["env"]);
        Assert.Equal("staging", ctx.Metadata["env"]);
    }

    [Fact]
    public void HasRole_returns_true_for_matching_role()
    {
        var ctx = new RunContext
        {
            Roles = ["admin", "viewer"]
        };

        Assert.True(ctx.HasRole("admin"));
        Assert.True(ctx.HasRole("Admin")); // case-insensitive
        Assert.False(ctx.HasRole("editor"));
    }

    [Fact]
    public void HasRole_returns_false_when_no_roles()
    {
        var ctx = new RunContext();
        Assert.False(ctx.HasRole("admin"));
    }

    [Fact]
    public void HasClaim_by_type_returns_true_when_present()
    {
        var ctx = new RunContext
        {
            Claims = [new System.Security.Claims.Claim("department", "engineering")]
        };

        Assert.True(ctx.HasClaim("department"));
        Assert.True(ctx.HasClaim("Department")); // case-insensitive
        Assert.False(ctx.HasClaim("org"));
    }

    [Fact]
    public void HasClaim_by_type_and_value_returns_true_when_matched()
    {
        var ctx = new RunContext
        {
            Claims = [new System.Security.Claims.Claim("department", "engineering")]
        };

        Assert.True(ctx.HasClaim("department", "engineering"));
        Assert.True(ctx.HasClaim("Department", "Engineering")); // case-insensitive
        Assert.False(ctx.HasClaim("department", "marketing"));
    }

    [Fact]
    public void FindClaim_returns_matching_claim()
    {
        var claim = new System.Security.Claims.Claim("org", "acme");
        var ctx = new RunContext { Claims = [claim] };

        var found = ctx.FindClaim("org");
        Assert.NotNull(found);
        Assert.Equal("acme", found.Value);
        Assert.Null(ctx.FindClaim("missing"));
    }

    [Fact]
    public void Roles_defaults_to_empty()
    {
        var ctx = new RunContext();
        Assert.Empty(ctx.Roles);
    }
}