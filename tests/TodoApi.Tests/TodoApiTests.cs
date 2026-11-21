using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoApi.Todos;

namespace TodoApi.Tests;

/// <summary>
/// Integration tests: each test starts the whole API in memory (routing, JSON,
/// validation, dependency injection) and talks to it over HTTP, exactly like a
/// real client would. No ports and no separate process are needed.
///
/// xUnit creates a new instance of this class for every test, so every test gets
/// its own fresh API with an empty to-do list and can't affect the others.
/// </summary>
public sealed class TodoApiTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly HttpClient _client;

    public TodoApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<TodoItem> CreateAsync(string title)
    {
        var response = await _client.PostAsJsonAsync("/todos", new CreateTodoRequest(title));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TodoItem>())!;
    }

    // ---- Operational endpoints ----------------------------------------------

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Version_ReportsVersionAndEnvironment()
    {
        var info = await _client.GetFromJsonAsync<JsonElement>("/version");

        Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("version").GetString()));
        Assert.Equal("Development", info.GetProperty("environment").GetString());
    }

    // ---- Create and read ----------------------------------------------------

    [Fact]
    public async Task GetAll_StartsEmpty()
    {
        var todos = await _client.GetFromJsonAsync<List<TodoItem>>("/todos");

        Assert.NotNull(todos);
        Assert.Empty(todos);
    }

    [Fact]
    public async Task Post_CreatesTodo_AndReturns201WithLocation()
    {
        var response = await _client.PostAsJsonAsync("/todos", new CreateTodoRequest("  Buy milk  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/todos/1", response.Headers.Location?.ToString());

        var created = await response.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(created);
        Assert.Equal(1, created.Id);
        Assert.Equal("Buy milk", created.Title);   // surrounding spaces trimmed
        Assert.False(created.IsCompleted);
    }

    [Fact]
    public async Task GetById_ReturnsTheCreatedTodo()
    {
        var created = await CreateAsync("Write tests");

        var fetched = await _client.GetFromJsonAsync<TodoItem>($"/todos/{created.Id}");

        Assert.Equal(created, fetched);   // records compare by value
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/todos/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Validation ---------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Post_WithoutTitle_Returns400WithTitleError(string? title)
    {
        var response = await _client.PostAsJsonAsync("/todos", new CreateTodoRequest(title));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("title", out _));
    }

    [Fact]
    public async Task Post_TitleTooLong_Returns400()
    {
        var title = new string('x', TodoEndpoints.MaxTitleLength + 1);

        var response = await _client.PostAsJsonAsync("/todos", new CreateTodoRequest(title));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_TitleAtMaxLength_IsAccepted()
    {
        var title = new string('x', TodoEndpoints.MaxTitleLength);

        var response = await _client.PostAsJsonAsync("/todos", new CreateTodoRequest(title));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- Update and filter --------------------------------------------------

    [Fact]
    public async Task Put_UpdatesTitleAndCompletes()
    {
        var created = await CreateAsync("Draft");

        var response = await _client.PutAsJsonAsync($"/todos/{created.Id}", new UpdateTodoRequest("Final", true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TodoItem>();
        Assert.NotNull(updated);
        Assert.Equal("Final", updated.Title);
        Assert.True(updated.IsCompleted);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);   // creation time unchanged
    }

    [Fact]
    public async Task Put_UnknownId_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/todos/999", new UpdateTodoRequest("Anything", false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_FiltersByCompleted()
    {
        var done = await CreateAsync("Done");
        await CreateAsync("Not done");
        await _client.PutAsJsonAsync($"/todos/{done.Id}", new UpdateTodoRequest("Done", true));

        var completed = await _client.GetFromJsonAsync<List<TodoItem>>("/todos?completed=true");
        var open = await _client.GetFromJsonAsync<List<TodoItem>>("/todos?completed=false");
        var all = await _client.GetFromJsonAsync<List<TodoItem>>("/todos");

        Assert.Equal(new[] { "Done" }, completed!.Select(t => t.Title));
        Assert.Equal(new[] { "Not done" }, open!.Select(t => t.Title));
        Assert.Equal(2, all!.Count);
    }

    // ---- Delete -------------------------------------------------------------

    [Fact]
    public async Task Delete_RemovesTodo_ThenSecondDeleteReturns404()
    {
        var created = await CreateAsync("Temporary");

        var first = await _client.DeleteAsync($"/todos/{created.Id}");
        var afterwards = await _client.GetAsync($"/todos/{created.Id}");
        var second = await _client.DeleteAsync($"/todos/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    // ---- Concurrency ----------------------------------------------------------

    [Fact]
    public async Task ManyParallelPosts_AllGetUniqueIds()
    {
        // A web server handles requests in parallel. A plain Dictionary and an
        // int counter would lose items or hand out duplicate IDs here.
        const int Count = 200;

        var responses = await Task.WhenAll(Enumerable.Range(0, Count)
            .Select(i => _client.PostAsJsonAsync("/todos", new CreateTodoRequest($"Task {i}"))));
        var items = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<TodoItem>()));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(Count, items.Select(t => t!.Id).Distinct().Count());
        Assert.Equal(Count, (await _client.GetFromJsonAsync<List<TodoItem>>("/todos"))!.Count);
    }
}