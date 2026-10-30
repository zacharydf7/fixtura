namespace Fixtura.Tests.Unit;

public class TestUserTests
{
    [Fact]
    public void Equals_ShouldBeTrue_WhenTwoUsersAreBuiltAlike()
    {
        TestUser first = TestUser.Named("ann").WithId("7").WithRoles("Auditor").WithClaim("tenant", "north");
        TestUser second = TestUser.Named("ann").WithId("7").WithRoles("Auditor").WithClaim("tenant", "north");

        bool equal = first.Equals(second);

        Assert.True(equal);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_ShouldBeFalse_WhenTheRolesDiffer()
    {
        TestUser auditor = TestUser.Named("ann").WithRoles("Auditor");

        bool equal = auditor.Equals(TestUser.Named("ann").WithRoles("Manager"));

        Assert.False(equal);
    }

    [Fact]
    public void ToString_ShouldListTheRolesAndClaims_WhenCalled()
    {
        TestUser user = TestUser.Named("ann").WithRoles("Auditor", "Manager").WithClaim("tenant", "north");

        string text = user.ToString();

        Assert.Equal("TestUser { Name = ann, Roles = [Auditor, Manager], Claims = [tenant: north] }", text);
    }
}
