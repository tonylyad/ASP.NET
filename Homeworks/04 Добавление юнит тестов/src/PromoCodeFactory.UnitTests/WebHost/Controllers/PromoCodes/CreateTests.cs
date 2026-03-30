using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PromoCodeFactory.Core.Abstractions.Repositories;
using PromoCodeFactory.Core.Domain.Administration;
using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.WebHost.Controllers;
using PromoCodeFactory.WebHost.Models.PromoCodes;
using Soenneker.Utils.AutoBogus;

namespace PromoCodeFactory.UnitTests.WebHost.Controllers.PromoCodes;

public class CreateTests
{
    private readonly Mock<IRepository<PromoCode>> _promoCodesRepositoryMock;
    private readonly Mock<IRepository<Customer>> _customersRepositoryMock;
    private readonly Mock<IRepository<CustomerPromoCode>> _customerPromoCodesRepositoryMock;
    private readonly Mock<IRepository<Partner>> _partnersRepositoryMock;
    private readonly Mock<IRepository<Preference>> _preferencesRepositoryMock;
    private readonly PromoCodesController _sut;

    public CreateTests()
    {
        _promoCodesRepositoryMock = new Mock<IRepository<PromoCode>>();
        _customersRepositoryMock = new Mock<IRepository<Customer>>();
        _customerPromoCodesRepositoryMock = new Mock<IRepository<CustomerPromoCode>>();
        _partnersRepositoryMock = new Mock<IRepository<Partner>>();
        _preferencesRepositoryMock = new Mock<IRepository<Preference>>();

        _sut = new PromoCodesController(
            _promoCodesRepositoryMock.Object,
            _customersRepositoryMock.Object,
            _customerPromoCodesRepositoryMock.Object,
            _partnersRepositoryMock.Object,
            _preferencesRepositoryMock.Object);
    }

    [Fact]
    public async Task Create_WhenPartnerNotFound_ReturnsNotFound()
    {
        // Arrange
        var request = CreateRequest();

        _partnersRepositoryMock
            .Setup(r => r.GetById(request.PartnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Partner?)null);

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();

        var notFoundResult = (NotFoundObjectResult)result.Result!;
        notFoundResult.Value.Should().BeOfType<ProblemDetails>();

        var problemDetails = (ProblemDetails)notFoundResult.Value!;
        problemDetails.Title.Should().Be("Partner not found");
        problemDetails.Detail.Should().Be($"Partner with Id {request.PartnerId} not found.");
    }

    [Fact]
    public async Task Create_WhenPreferenceNotFound_ReturnsNotFound()
    {
        // Arrange
        var partner = CreatePartnerWithActiveLimit(Guid.NewGuid(), limitValue: 10, issuedCount: 0);
        var request = CreateRequest(partnerId: partner.Id);

        _partnersRepositoryMock
            .Setup(r => r.GetById(request.PartnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _preferencesRepositoryMock
            .Setup(r => r.GetById(request.PreferenceId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Preference?)null);

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();

        var notFoundResult = (NotFoundObjectResult)result.Result!;
        notFoundResult.Value.Should().BeOfType<ProblemDetails>();

        var problemDetails = (ProblemDetails)notFoundResult.Value!;
        problemDetails.Title.Should().Be("Preference not found");
        problemDetails.Detail.Should().Be($"Preference with Id {request.PreferenceId} not found.");
    }

    [Fact]
    public async Task Create_WhenNoActiveLimit_ReturnsUnprocessableEntity()
    {
        // Arrange
        var partner = CreatePartnerWithoutActiveLimit(Guid.NewGuid());
        var preference = CreatePreference(Guid.NewGuid());
        var request = CreateRequest(
            partnerId: partner.Id,
            preferenceId: preference.Id);

        _partnersRepositoryMock
            .Setup(r => r.GetById(request.PartnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _preferencesRepositoryMock
            .Setup(r => r.GetById(request.PreferenceId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preference);

        _customersRepositoryMock
            .Setup(r => r.GetWhere(It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer>());

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();

        var objectResult = (ObjectResult)result.Result!;
        objectResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        objectResult.Value.Should().BeOfType<ProblemDetails>();

        var problemDetails = (ProblemDetails)objectResult.Value!;
        problemDetails.Title.Should().Be("No active limit");
        problemDetails.Detail.Should().Be("Partner has no active promo code limit.");
    }

    [Fact]
    public async Task Create_WhenLimitExceeded_ReturnsUnprocessableEntity()
    {
        // Arrange
        var partner = CreatePartnerWithActiveLimit(Guid.NewGuid(), limitValue: 5, issuedCount: 5);
        var preference = CreatePreference(Guid.NewGuid());
        var request = CreateRequest(
            partnerId: partner.Id,
            preferenceId: preference.Id);

        _partnersRepositoryMock
            .Setup(r => r.GetById(request.PartnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _preferencesRepositoryMock
            .Setup(r => r.GetById(request.PreferenceId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preference);

        _customersRepositoryMock
            .Setup(r => r.GetWhere(It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer>());

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();

        var objectResult = (ObjectResult)result.Result!;
        objectResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        objectResult.Value.Should().BeOfType<ProblemDetails>();

        var problemDetails = (ProblemDetails)objectResult.Value!;
        problemDetails.Title.Should().Be("Limit exceeded");
        problemDetails.Detail.Should().Be("Cannot create promo code. Limit would be exceeded (current: 5/5).");
    }

    [Fact]
    public async Task Create_WhenValidRequest_ReturnsCreatedAndIncrementsIssuedCount()
    {
        // Arrange
        var partner = CreatePartnerWithActiveLimit(Guid.NewGuid(), limitValue: 10, issuedCount: 2);
        var activeLimit = partner.PartnerLimits.Single();

        var preference = CreatePreference(Guid.NewGuid());
        var customer1 = CreateCustomer(Guid.NewGuid(), preference);
        var customer2 = CreateCustomer(Guid.NewGuid(), preference);

        var request = CreateRequest(
            partnerId: partner.Id,
            preferenceId: preference.Id);

        PromoCode? addedPromoCode = null;

        _partnersRepositoryMock
            .Setup(r => r.GetById(request.PartnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _preferencesRepositoryMock
            .Setup(r => r.GetById(request.PreferenceId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preference);

        _customersRepositoryMock
            .Setup(r => r.GetWhere(It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer> { customer1, customer2 });

        _promoCodesRepositoryMock
            .Setup(r => r.Add(It.IsAny<PromoCode>(), It.IsAny<CancellationToken>()))
            .Callback<PromoCode, CancellationToken>((promoCode, _) => addedPromoCode = promoCode)
            .Returns(Task.CompletedTask);

        _partnersRepositoryMock
            .Setup(r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.Create(request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<CreatedAtActionResult>();

        var createdResult = (CreatedAtActionResult)result.Result!;
        createdResult.ActionName.Should().Be(nameof(PromoCodesController.GetById));

        addedPromoCode.Should().NotBeNull();
        addedPromoCode!.Code.Should().Be(request.Code);
        addedPromoCode.ServiceInfo.Should().Be(request.ServiceInfo);
        addedPromoCode.Partner.Should().Be(partner);
        addedPromoCode.Preference.Should().Be(preference);
        addedPromoCode.BeginDate.Should().Be(request.BeginDate.UtcDateTime);
        addedPromoCode.EndDate.Should().Be(request.EndDate.UtcDateTime);

        addedPromoCode.CustomerPromoCodes.Should().HaveCount(2);
        addedPromoCode.CustomerPromoCodes.Select(cpc => cpc.CustomerId)
            .Should()
            .BeEquivalentTo([customer1.Id, customer2.Id]);

        addedPromoCode.CustomerPromoCodes.All(cpc => cpc.PromoCodeId == addedPromoCode.Id)
            .Should()
            .BeTrue();

        activeLimit.IssuedCount.Should().Be(3);

        _promoCodesRepositoryMock.Verify(
            r => r.Add(It.IsAny<PromoCode>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _partnersRepositoryMock.Verify(
            r => r.Update(partner, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static PromoCodeCreateRequest CreateRequest(Guid? partnerId = null, Guid? preferenceId = null)
    {
        return new PromoCodeCreateRequest(
            Code: $"CODE-{Guid.NewGuid():N}"[..12],
            ServiceInfo: "Test service info",
            PartnerId: partnerId ?? Guid.NewGuid(),
            BeginDate: DateTimeOffset.UtcNow,
            EndDate: DateTimeOffset.UtcNow.AddDays(30),
            PreferenceId: preferenceId ?? Guid.NewGuid());
    }

    private static Partner CreatePartnerWithActiveLimit(Guid partnerId, int limitValue, int issuedCount)
    {
        var role = new AutoFaker<Role>()
            .RuleFor(r => r.Id, _ => Guid.NewGuid())
            .Generate();

        var employee = new AutoFaker<Employee>()
            .RuleFor(e => e.Id, _ => Guid.NewGuid())
            .RuleFor(e => e.Role, _ => role)
            .Generate();

        var limits = new List<PartnerPromoCodeLimit>();

        var partner = new AutoFaker<Partner>()
            .RuleFor(p => p.Id, _ => partnerId)
            .RuleFor(p => p.IsActive, _ => true)
            .RuleFor(p => p.Manager, _ => employee)
            .RuleFor(p => p.PartnerLimits, _ => limits)
            .Generate();

        var activeLimit = new AutoFaker<PartnerPromoCodeLimit>()
            .RuleFor(l => l.Id, _ => Guid.NewGuid())
            .RuleFor(l => l.Partner, _ => partner)
            .RuleFor(l => l.CreatedAt, _ => DateTimeOffset.UtcNow.AddDays(-1))
            .RuleFor(l => l.EndAt, _ => DateTimeOffset.UtcNow.AddDays(10))
            .RuleFor(l => l.CanceledAt, _ => null)
            .RuleFor(l => l.Limit, _ => limitValue)
            .RuleFor(l => l.IssuedCount, _ => issuedCount)
            .Generate();

        limits.Add(activeLimit);

        return partner;
    }

    private static Partner CreatePartnerWithoutActiveLimit(Guid partnerId)
    {
        var role = new AutoFaker<Role>()
            .RuleFor(r => r.Id, _ => Guid.NewGuid())
            .Generate();

        var employee = new AutoFaker<Employee>()
            .RuleFor(e => e.Id, _ => Guid.NewGuid())
            .RuleFor(e => e.Role, _ => role)
            .Generate();

        var expiredLimit = new AutoFaker<PartnerPromoCodeLimit>()
            .RuleFor(l => l.Id, _ => Guid.NewGuid())
            .RuleFor(l => l.CreatedAt, _ => DateTimeOffset.UtcNow.AddDays(-10))
            .RuleFor(l => l.EndAt, _ => DateTimeOffset.UtcNow.AddDays(-1))
            .RuleFor(l => l.CanceledAt, _ => null)
            .RuleFor(l => l.Limit, _ => 10)
            .RuleFor(l => l.IssuedCount, _ => 2)
            .Generate();

        var canceledLimit = new AutoFaker<PartnerPromoCodeLimit>()
            .RuleFor(l => l.Id, _ => Guid.NewGuid())
            .RuleFor(l => l.CreatedAt, _ => DateTimeOffset.UtcNow.AddDays(-5))
            .RuleFor(l => l.EndAt, _ => DateTimeOffset.UtcNow.AddDays(5))
            .RuleFor(l => l.CanceledAt, _ => DateTimeOffset.UtcNow.AddDays(-1))
            .RuleFor(l => l.Limit, _ => 10)
            .RuleFor(l => l.IssuedCount, _ => 1)
            .Generate();

        var limits = new List<PartnerPromoCodeLimit> { expiredLimit, canceledLimit };

        var partner = new AutoFaker<Partner>()
            .RuleFor(p => p.Id, _ => partnerId)
            .RuleFor(p => p.IsActive, _ => true)
            .RuleFor(p => p.Manager, _ => employee)
            .RuleFor(p => p.PartnerLimits, _ => limits)
            .Generate();

        expiredLimit.Partner = partner;
        canceledLimit.Partner = partner;

        return partner;
    }

    private static Preference CreatePreference(Guid preferenceId)
    {
        return new AutoFaker<Preference>()
            .RuleFor(p => p.Id, _ => preferenceId)
            .Generate();
    }

    private static Customer CreateCustomer(Guid customerId, Preference preference)
    {
        return new AutoFaker<Customer>()
            .RuleFor(c => c.Id, _ => customerId)
            .RuleFor(c => c.Preferences, _ => new List<Preference> { preference })
            .Generate();
    }
}
