using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using FusionOps.Application.Dto;
using FusionOps.Application.Handlers;
using FusionOps.Application.Queries;
using FusionOps.Infrastructure.Persistence.Postgres;
using Xunit;

namespace FusionOps.Application.Tests.Handlers;

public class GetAllocationHistoryHandlerTests
{
    private readonly Mock<IDbContextFactory<FulfillmentContext>> _factoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<GetAllocationHistoryHandler>> _loggerMock;
    private readonly GetAllocationHistoryHandler _handler;

    public GetAllocationHistoryHandlerTests()
    {
        _factoryMock = new Mock<IDbContextFactory<FulfillmentContext>>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<GetAllocationHistoryHandler>>();
        _handler = new GetAllocationHistoryHandler(_factoryMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnPagedResult_WhenValidQuery()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var query = new GetAllocationHistoryQuery(projectId, page: 1, pageSize: 10);
        
        var historyRows = new List<AllocationHistoryRow>
        {
            new() { AllocationId = Guid.NewGuid(), ProjectId = projectId, Recorded = DateTime.UtcNow },
            new() { AllocationId = Guid.NewGuid(), ProjectId = projectId, Recorded = DateTime.UtcNow.AddDays(-1) }
        };

        var dtos = historyRows.Select(r => new AllocationHistoryDto 
        { 
            AllocationId = r.AllocationId, 
            Recorded = r.Recorded 
        }).ToList();

        var contextMock = new Mock<FulfillmentContext>();
        var dbSetMock = new Mock<DbSet<AllocationHistoryRow>>();
        
        dbSetMock.As<IQueryable<AllocationHistoryRow>>()
            .Setup(m => m.Provider).Returns(historyRows.AsQueryable().Provider);
        dbSetMock.As<IQueryable<AllocationHistoryRow>>()
            .Setup(m => m.Expression).Returns(historyRows.AsQueryable().Expression);
        dbSetMock.As<IQueryable<AllocationHistoryRow>>()
            .Setup(m => m.ElementType).Returns(historyRows.AsQueryable().ElementType);
        dbSetMock.As<IQueryable<AllocationHistoryRow>>()
            .Setup(m => m.GetEnumerator()).Returns(historyRows.GetEnumerator());

        contextMock.Setup(c => c.AllocationHistory).Returns(dbSetMock.Object);
        
        _factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextMock.Object);
        
        _mapperMock.Setup(m => m.Map<List<AllocationHistoryDto>>(It.IsAny<List<AllocationHistoryRow>>()))
            .Returns(dtos);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }
}
