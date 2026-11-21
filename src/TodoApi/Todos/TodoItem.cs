namespace TodoApi.Todos;

public sealed record TodoItem(int Id, string Title, bool IsCompleted, DateTimeOffset CreatedAt);

public sealed record CreateTodoRequest(string? Title);

public sealed record UpdateTodoRequest(string? Title, bool IsCompleted);