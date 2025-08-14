using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using FusionOps.Application.Dto;
using FusionOps.Application.Queries;
using FusionOps.Infrastructure.Persistence.Postgres;
using FusionOps.Infrastructure.Persistence.Postgres.Models;
using FusionOps.Infrastructure.Handlers;
using Xunit;

namespace FusionOps.Application.Tests.Handlers;

public class GetAllocationHistoryHandlerTests
{
    private readonly Mock<IDbContextFactory<FulfillmentContext>> _factoryMock;
    private readonly Mock<ILogger<GetAllocationHistoryHandler>> _loggerMock;
    private readonly GetAllocationHistoryHandler _handler;

    public GetAllocationHistoryHandlerTests()
    {
        _factoryMock = new Mock<IDbContextFactory<FulfillmentContext>>();
        _loggerMock = new Mock<ILogger<GetAllocationHistoryHandler>>();

        _handler = new GetAllocationHistoryHandler(_factoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnPagedResult_WhenValidQuery()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var query = new GetAllocationHistoryQuery(projectId, page: 1, pageSize: 10);
        
        var options = new DbContextOptionsBuilder<FulfillmentContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var ctx = new FulfillmentContext(options);

        var row1 = new AllocationHistoryRow
        {
            AllocationId = Guid.NewGuid(),
            ProjectId = projectId,
            Recorded = DateTime.UtcNow,
            FromTs = DateTime.UtcNow.AddHours(-2),
            ToTs = DateTime.UtcNow.AddHours(-1),
            ResourceId = Guid.NewGuid()
        };
        var row2 = new AllocationHistoryRow
        {
            AllocationId = Guid.NewGuid(),
            ProjectId = projectId,
            Recorded = DateTime.UtcNow.AddDays(-1),
            FromTs = DateTime.UtcNow.AddDays(-1).AddHours(-2),
            ToTs = DateTime.UtcNow.AddDays(-1).AddHours(-1),
            ResourceId = Guid.NewGuid()
        };

        ctx.AllocationHistory.AddRange(row1, row2);
        await ctx.SaveChangesAsync();

        _factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ctx);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }
}
