using System.Net;
using EReader_API.Api.Tests.Fixtures;
using FluentAssertions;

namespace EReader_API.Api.Tests;

public class HealthEndpointsTests(EReaderApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetReady_DatabaseAndStorageHealthy_Returns200()
    {
        var response = await Client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLive_AlwaysReturns200()
    {
        var response = await Client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
