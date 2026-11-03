using Shammy.Core.Motion;

namespace Shammy.Core.Tests;

public class SpringTests
{
    static int StepUntilSettled(Spring s, double dt = 1.0 / 60, int maxFrames = 600)
    {
        int frames = 0;
        while (!s.IsSettled && frames < maxFrames)
        {
            s.Step(dt);
            frames++;
        }
        return frames;
    }

    [Fact]
    public void Settles_exactly_on_target()
    {
        var s = new Spring(0) { Target = 400 };
        int frames = StepUntilSettled(s);
        Assert.True(s.IsSettled);
        Assert.Equal(400, s.Value);
        Assert.Equal(0, s.Velocity);
        Assert.InRange(frames, 10, 120);
    }

    [Fact]
    public void Underdamped_spring_overshoots_and_critical_does_not()
    {
        var bouncy = new Spring(0, 0.5, 0.6) { Target = 100 };
        var critical = new Spring(0, 0.5, 1.0) { Target = 100 };
        double bouncyMax = 0, criticalMax = 0;
        for (int i = 0; i < 240; i++)
        {
            bouncy.Step(1.0 / 60);
            critical.Step(1.0 / 60);
            bouncyMax = Math.Max(bouncyMax, bouncy.Value);
            criticalMax = Math.Max(criticalMax, critical.Value);
        }
        Assert.True(bouncyMax > 100.5);
        Assert.True(criticalMax <= 100.0001);
    }

    [Fact]
    public void Result_is_independent_of_frame_rate()
    {
        var at60 = new Spring(0) { Target = 200 };
        var at144 = new Spring(0) { Target = 200 };
        for (int i = 0; i < 15; i++) at60.Step(1.0 / 60);
        for (int i = 0; i < 36; i++) at144.Step(1.0 / 144);
        Assert.InRange(Math.Abs(at60.Value - at144.Value), 0, 0.5);
    }

    [Fact]
    public void Retargeting_keeps_velocity()
    {
        var s = new Spring(0) { Target = 100 };
        for (int i = 0; i < 6; i++) s.Step(1.0 / 60);
        double v = s.Velocity;
        s.Target = 300;
        Assert.Equal(v, s.Velocity);
    }

    [Fact]
    public void Huge_frame_gap_stays_stable()
    {
        var s = new Spring(0) { Target = 100 };
        s.Step(5);
        Assert.False(double.IsNaN(s.Value));
        Assert.InRange(s.Value, -500, 500);
    }
}
