namespace TodoApi.Todos;

public static class TodoEndpoints
{
    public const int MaxTitleLength = 200;

    public static RouteGroupBuilder MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/todos").WithTags("Todos");

        // GET /todos  or  GET /todos?completed=true
        group.MapGet("/", (bool? completed, ITodoRepository repo) =>
            completed is null
                ? repo.GetAll()
                : repo.GetAll().Where(t => t.IsCompleted == completed).ToList());

        // GET /todos/5
        group.MapGet("/{id:int}", (int id, ITodoRepository repo) =>
            repo.Get(id) is { } item ? Results.Ok(item) : Results.NotFound());

        // POST /todos   { "title": "Buy milk" }
        group.MapPost("/", (CreateTodoRequest request, ITodoRepository repo) =>
        {
            if (ValidateTitle(request.Title) is { } errors) return Results.ValidationProblem(errors);

            var item = repo.Add(request.Title!.Trim());
            return Results.Created($"/todos/{item.Id}", item);
        });

        // PUT /todos/5   { "title": "Buy milk", "isCompleted": true }
        group.MapPut("/{id:int}", (int id, UpdateTodoRequest request, ITodoRepository repo) =>
        {
            if (ValidateTitle(request.Title) is { } errors) return Results.ValidationProblem(errors);

            return repo.Update(id, request.Title!.Trim(), request.IsCompleted) is { } item
                ? Results.Ok(item)
                : Results.NotFound();
        });

        // DELETE /todos/5
        group.MapDelete("/{id:int}", (int id, ITodoRepository repo) =>
            repo.Delete(id) ? Results.NoContent() : Results.NotFound());

        // GET /todos/stats
        group.MapGet("/stats", (ITodoRepository repo) =>
        {
            var all = repo.GetAll();
            var completed = all.Count(t => t.IsCompleted);
            return new { Total = all.Count, Completed = completed, Open = all.Count - completed };
        });

        return group;
    }

    private static Dictionary<string, string[]>? ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return new() { ["title"] = ["Title is required."] };

        if (title.Trim().Length > MaxTitleLength)
            return new() { ["title"] = [$"Title must be {MaxTitleLength} characters or fewer."] };

        return null;
    }
}