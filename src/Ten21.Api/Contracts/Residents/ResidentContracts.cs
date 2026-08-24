using Ten21.Domain.Enums;

namespace Ten21.Api.Contracts.Residents;

public record EmergencyContactRequest(string Name, string PhoneNumber, string? Relationship);

/// <summary>
/// US-23: EmergencyContacts is always the FULL desired set for this resident -- PUT
/// replaces every existing contact row with whatever's in this list (remove-all-then-re-add),
/// not a per-item diff/patch. Simpler and matches how the drawer's form naturally submits
/// (a small, complete list, not an incremental edit log).
/// </summary>
public record UpsertResidentRequest(
    OccupantType OccupantType,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string? ForwardingAddress,
    DateTimeOffset? NoticeGivenDate,
    bool ShowInDirectory,
    IReadOnlyList<EmergencyContactRequest> EmergencyContacts);

public record EmergencyContactResponse(Guid Id, string Name, string PhoneNumber, string? Relationship);

public record ResidentResponse(
    Guid Id,
    Guid PropertyId,
    Guid? UserId,
    OccupantType OccupantType,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string? ForwardingAddress,
    DateTimeOffset? NoticeGivenDate,
    bool ShowInDirectory,
    IReadOnlyList<EmergencyContactResponse> EmergencyContacts);
