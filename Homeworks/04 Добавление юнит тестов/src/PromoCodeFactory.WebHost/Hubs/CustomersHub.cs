using Microsoft.AspNetCore.SignalR;
using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.WebHost.Models.Customers;
using PromoCodeFactory.WebHost.Services;

namespace PromoCodeFactory.WebHost.Hubs;

public sealed class CustomersHub(CustomerService customers) : Hub
{
    public Task<IReadOnlyCollection<CustomerShortResponse>> GetAll() =>
        customers.GetAll(Context.ConnectionAborted);

    public Task<CustomerShortResponse> GetById(Guid id) =>
        Execute(() => customers.GetById(id, Context.ConnectionAborted));

    public async Task<CustomerShortResponse> Create(CustomerCreateRequest request)
    {
        var customer = await Execute(() => customers.Create(request, Context.ConnectionAborted));
        await Clients.Others.SendAsync("CustomerCreated", customer, Context.ConnectionAborted);
        return customer;
    }

    public async Task<CustomerShortResponse> Update(Guid id, CustomerUpdateRequest request)
    {
        var customer = await Execute(() => customers.Update(id, request, Context.ConnectionAborted));
        await Clients.Others.SendAsync("CustomerUpdated", customer, Context.ConnectionAborted);
        return customer;
    }

    public async Task Delete(Guid id)
    {
        await Execute(async () =>
        {
            await customers.Delete(id, Context.ConnectionAborted);
            return true;
        });
        await Clients.Others.SendAsync("CustomerDeleted", id, Context.ConnectionAborted);
    }

    private static async Task<T> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (EntityNotFoundException exception)
        {
            throw new HubException(exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw new HubException(exception.Message);
        }
    }
}
