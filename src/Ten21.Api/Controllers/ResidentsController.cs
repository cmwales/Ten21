using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Residents;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-23/US-24: Tenant Profile Directory + Zero-Token Welcome & Provisioning. A
/// ResidentProfile always belongs to exactly one Property, addressed here as a nested
/// resource (api/properties/{propertyId}/residents) -- every action re-checks `PropertyId ==
/// propertyId` in its own query rather than trusting a bare {id} lookup, per CLAUDE.md's
/// BOLA/IDOR resource-based-authorization mandate (the tenant filter alone isn't sufficient:
/// a PM could otherwise probe/mutate a resident under the wrong property by guessing a
/// resident Id).
///
/// CreateResident provisions a real login for ANY resident with an email (primary or
/// secondary, confirmed explicitly with the founder -- not primary-only), via
/// ProvisionResidentLoginAsync below.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/residents")]
public class ResidentsController : ControllerBase
{
    /// <summary>
    /// US-24: hardcoded, not the configurable _frontendBaseUrl pattern AuthController uses
    /// for activation/reset links. Those links necessarily carry a token in the query
    /// string and so must be environment-aware; a login URL carries nothing, and the
    /// acceptance criteria's own wording ("directing users strictly to
    /// https://app.ten21.io/login") calls for the literal production URL every time.
    /// </summary>
    private const string WelcomeLoginUrl = "https://app.ten21.io/login";

    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IEmailSender _emailSender;

    public ResidentsController(
        Ten21DbContext dbContext,
        IInputSanitizer sanitizer,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IEmailSender emailSender)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
        _userManager = userManager;
        _roleManager = roleManager;
        _emailSender = emailSender;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Resident.Read)]
    public async Task<IActionResult> GetResidents(Guid propertyId, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var residents = await _dbContext.ResidentProfiles
            .Include(r => r.EmergencyContacts)
            .Where(r => r.PropertyId == propertyId)
            .OrderBy(r => r.OccupantType).ThenBy(r => r.LastName)
            .ToListAsync(cancellationToken);

        return Ok(residents.Select(r => ToResponse(r, r.EmergencyContacts.ToList())).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Read)]
    public async Task<IActionResult> GetResident(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var resident = await FindResidentAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Resident '{id}' was not found on this property.");

        return Ok(ToResponse(resident, resident.EmergencyContacts.ToList()));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> CreateResident(
        Guid propertyId, [FromBody] UpsertResidentRequest request, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);
        ValidateRequest(request);

        var resident = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            OccupantType = request.OccupantType,
            FirstName = _sanitizer.Sanitize(request.FirstName)!,
            LastName = _sanitizer.Sanitize(request.LastName)!,
            Email = NullIfBlank(_sanitizer.Sanitize(request.Email)),
            PhoneNumber = NullIfBlank(_sanitizer.Sanitize(request.PhoneNumber)),
            ForwardingAddress = NullIfBlank(_sanitizer.Sanitize(request.ForwardingAddress)),
            NoticeGivenDate = request.NoticeGivenDate,
            ShowInDirectory = request.ShowInDirectory,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var contacts = BuildEmergencyContacts(resident.Id, request.EmergencyContacts);
        foreach (var contact in contacts)
        {
            resident.EmergencyContacts.Add(contact);
        }

        _dbContext.ResidentProfiles.Add(resident);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // US-24: not wrapped in one atomic transaction with the ResidentProfile insert
        // above -- UserManager.CreateAsync manages its own SaveChanges internally, and
        // nesting that inside an explicit transaction here adds real complexity for a
        // failure mode (resident row created, provisioning failed) that's inconvenient but
        // not dangerous: the resident record stays valid with no UserId, and CreateResident
        // simply propagates the provisioning error to the caller.
        await ProvisionResidentLoginAsync(resident, cancellationToken);

        return CreatedAtAction(nameof(GetResident), new { propertyId, id = resident.Id }, ToResponse(resident, contacts));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> UpdateResident(
        Guid propertyId, Guid id, [FromBody] UpsertResidentRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var resident = await FindResidentAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Resident '{id}' was not found on this property.");

        resident.OccupantType = request.OccupantType;
        resident.FirstName = _sanitizer.Sanitize(request.FirstName)!;
        resident.LastName = _sanitizer.Sanitize(request.LastName)!;
        resident.Email = NullIfBlank(_sanitizer.Sanitize(request.Email));
        resident.PhoneNumber = NullIfBlank(_sanitizer.Sanitize(request.PhoneNumber));
        resident.ForwardingAddress = NullIfBlank(_sanitizer.Sanitize(request.ForwardingAddress));
        resident.NoticeGivenDate = request.NoticeGivenDate;
        resident.ShowInDirectory = request.ShowInDirectory;

        // Managed directly via the DbSet rather than resident.EmergencyContacts navigation
        // mutation (Clear()/Add()) -- a real test run caught that mixing navigation-collection
        // edits on an already-tracked, Include()-loaded parent produces unpredictable entity
        // states here (a freshly re-added contact ended up Modified instead of Added, tripping
        // ApplyTenantStamping's Modified-state ownership check since it had no TenantId yet).
        // Working against the DbSet directly sidesteps that relationship-fixup ambiguity
        // entirely -- explicit Added/Deleted states, no navigation-collection state to reason
        // about.
        var existingContacts = await _dbContext.EmergencyContacts
            .Where(c => c.ResidentProfileId == resident.Id)
            .ToListAsync(cancellationToken);
        _dbContext.EmergencyContacts.RemoveRange(existingContacts);

        var newContacts = BuildEmergencyContacts(resident.Id, request.EmergencyContacts);
        _dbContext.EmergencyContacts.AddRange(newContacts);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(resident, newContacts));
    }

    /// <summary>
    /// Always a soft delete (ResidentProfile is ISoftDelete, and AuditSaveChangesInterceptor
    /// converts the Remove() below automatically) -- unlike Property's US-22 payment-aware
    /// hard/soft branching, nothing here justifies ever hard-deleting occupant history, so
    /// there's no IHardDeleteOverride opt-out call needed.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> DeleteResident(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var resident = await FindResidentAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Resident '{id}' was not found on this property.");

        _dbContext.ResidentProfiles.Remove(resident);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// US-24: a resident with no email is left with UserId = null (profile-only, no login --
    /// intentional for e.g. a minor or an emergency-contact-only entry). A resident with an
    /// email always gets provisioned, primary or secondary alike.
    /// </summary>
    private async Task ProvisionResidentLoginAsync(ResidentProfile resident, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resident.Email))
        {
            return;
        }

        var existingUser = await _userManager.FindByEmailAsync(resident.Email);
        if (existingUser is not null)
        {
            await LinkExistingUserToTenantAsync(existingUser, resident, cancellationToken);
            return;
        }

        var tenantRole = await _roleManager.FindByNameAsync(RoleNames.Tenant)
            ?? throw new InvalidOperationException("Tenant role not seeded -- has RoleSeeder run?");

        var temporaryPassword = GenerateTemporaryPassword();
        var newUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = resident.Email,
            Email = resident.Email,
            EmailConfirmed = true, // the PM entering it directly stands in for self-confirmation
            FirstName = resident.FirstName,
            LastName = resident.LastName,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var createResult = await _userManager.CreateAsync(newUser, temporaryPassword);
        if (!createResult.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Email"] = createResult.Errors.Select(e => e.Description).ToArray(),
            });
        }

        // No manual TenantId assignment -- Ten21DbContext.ApplyTenantStamping auto-populates
        // it from the active tenant context on insert, same as every other ITenantScopedEntity
        // add in this codebase.
        _dbContext.TenantMemberships.Add(new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = newUser.Id,
            RoleId = tenantRole.Id,
            IsPrimary = true, // this new user's first (and, today, only) membership
            CreatedAt = DateTimeOffset.UtcNow,
        });

        resident.UserId = newUser.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _emailSender.SendAsync(
            resident.Email,
            "Welcome to Ten21",
            $"<p>You've been added as a resident on Ten21. Your temporary password is: " +
            $"<strong>{temporaryPassword}</strong></p>" +
            $"<p>Log in at <a href=\"{WelcomeLoginUrl}\">{WelcomeLoginUrl}</a> -- you'll be " +
            $"asked to choose a new password the first time you sign in.</p>",
            cancellationToken);
    }

    /// <summary>
    /// US-24: the same email already has an ApplicationUser -- most likely this person
    /// already rents from a different Property Manager. TenantMembership already supports
    /// "one login, many tenant/role pairs" by design (see its own class comment), so this
    /// links the existing account into this tenant rather than erroring or duplicating.
    /// Deliberately does NOT reset their password or set MustChangePassword -- they already
    /// have working credentials. Richer cross-PM UX (visibility across memberships,
    /// consent, conflict resolution) is deliberately deferred to a future sprint.
    /// </summary>
    private async Task LinkExistingUserToTenantAsync(
        ApplicationUser existingUser, ResidentProfile resident, CancellationToken cancellationToken)
    {
        var alreadyMember = await _dbContext.TenantMemberships
            .IgnoreQueryFilters() // this lookup spans every tenant this user belongs to, not just the active one
            .AnyAsync(tm => tm.UserId == existingUser.Id && tm.TenantId == resident.TenantId, cancellationToken);

        if (!alreadyMember)
        {
            var tenantRole = await _roleManager.FindByNameAsync(RoleNames.Tenant)
                ?? throw new InvalidOperationException("Tenant role not seeded -- has RoleSeeder run?");

            var hasAnyMembership = await _dbContext.TenantMemberships
                .IgnoreQueryFilters()
                .AnyAsync(tm => tm.UserId == existingUser.Id, cancellationToken);

            _dbContext.TenantMemberships.Add(new TenantMembership
            {
                Id = Guid.NewGuid(),
                UserId = existingUser.Id,
                RoleId = tenantRole.Id,
                IsPrimary = !hasAnyMembership,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        resident.UserId = existingUser.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _emailSender.SendAsync(
            resident.Email!,
            "You've been added as a Ten21 resident",
            $"<p>You've been added as a resident on Ten21, using your existing account.</p>" +
            $"<p>Log in as usual at <a href=\"{WelcomeLoginUrl}\">{WelcomeLoginUrl}</a>.</p>",
            cancellationToken);
    }

    /// <summary>
    /// US-24: meets ASP.NET Core Identity's default password policy (min length 6, at least
    /// one each of digit/lowercase/uppercase/non-alphanumeric) by construction -- one
    /// character from each required category, then random fill, then shuffled so the
    /// guaranteed categories aren't always in the same positions. Excludes visually
    /// ambiguous characters (0/O, 1/I/l) since this is read out of an email and typed back
    /// in by hand.
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        const string uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lowercase = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*";
        const string all = uppercase + lowercase + digits + special;

        var chars = new char[12];
        chars[0] = uppercase[RandomNumberGenerator.GetInt32(uppercase.Length)];
        chars[1] = lowercase[RandomNumberGenerator.GetInt32(lowercase.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = special[RandomNumberGenerator.GetInt32(special.Length)];
        for (var i = 4; i < chars.Length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private async Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Property '{propertyId}' was not found.");
        }
    }

    private async Task<ResidentProfile?> FindResidentAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.ResidentProfiles
            .Include(r => r.EmergencyContacts)
            .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == id, cancellationToken);

    private List<EmergencyContact> BuildEmergencyContacts(Guid residentId, IReadOnlyList<EmergencyContactRequest> contacts) =>
        contacts.Select(contact => new EmergencyContact
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = residentId,
            Name = _sanitizer.Sanitize(contact.Name)!,
            PhoneNumber = _sanitizer.Sanitize(contact.PhoneNumber)!,
            Relationship = NullIfBlank(_sanitizer.Sanitize(contact.Relationship)),
            CreatedAt = DateTimeOffset.UtcNow,
        }).ToList();

    private static void ValidateRequest(UpsertResidentRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors["FirstName"] = ["First name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            errors["LastName"] = ["Last name is required."];
        }

        if (request.Email is { Length: > 256 })
        {
            errors["Email"] = ["Email must be 256 characters or fewer."];
        }

        for (var i = 0; i < request.EmergencyContacts.Count; i++)
        {
            var contact = request.EmergencyContacts[i];
            if (string.IsNullOrWhiteSpace(contact.Name))
            {
                errors[$"EmergencyContacts[{i}].Name"] = ["Emergency contact name is required."];
            }

            if (string.IsNullOrWhiteSpace(contact.PhoneNumber))
            {
                errors[$"EmergencyContacts[{i}].PhoneNumber"] = ["Emergency contact phone number is required."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ResidentResponse ToResponse(ResidentProfile resident, IReadOnlyList<EmergencyContact> contacts) => new(
        resident.Id,
        resident.PropertyId,
        resident.UserId,
        resident.OccupantType,
        resident.FirstName,
        resident.LastName,
        resident.Email,
        resident.PhoneNumber,
        resident.ForwardingAddress,
        resident.NoticeGivenDate,
        resident.ShowInDirectory,
        contacts.Select(c => new EmergencyContactResponse(c.Id, c.Name, c.PhoneNumber, c.Relationship)).ToList());
}
