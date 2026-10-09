namespace Buddy.Windows;

internal enum CompanionMood { Idle, Listening, Looking, Thinking, Speaking, Pointing, Unsure, Error, AgentWorking, Researching, Sleeping }

// Each surface owns one activity entry. An idle callback cannot erase another active session.
internal sealed class CompanionState
{
    private readonly Dictionary<string, (CompanionMood Mood, long Sequence)> active = [];
    private long sequence;
    internal string BrainLabel { get; private set; } = "on this PC";
    internal CompanionMood Current { get; private set; }
    internal event Action<CompanionMood>? Changed;
    internal void Set(string source, CompanionMood mood)
    {
        if (mood == CompanionMood.Idle) active.Remove(source); else active[source] = (mood, ++sequence);
        var next = active.Values.OrderByDescending(v => Priority(v.Mood)).ThenByDescending(v => v.Sequence).FirstOrDefault().Mood;
        if (next != Current) { Current = next; Changed?.Invoke(next); }
    }
    private static int Priority(CompanionMood mood) => mood switch {
        CompanionMood.Listening => 100, CompanionMood.AgentWorking => 90, CompanionMood.Looking => 80,
        CompanionMood.Speaking => 70, CompanionMood.Researching => 60, CompanionMood.Thinking => 50,
        CompanionMood.Pointing => 40, CompanionMood.Error => 30, CompanionMood.Unsure => 20, CompanionMood.Sleeping => 10, _ => 0
    };
}

internal sealed class CompanionSpring
{
    internal PixelPosition Position { get; private set; }
    private double vx, vy;
    private bool ready;
    internal PixelPosition Step(PixelPosition target, double seconds, bool animate)
    {
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Y)) throw new ArgumentException("Invalid companion target.");
        if (!ready || !animate || Math.Abs(target.X - Position.X) + Math.Abs(target.Y - Position.Y) > 1600) {
            ready = true; vx = vy = 0; return Position = target;
        }
        var dt = Math.Clamp(seconds, .001, .04);
        vx += (180 * (target.X - Position.X) - 22 * vx) * dt;
        vy += (180 * (target.Y - Position.Y) - 22 * vy) * dt;
        Position = new(Position.X + vx * dt, Position.Y + vy * dt);
        if (Math.Abs(target.X - Position.X) + Math.Abs(target.Y - Position.Y) < .2 && Math.Abs(vx) + Math.Abs(vy) < .2) { Position = target; vx = vy = 0; }
        return Position;
    }
    internal void Reset() { ready = false; vx = vy = 0; }
}
