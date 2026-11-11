using System.Text.Json.Serialization;

using KnowledgeAssistant.Api.Auth;
using KnowledgeAssistant.Api.Endpoints;
using KnowledgeAssistant.Api.ErrorHandling;
using KnowledgeAssistant.Api.RateLimiting;
using KnowledgeAssistant.Application;
using KnowledgeAssistant.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    // Machine-readable logs outside the developer console.
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddKnowledgeAuthentication();
builder.Services.AddKnowledgeAuthorization();

builder.Services.AddModelRateLimiting(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

await app.Services.InitializeKnowledgeStoreAsync();

app.UseExceptionHandler();
// Gives bodiless error responses (401, 403, 404 for unknown routes) the same problem+json shape.
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<UserLogScopeMiddleware>();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthEndpoints();
app.MapDocumentEndpoints();
app.MapChatEndpoints();
app.MapQuestionEndpoints();

await app.RunAsync();
