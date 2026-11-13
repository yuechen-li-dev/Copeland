using System.Numerics;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class FirstPersonPresentationTests
{
    [Fact]
    public void AcceptedDistanceDrivesStepsAndIdleSettlesWithoutPhantomRecoil()
    {
        var view = new FirstPersonPresentation3D();
        var state = new FirstPersonMotion3D(Vector3.Zero, 0, 0, true, 0);
        view.Reset(state);
        for (int tick = 1; tick <= 60; tick++)
        {
            view.Step(state with { Feet = new(0, 0, -4f * tick / 60) }, 1f / 60);
        }
        Assert.Equal(2, view.Footsteps);
        Assert.NotEqual(Vector3.Zero, view.Pose.WeaponOffset);
        Assert.Equal(0, view.Recoil);
        for (int tick = 0; tick < 120; tick++)
        {
            view.Step(state with { Feet = new(0, 0, -4) }, 1f / 60);
        }
        Assert.True(view.Pose.WeaponOffset.Length() < .0001f);
        Assert.Equal(2, view.Footsteps);
    }

    [Fact]
    public void RecoilSpringMatchesSubdividedTimeAndDoesNotRepeatHeldShotCounts()
    {
        var whole = new FirstPersonPresentation3D();
        var split = new FirstPersonPresentation3D();
        var state = new FirstPersonMotion3D(Vector3.Zero, 0, 0, true, 0);
        whole.Reset(state);
        split.Reset(state);
        whole.Step(state with { Shots = 1 }, .02f);
        split.Step(state with { Shots = 1 }, .01f);
        split.Step(state with { Shots = 1 }, .01f);
        Assert.Equal(whole.Recoil, split.Recoil, 6);
        Assert.True(whole.Recoil > 0);
        for (int tick = 0; tick < 120; tick++)
        {
            whole.Step(state with { Shots = 1 }, 1f / 60);
        }
        Assert.True(whole.Recoil < .0001f);
    }

    [Fact]
    public void AimLandingAndResetAreBoundedAndDoNotChangeInput()
    {
        var view = new FirstPersonPresentation3D();
        var state = new FirstPersonMotion3D(Vector3.Zero, 0, 0, true, 0);
        view.Reset(state);
        for (int tick = 0; tick < 60; tick++)
        {
            view.Step(state with { Aiming = true }, 1f / 60);
        }
        Assert.InRange(view.Pose.FieldOfView * 180 / MathF.PI, 47.99f, 48.01f);
        view.Step(state with { Feet = new(0, 1, 0), Grounded = false }, .05f);
        view.Step(state with { Feet = new(0, .5f, 0), Grounded = false }, .05f);
        view.Step(state, .05f);
        Assert.Equal(1, view.Landings);
        Assert.Equal(10, view.LastLandingSpeed);
        Assert.InRange(view.Pose.CameraOffset.Y, -.06f, 0);
        view.Reset(state);
        Assert.Equal(FirstPersonPose3D.Neutral, view.Pose);
        Assert.Equal(Vector3.Zero, state.Feet);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Step(state, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirstPersonPresentation3D(new() { SpringFrequency = float.NaN }));
    }
}
