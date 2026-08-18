using PromoCodeFactory.DataAccess;
using PromoCodeFactory.WebHost.Grpc;
using PromoCodeFactory.WebHost.Hubs;
using PromoCodeFactory.WebHost.Services;

var builder = WebApplication.CreateBuilder();

builder.Services.AddEfDataAccess();

builder.Services.AddProblemDetails();
builder.Services.AddRouting(options =>
{
    options.LowercaseUrls = true;
});
builder.Services.AddControllers();
builder.Services.AddGrpc();
builder.Services.AddSignalR();
builder.Services.AddScoped<CustomerService>();

builder.Services.AddOpenApi(builder.Environment);

var app = builder.Build();

app.UseExceptionHandler();

app.MapOpenApi();
app.MapSwaggerUI();

app.UseHttpsRedirection();

app.MapControllers();
app.MapGrpcService<CustomersGrpcService>();
app.MapHub<CustomersHub>("/hubs/customers");

app.MigrateDatabase();

if (app.Environment.IsDevelopment())
    await app.SeedDatabase();

app.Run();
