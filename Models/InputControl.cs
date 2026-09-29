namespace Controller_Diagnostics_Tool.Models;

public sealed record InputControl(string Label, string Group, double Value, string Display, bool Mapped, bool Active);
