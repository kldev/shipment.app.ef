using Shipment.Domain.Common;

namespace Shipment.Domain.Crm;

/// <summary>Who receives orders; the e-mail (unique) is where the notifications go.</summary>
public sealed class Customer : Audited
{
    private readonly List<CustomerAddress> _addresses = [];

    private Customer() { }

    public Customer(Guid id, string displayName, string email, string? phone)
    {
        Id = id;
        DisplayName = displayName;
        Email = email;
        Phone = phone;
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; } = "";

    public string Email { get; private set; } = "";

    public string? Phone { get; private set; }

    public IReadOnlyList<CustomerAddress> Addresses => _addresses;

    public bool Same(string displayName, string email, string? phone) =>
        DisplayName == displayName && Email == email && Phone == phone;

    public bool Update(string displayName, string email, string? phone)
    {
        if (Same(displayName, email, phone))
            return false;
        (DisplayName, Email, Phone) = (displayName, email, phone);
        return true;
    }

    /// <summary>
    /// A new delivery address. The first one becomes the default; a new default takes the flag from the old one (the
    /// addresses must be loaded).
    /// </summary>
    public CustomerAddress AddAddress(
        Guid id,
        string label,
        Place place,
        string? notes,
        bool isDefault
    )
    {
        var makeDefault = isDefault || _addresses.Count == 0;
        if (makeDefault)
            ClearDefault();
        var address = new CustomerAddress(id, Id, label, place, notes, makeDefault);
        _addresses.Add(address);
        return address;
    }

    /// <summary>Edits an address; making it the default takes the flag from the old one, the default cannot be unset.</summary>
    public bool UpdateAddress(
        CustomerAddress address,
        string label,
        Place place,
        string? notes,
        bool makeDefault
    )
    {
        var isDefault = address.IsDefault || makeDefault;
        if (address.Same(label, place, notes) && address.IsDefault == isDefault)
            return false;
        if (isDefault && !address.IsDefault)
            ClearDefault();
        address.Update(label, place, notes, isDefault);
        return true;
    }

    private void ClearDefault()
    {
        foreach (var other in _addresses.Where(a => a.IsDefault))
            other.SetDefault(false);
    }
}
