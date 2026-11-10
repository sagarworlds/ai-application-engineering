using System.Text.Json.Serialization;

using KnowledgeAssistant.Api.Auth;
using KnowledgeAssistant.Api.Endpoints;
using KnowledgeAssistant.Api.ErrorHandling;
using KnowledgeAssistant.Application;
using KnowledgeAssistant.Infrastructure;

using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    // Machine-readable logs outside the developer console.
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Token validation settings (Authority, ValidAudiences, signing keys for local dev tokens) are bound
// from configuration section "Authentication:Schemes:Bearer". Production: Microsoft Entra ID authority.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    // Keep raw JWT claim names ("tid", "roles", ...) instead of legacy XML-SOAP claim URIs.
    .AddJwtBearer(options => options.MapInboundClaims = false);
builder.Services.AddKnowledgeAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, ClaimsCurrentUser>();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthEndpoints();
app.MapDocumentEndpoints();
app.MapQuestionEndpoints();

await app.RunAsync();
