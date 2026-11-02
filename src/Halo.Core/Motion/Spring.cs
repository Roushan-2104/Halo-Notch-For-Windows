namespace Halo.Core.Motion;

/// <summary>
/// A damped harmonic spring parameterised like SwiftUI's <c>spring(response:dampingFraction:)</c>.
/// <see cref="Response"/> is the period of the undamped oscillation in seconds; a damping fraction of 1
/// is critically damped and anything below 1 overshoots. Retargeting keeps the current velocity, so an
/// animation can be interrupted mid-flight without a visible kink.
/// </summary>
public sealed class Spring
{
    const double MaxStep = 1.0 / 480.0;

    public Spring(double value, double response = 0.45, double dampingFraction = 0.85)
    {
        Value = Target = value;
        Response = response;
        DampingFraction = dampingFraction;
    }

    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }
    public double Response { get; set; }
    public double DampingFraction { get; set; }

    /// <summary>Distance from the target (in value units) below which the spring counts as at rest.</summary>
    public double RestThreshold { get; set; } = 0.05;

    public bool IsSettled =>
        Math.Abs(Target - Value) <= RestThreshold && Math.Abs(Velocity) <= RestThreshold * 4;

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    public void Step(double dt)
    {
        if (dt <= 0) return;
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
            return;
        }

        double omega = 2 * Math.PI / Math.Max(Response, 0.01);
        double stiffness = omega * omega;
        double damping = 2 * DampingFraction * omega;

        // Fixed sub-steps keep the integration stable regardless of the display's frame rate.
        dt = Math.Min(dt, 0.1);
        while (dt > 1e-9)
        {
            double h = Math.Min(MaxStep, dt);
            double accel = -stiffness * (Value - Target) - damping * Velocity;
            Velocity += accel * h;
            Value += Velocity * h;
            dt -= h;
        }

        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}
