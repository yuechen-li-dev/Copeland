using System.Numerics;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class FirstPersonPresentationTests
{
    [Fact]
    public void WeaponClearanceProfileControlsTheBoundedRetractionPose()
    {
        var profile = new FirstPersonProfile3D
        {
            WeaponReachMetres = .8f,
            RetractedReachMetres = .2f,
            ObstructionOffset = new(-.1f, .1f, -.3f),
            ObstructionPitchRadians = -.8f,
        };
        var view = new FirstPersonPresentation3D(profile);
        view.Reset(new(Vector3.Zero, 0, 0, true, 0, WeaponClearance: .5f));
        Assert.Equal(.5f, view.Pose.Obstruction, 6);
        Assert.True(Vector3.Distance(profile.ObstructionOffset * .5f, view.Pose.WeaponOffset) < .00001f);
        Assert.Equal(-.4f, view.Pose.WeaponAngles.X, 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirstPersonPresentation3D(profile with
        {
            RetractedReachMetres = profile.WeaponReachMetres,
        }));
    }

    [Fact]
    public void WallResponseEntersImmediatelyRecoversSmoothlyAndSurvivesMotionDisable()
    {
        var view = new FirstPersonPresentation3D();
        var state = new FirstPersonMotion3D(Vector3.Zero, 0, 0, true, 0);
        view.Reset(state);
        view.Step(state with { WeaponClearance = .2f, Aiming = true, Reloading = true, Shots = 1 }, 1f / 60);
        Assert.Equal(1, view.Pose.Obstruction);
        Assert.Equal(0, view.Pose.Aim);
        Assert.Equal(-1.35f, view.Pose.WeaponAngles.X);
        Assert.Equal(1, view.PoseWithoutMotion.Obstruction);
        view.Step(state with { Shots = 1 }, 1f / 60);
        Assert.InRange(view.Pose.Obstruction, .8f, .95f);
        for (int frame = 0; frame < 90; frame++)
        {
            view.Step(state with { Shots = 1 }, 1f / 60);
        }
        Assert.InRange(view.Pose.Obstruction, 0, .0001f);
        view.Reset(state with { WeaponClearance = 0 });
        Assert.Equal(1, view.Pose.Obstruction);
    }

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
