using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PromoCodeFactory.Core.Abstractions.Repositories;
using PromoCodeFactory.Core.Domain.Administration;
using PromoCodeFactory.Core.Domain.PromoCodeManagement;
using PromoCodeFactory.Core.Exceptions;
using PromoCodeFactory.WebHost.Controllers;
using PromoCodeFactory.WebHost.Models.Partners;
using Soenneker.Utils.AutoBogus;

namespace PromoCodeFactory.UnitTests.WebHost.Controllers.Partners;

public class SetLimitTests
{
    private readonly Mock<IRepository<Partner>> _partnersRepositoryMock;
    private readonly Mock<IRepository<PartnerPromoCodeLimit>> _partnerLimitsRepositoryMock;
    private readonly PartnersController _sut;
    public SetLimitTests()
    {
        _partnersRepositoryMock = new Mock<IRepository<Partner>>();
        _partnerLimitsRepositoryMock = new Mock<IRepository<PartnerPromoCodeLimit>>();
        _sut = new PartnersController(_partnersRepositoryMock.Object, _partnerLimitsRepositoryMock.Object);
    }

    [Fact]
    public async Task CreateLimit_WhenPartnerNotFound_ReturnsNotFound()
    {
        // Arrange
        var partnerId = Guid.NewGuid();
        var request = new PartnerPromoCodeLimitCreateRequest(
            DateTimeOffset.UtcNow.AddDays(30),
            100);

        _partnersRepositoryMock
            .Setup(r => r.GetById(partnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Partner?)null);

        // Act
        var result = await _sut.CreateLimit(partnerId, request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();

        var notFoundResult = (NotFoundObjectResult)result.Result!;
        notFoundResult.Value.Should().BeOfType<ProblemDetails>();

        var problemDetails = (ProblemDetails)notFoundResult.Value!;
        problemDetails.Title.Should().Be("Partner not found");
        problemDetails.Detail.Should().Be($"Partner with Id {partnerId} not found.");
    }

    [Fact]
    public async Task CreateLimit_WhenPartnerBlocked_ReturnsUnprocessableEntity()
    {
        // Arrange
        var partnerId = Guid.NewGuid();
        var partner = CreatePartner(partnerId, isActive: false);
        var request = new PartnerPromoCodeLimitCreateRequest(
            DateTimeOffset.UtcNow.AddDays(30),
            100);

        _partnersRepositoryMock
            .Setup(r => r.GetById(partnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        // Act
        var result = await _sut.CreateLimit(partnerId, request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<UnprocessableEntityObjectResult>();

        var objectResult = (UnprocessableEntityObjectResult)result.Result!;
        objectResult.Value.Should().BeOfType<ProblemDetails>();

        var problemDetails = (ProblemDetails)objectResult.Value!;
        problemDetails.Title.Should().Be("Partner blocked");
        problemDetails.Detail.Should().Be("Cannot create limit for a blocked partner.");
    }

    [Fact]
    public async Task CreateLimit_WhenValidRequest_ReturnsCreatedAndAddsLimit()
    {
        // Arrange
        var partnerId = Guid.NewGuid();
        var partner = CreatePartner(partnerId, isActive: true);
        var request = new PartnerPromoCodeLimitCreateRequest(
            DateTimeOffset.UtcNow.AddDays(30),
            100);

        PartnerPromoCodeLimit? addedLimit = null;

        _partnersRepositoryMock
            .Setup(r => r.GetById(partnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _partnerLimitsRepositoryMock
            .Setup(r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerPromoCodeLimit, CancellationToken>((limit, _) => addedLimit = limit)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CreateLimit(partnerId, request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<CreatedAtActionResult>();

        var createdResult = (CreatedAtActionResult)result.Result!;
        createdResult.ActionName.Should().Be(nameof(PartnersController.GetLimit));
        createdResult.RouteValues.Should().ContainKey("partnerId");
        createdResult.RouteValues!["partnerId"].Should().Be(partnerId);

        addedLimit.Should().NotBeNull();
        addedLimit!.Partner.Should().Be(partner);
        addedLimit.Limit.Should().Be(request.Limit);
        addedLimit.EndAt.Should().Be(request.EndAt);
        addedLimit.IssuedCount.Should().Be(0);
        addedLimit.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        createdResult.RouteValues.Should().ContainKey("limitId");
        createdResult.RouteValues!["limitId"].Should().Be(addedLimit.Id);

        createdResult.Value.Should().BeOfType<PartnerPromoCodeLimitResponse>();
        var response = (PartnerPromoCodeLimitResponse)createdResult.Value!;
        response.Id.Should().Be(addedLimit.Id);
        response.Limit.Should().Be(request.Limit);
        response.IssuedCount.Should().Be(0);

        _partnerLimitsRepositoryMock.Verify(
            r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _partnersRepositoryMock.Verify(
            r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateLimit_WhenValidRequestWithActiveLimits_CancelsOldLimitsAndAddsNew()
    {
        // Arrange
        var partnerId = Guid.NewGuid();
        var activeLimit1 = CreateLimit(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(10), canceledAt: null, limit: 10);
        var activeLimit2 = CreateLimit(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(20), canceledAt: null, limit: 20);
        var canceledLimit = CreateLimit(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(5), canceledAt: DateTimeOffset.UtcNow.AddDays(-1), limit: 5);

        var partner = CreatePartner(partnerId, true, [activeLimit1, activeLimit2, canceledLimit]);

        foreach (var limit in partner.PartnerLimits)
        {
            limit.Partner = partner;
        }

        var request = new PartnerPromoCodeLimitCreateRequest(
            DateTimeOffset.UtcNow.AddDays(30),
            100);

        PartnerPromoCodeLimit? addedLimit = null;

        _partnersRepositoryMock
            .Setup(r => r.GetById(partnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _partnersRepositoryMock
            .Setup(r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _partnerLimitsRepositoryMock
            .Setup(r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerPromoCodeLimit, CancellationToken>((limit, _) => addedLimit = limit)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CreateLimit(partnerId, request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<CreatedAtActionResult>();

        activeLimit1.CanceledAt.Should().NotBeNull();
        activeLimit2.CanceledAt.Should().NotBeNull();
        canceledLimit.CanceledAt.Should().NotBeNull();

        addedLimit.Should().NotBeNull();
        addedLimit!.CanceledAt.Should().BeNull();
        addedLimit.Limit.Should().Be(request.Limit);

        _partnersRepositoryMock.Verify(
            r => r.Update(partner, It.IsAny<CancellationToken>()),
            Times.Once);

        _partnerLimitsRepositoryMock.Verify(
            r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateLimit_WhenUpdateThrowsEntityNotFoundException_ReturnsNotFound()
    {
        // Arrange
        var partnerId = Guid.NewGuid();
        var existingActiveLimit = CreateLimit(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(10), canceledAt: null, limit: 10);
        var partner = CreatePartner(partnerId, true, [existingActiveLimit]);

        existingActiveLimit.Partner = partner;

        var request = new PartnerPromoCodeLimitCreateRequest(
            DateTimeOffset.UtcNow.AddDays(30),
            100);

        _partnersRepositoryMock
            .Setup(r => r.GetById(partnerId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partner);

        _partnersRepositoryMock
            .Setup(r => r.Update(It.IsAny<Partner>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntityNotFoundException<Partner>(partnerId));

        // Act
        var result = await _sut.CreateLimit(partnerId, request, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();

        _partnerLimitsRepositoryMock.Verify(
            r => r.Add(It.IsAny<PartnerPromoCodeLimit>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Partner CreatePartner(Guid partnerId, bool isActive, List<PartnerPromoCodeLimit>? limits = null)
    {
        var role = new AutoFaker<Role>()
            .RuleFor(r => r.Id, _ => Guid.NewGuid())
            .Generate();

        var employee = new AutoFaker<Employee>()
            .RuleFor(e => e.Id, _ => Guid.NewGuid())
            .RuleFor(e => e.Role, role)
            .Generate();

        limits ??= [];

        var partner = new AutoFaker<Partner>()
            .RuleFor(p => p.Id, _ => partnerId)
            .RuleFor(p => p.IsActive, _ => isActive)
            .RuleFor(p => p.Manager, _ => employee)
            .RuleFor(p => p.PartnerLimits, _ => limits)
            .Generate();

        return partner;
    }

    private static PartnerPromoCodeLimit CreateLimit(
        Guid limitId,
        DateTimeOffset endAt,
        DateTimeOffset? canceledAt,
        int limit)
    {
        return new AutoFaker<PartnerPromoCodeLimit>()
            .RuleFor(l => l.Id, _ => limitId)
            .RuleFor(l => l.CreatedAt, _ => DateTimeOffset.UtcNow.AddDays(-1))
            .RuleFor(l => l.EndAt, _ => endAt)
            .RuleFor(l => l.CanceledAt, _ => canceledAt)
            .RuleFor(l => l.Limit, _ => limit)
            .RuleFor(l => l.IssuedCount, _ => 0)
            .Generate();
    }
}
