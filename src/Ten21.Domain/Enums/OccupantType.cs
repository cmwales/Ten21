namespace Ten21.Domain.Enums;

/// <summary>US-23: which kind of occupant a ResidentProfile row represents -- Primary and
/// Secondary are structurally identical rows (both can be provisioned a login per US-24),
/// distinguished only by this so the directory/drawer UI can label them meaningfully.</summary>
public enum OccupantType
{
    Primary,
    Secondary,
}
