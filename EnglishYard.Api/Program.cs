using EnglishYard.Api.Authentication;
using EnglishYard.Infrastructure;
using EnglishYard.Application.Autenticacao;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Configuração local de desenvolvimento.
// O arquivo appsettings.Local.json é ignorado pelo Git e pode guardar
// as chaves do Supabase sem exigir scripts de User Secrets.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile(
        "appsettings.Local.json",
        optional: true,
        reloadOnChange: true);
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddAuthentication(SupabaseAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SupabaseAuthenticationHandler>(
        SupabaseAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
var frontendUrl = builder.Configuration["Frontend:Url"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = new List<string>
        {
            "http://localhost:4200",
            "https://localhost:4200",
            "https://front-englishyard.vercel.app"
        };

        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            origins.Add(frontendUrl.TrimEnd('/'));
        }

        policy
            .WithOrigins(origins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var mustChangePassword = context.User.Identity?.IsAuthenticated == true
        && string.Equals(context.User.FindFirst(PortalClaimTypes.DeveAlterarSenha)?.Value, "true", StringComparison.OrdinalIgnoreCase);

    var authenticationEndpoint = context.Request.Path.StartsWithSegments("/api/autenticacao");
    if (mustChangePassword && !authenticationEndpoint)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            title = "Troca de senha obrigatória",
            status = StatusCodes.Status403Forbidden,
            detail = "Defina sua senha definitiva antes de acessar os demais módulos do portal."
        });
        return;
    }

    await next();
});
app.UseAuthorization();
app.MapControllers();

app.Run();
