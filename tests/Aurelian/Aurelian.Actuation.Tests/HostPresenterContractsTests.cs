using Aurelian.Actuation.Host;
using Xunit;

namespace Aurelian.Actuation.Tests;

public sealed class HostPresenterContractsTests
{
    [Fact]
    public void MoveIntent_WithinBounds_IsValid()
    {
        HostCommandRequest request = new(
            Guid.NewGuid(),
            3,
            HostCommandKind.SetMoveIntent,
            TimeSpan.FromSeconds(1),
            new MoveIntentArguments(1.0f, -0.5f));

        Assert.True(request.Validate().IsValid);
    }

    [Fact]
    public void ArbitraryArguments_AreRejected()
    {
        HostCommandRequest request = new(
            Guid.NewGuid(),
            3,
            HostCommandKind.BasicAttack,
            TimeSpan.FromSeconds(1),
            new MoveIntentArguments(0.0f, 0.0f));

        HostCommandValidationResult result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Equal("arguments_not_allowed_for_command", result.FailureReason);
    }

    [Fact]
    public void TargetActivation_RequiresTargetIdentity()
    {
        HostCommandRequest request = new(
            Guid.NewGuid(),
            3,
            HostCommandKind.ActivateTarget,
            TimeSpan.FromSeconds(1),
            new ActivateTargetArguments(0));

        HostCommandValidationResult result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Equal("target_form_id_missing", result.FailureReason);
    }
}
