using Controller_Diagnostics_Tool.Models;

namespace Controller_Diagnostics_Tool.Services;

public static class DiagnosticThresholds
{
    public const int NeutralSamples = 15;
    public const double NeutralCaptureRadius = 0.18;
    public const double NeutralSpread = 0.04;
    public const double MinorDrift = 0.035;
    public const double NoticeableDrift = 0.09;
    public const double StickHistoryStep = 0.22;
    public const double TriggerHistoryStep = 0.12;
}

public sealed class StickDiagnostic
{
    private readonly Queue<(double X, double Y)> _neutral = new();
    public double X { get; private set; }
    public double Y { get; private set; }
    public bool Available { get; private set; }
    public double DeadZone { get; set; } = 0.08;
    public double Distance => Math.Sqrt(X * X + Y * Y);
    public bool InsideDeadZone => Distance <= DeadZone;
    public double NeutralX { get; private set; }
    public double NeutralY { get; private set; }
    public double LargestNeutralDeviation { get; private set; }
    public string DriftStatus { get; private set; } = "Collecting neutral samples";
    public string RestingStatus { get; private set; } = "Not enough samples";
    public bool HasNeutralEstimate { get; private set; }
    public double MinX { get; private set; } = double.PositiveInfinity;
    public double MaxX { get; private set; } = double.NegativeInfinity;
    public double MinY { get; private set; } = double.PositiveInfinity;
    public double MaxY { get; private set; } = double.NegativeInfinity;
    public bool Testing { get; private set; }
    public double TestMinX { get; private set; } = double.PositiveInfinity;
    public double TestMaxX { get; private set; } = double.NegativeInfinity;
    public double TestMinY { get; private set; } = double.PositiveInfinity;
    public double TestMaxY { get; private set; } = double.NegativeInfinity;
    public double TestMaxRadius { get; private set; }
    public double TestCenterX { get; private set; }
    public double TestCenterY { get; private set; }

    public void Update(double x, double y)
    {
        Available = true;
        X = x;
        Y = y;
        MinX = Math.Min(MinX, x); MaxX = Math.Max(MaxX, x);
        MinY = Math.Min(MinY, y); MaxY = Math.Max(MaxY, y);

        if (Distance <= DiagnosticThresholds.NeutralCaptureRadius)
        {
            _neutral.Enqueue((x, y));
            while (_neutral.Count > DiagnosticThresholds.NeutralSamples) _neutral.Dequeue();
            if (_neutral.Count >= DiagnosticThresholds.NeutralSamples &&
                _neutral.Max(p => p.X) - _neutral.Min(p => p.X) <= DiagnosticThresholds.NeutralSpread &&
                _neutral.Max(p => p.Y) - _neutral.Min(p => p.Y) <= DiagnosticThresholds.NeutralSpread)
            {
                NeutralX = _neutral.Average(p => p.X);
                NeutralY = _neutral.Average(p => p.Y);
                LargestNeutralDeviation = Math.Max(LargestNeutralDeviation,
                    _neutral.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)));
                var restingDistance = Math.Sqrt(NeutralX * NeutralX + NeutralY * NeutralY);
                RestingStatus = restingDistance >= DiagnosticThresholds.NoticeableDrift ? "Noticeable drift observed"
                    : restingDistance >= DiagnosticThresholds.MinorDrift ? "Minor drift observed" : "Stable";
                HasNeutralEstimate = true;
                DriftStatus = RestingStatus;
            }
        }
        else
        {
            _neutral.Clear();
            DriftStatus = "Moving";
        }

        if (!Testing) return;
        TestMinX = Math.Min(TestMinX, x); TestMaxX = Math.Max(TestMaxX, x);
        TestMinY = Math.Min(TestMinY, y); TestMaxY = Math.Max(TestMaxY, y);
        TestMaxRadius = Math.Max(TestMaxRadius, Math.Sqrt(
            Math.Pow(x - TestCenterX, 2) + Math.Pow(y - TestCenterY, 2)));
    }

    public void ToggleTest()
    {
        if (Testing) { Testing = false; return; }
        ResetTest();
        TestCenterX = NeutralX;
        TestCenterY = NeutralY;
        Testing = true;
    }

    public void ResetTest()
    {
        Testing = false;
        TestMinX = TestMinY = double.PositiveInfinity;
        TestMaxX = TestMaxY = double.NegativeInfinity;
        TestMaxRadius = 0;
        TestCenterX = NeutralX;
        TestCenterY = NeutralY;
    }
}

public sealed class TriggerDiagnostic
{
    public double Value { get; private set; }
    public double ObservedMin { get; private set; } = double.PositiveInfinity;
    public double ObservedMax { get; private set; } = double.NegativeInfinity;
    public double TestMin { get; private set; } = double.PositiveInfinity;
    public double TestMax { get; private set; } = double.NegativeInfinity;
    public bool Testing { get; private set; }

    public void Update(double value)
    {
        Value = value;
        ObservedMin = Math.Min(ObservedMin, value);
        ObservedMax = Math.Max(ObservedMax, value);
        if (!Testing) return;
        TestMin = Math.Min(TestMin, value);
        TestMax = Math.Max(TestMax, value);
    }

    public void ToggleTest()
    {
        if (Testing) { Testing = false; return; }
        Reset();
        Testing = true;
    }

    public void Reset()
    {
        Testing = false;
        TestMin = double.PositiveInfinity;
        TestMax = double.NegativeInfinity;
    }
}

public sealed record InputEvent(DateTime Time, string Control, string State)
{
    public string TimeLabel => Time.ToString("HH:mm:ss");
}

public sealed class DiagnosticsSession
{
    private readonly Dictionary<string, InputControl> _previous = new();
    private readonly Dictionary<string, (double X, double Y, DateTime Time)> _lastStickEvent = new();
    private readonly List<InputEvent> _history = new();
    public StickDiagnostic Left { get; } = new();
    public StickDiagnostic Right { get; } = new();
    public Dictionary<string, TriggerDiagnostic> Triggers { get; } = new();
    public Dictionary<string, (double Min, double Max)> RawRanges { get; } = new();
    public IReadOnlyList<InputEvent> History => _history;

    public void Update(IReadOnlyList<InputControl> controls)
    {
        var now = DateTime.Now;
        UpdateStick(Left, "Left", controls, now);
        UpdateStick(Right, "Right", controls, now);
        foreach (var control in controls)
        {
            if (control.Group == "Triggers")
            {
                if (!Triggers.TryGetValue(control.Label, out var trigger))
                    Triggers[control.Label] = trigger = new TriggerDiagnostic();
                trigger.Update(control.Value);
            }
            if (!control.Mapped)
            {
                var old = RawRanges.GetValueOrDefault(control.Label, (double.PositiveInfinity, double.NegativeInfinity));
                var raw = double.TryParse(control.Display, out var parsed) ? parsed : control.Value;
                RawRanges[control.Label] = (Math.Min(old.Item1, raw), Math.Max(old.Item2, raw));
            }

            if (control.Group is "Buttons" or "D-pad")
            {
                if (_previous.TryGetValue(control.Label, out var before))
                {
                    if (before.Display != control.Display) AddEvent(now, control.Label, control.Display);
                }
                else if (control.Active) AddEvent(now, control.Label, control.Display);
            }
            else if (control.Group == "Triggers" &&
                     (!_previous.TryGetValue(control.Label, out var before) && control.Value > DiagnosticThresholds.TriggerHistoryStep ||
                      before is not null && Math.Abs(control.Value - before.Value) >= DiagnosticThresholds.TriggerHistoryStep))
                AddEvent(now, control.Label, control.Display);

            _previous[control.Label] = control;
        }
    }

    private void UpdateStick(StickDiagnostic stick, string side, IReadOnlyList<InputControl> controls, DateTime now)
    {
        var x = controls.FirstOrDefault(c => c.Label == side + " stick X");
        var y = controls.FirstOrDefault(c => c.Label == side + " stick Y");
        if (x is null || y is null) return;
        stick.Update(x.Value, y.Value);
        if (_lastStickEvent.TryGetValue(side, out var last) &&
            (now - last.Time).TotalMilliseconds < 300) return;
        if (!_lastStickEvent.TryGetValue(side, out last) ||
            Math.Sqrt(Math.Pow(x.Value - last.X, 2) + Math.Pow(y.Value - last.Y, 2)) >= DiagnosticThresholds.StickHistoryStep)
        {
            if (stick.Distance > 0.12 || Math.Sqrt(last.X * last.X + last.Y * last.Y) > 0.12)
                AddEvent(now, side + " stick", $"X {x.Value:+0.00;-0.00;0.00}  Y {y.Value:+0.00;-0.00;0.00}");
            _lastStickEvent[side] = (x.Value, y.Value, now);
        }
    }

    private void AddEvent(DateTime time, string control, string state)
    {
        _history.Insert(0, new InputEvent(time, control, state));
        if (_history.Count > 25) _history.RemoveAt(_history.Count - 1);
    }
}
