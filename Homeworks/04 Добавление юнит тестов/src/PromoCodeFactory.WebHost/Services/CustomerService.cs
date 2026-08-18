using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.WebHost.Mapping;
using PromoCodeFactory.WebHost.Models.Customers;

namespace PromoCodeFactory.WebHost.Services;

public sealed class CustomerService(
    IRepository<Customer> customersRepository,
    IRepository<Preference> preferencesRepository)
{
    public async Task<IReadOnlyCollection<CustomerShortResponse>> GetAll(CancellationToken ct) =>
        (await customersRepository.GetAll(true, ct))
        .Select(CustomersMapper.ToCustomerShortResponse)
        .ToArray();

    public async Task<CustomerShortResponse> GetById(Guid id, CancellationToken ct)
    {
        var customer = await customersRepository.GetById(id, true, ct)
            ?? throw new EntityNotFoundException<Customer>(id);
        return CustomersMapper.ToCustomerShortResponse(customer);
    }

    public async Task<CustomerShortResponse> Create(CustomerCreateRequest request, CancellationToken ct)
    {
        Validate(request.FirstName, request.LastName, request.Email, request.PreferenceIds);
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Preferences = await GetPreferences(request.PreferenceIds, ct)
        };
        await customersRepository.Add(customer, ct);
        return CustomersMapper.ToCustomerShortResponse(customer);
    }

    public async Task<CustomerShortResponse> Update(Guid id, CustomerUpdateRequest request, CancellationToken ct)
    {
        Validate(request.FirstName, request.LastName, request.Email, request.PreferenceIds);
        var customer = await customersRepository.GetById(id, true, ct)
            ?? throw new EntityNotFoundException<Customer>(id);
        customer.FirstName = request.FirstName;
        customer.LastName = request.LastName;
        customer.Email = request.Email;
        customer.Preferences = await GetPreferences(request.PreferenceIds, ct);
        await customersRepository.Update(customer, ct);
        return CustomersMapper.ToCustomerShortResponse(customer);
    }

    public Task Delete(Guid id, CancellationToken ct) => customersRepository.Delete(id, ct);

    private async Task<ICollection<Preference>> GetPreferences(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var preferences = await preferencesRepository.GetByRangeId(ids.Distinct(), false, ct);
        var requestedIds = ids.Distinct().ToArray();
        var missingId = requestedIds.FirstOrDefault(id => preferences.All(p => p.Id != id));
        if (missingId != Guid.Empty)
            throw new ArgumentException($"Preference with Id {missingId} not found.");
        return preferences.ToList();
    }

    private static void Validate(string firstName, string lastName, string email, Guid[] preferenceIds)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("First name and last name are required.");
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            throw new ArgumentException("Email has an invalid format.");
        if (preferenceIds.Length == 0)
            throw new ArgumentException("At least one preference is required.");
    }
}
