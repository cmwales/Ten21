namespace Ten21.Domain.Enums;

/// <summary>US-19/US-20: a Unit's current occupancy state. Drives US-20's list-view badge
/// coloring (Vacant = Alert Amber, Occupied = Financial Emerald).</summary>
public enum OccupancyStatus
{
    Vacant,
    Occupied,
    Maintenance,
}
