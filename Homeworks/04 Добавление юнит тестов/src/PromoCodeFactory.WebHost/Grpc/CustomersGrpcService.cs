using Grpc.Core;
using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.WebHost.Grpc.Contracts;
using PromoCodeFactory.WebHost.Models.Customers;
using PromoCodeFactory.WebHost.Services;

namespace PromoCodeFactory.WebHost.Grpc;

public sealed class CustomersGrpcService(CustomerService customers) : CustomersApi.CustomersApiBase
{
    public override async Task<CustomersReply> GetAll(GetAllCustomersRequest request, ServerCallContext context)
    {
        var reply = new CustomersReply();
        reply.Customers.AddRange((await customers.GetAll(context.CancellationToken)).Select(ToReply));
        return reply;
    }

    public override async Task<CustomerReply> GetById(CustomerByIdRequest request, ServerCallContext context) =>
        await Execute(() => customers.GetById(ParseId(request.Id), context.CancellationToken));

    public override async Task<CustomerReply> Create(SaveCustomerRequest request, ServerCallContext context) =>
        await Execute(() => customers.Create(new CustomerCreateRequest(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PreferenceIds.Select(ParseId).ToArray()), context.CancellationToken));

    public override async Task<CustomerReply> Update(UpdateCustomerRequest request, ServerCallContext context) =>
        await Execute(() => customers.Update(ParseId(request.Id), new CustomerUpdateRequest(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PreferenceIds.Select(ParseId).ToArray()), context.CancellationToken));

    public override async Task<DeleteCustomerReply> Delete(CustomerByIdRequest request, ServerCallContext context)
    {
        try
        {
            await customers.Delete(ParseId(request.Id), context.CancellationToken);
            return new DeleteCustomerReply { Deleted = true };
        }
        catch (EntityNotFoundException exception)
        {
            throw new RpcException(new Status(StatusCode.NotFound, exception.Message));
        }
    }

    private static async Task<CustomerReply> Execute(Func<Task<CustomerShortResponse>> action)
    {
        try
        {
            return ToReply(await action());
        }
        catch (EntityNotFoundException exception)
        {
            throw new RpcException(new Status(StatusCode.NotFound, exception.Message));
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{value}' is not a valid UUID."));
        return id;
    }

    private static CustomerReply ToReply(CustomerShortResponse customer)
    {
        var reply = new CustomerReply
        {
            Id = customer.Id.ToString(),
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email
        };
        reply.Preferences.AddRange(customer.Preferences.Select(p => new PreferenceReply
        {
            Id = p.Id.ToString(),
            Name = p.Name
        }));
        return reply;
    }
}
