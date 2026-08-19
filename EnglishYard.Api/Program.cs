using EnglishYard.Api.Authentication;
using EnglishYard.Infrastructure;
using EnglishYard.Application.Autenticacao;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // O Render atua como proxy reverso antes do container.
    // Limpar as listas permite respeitar os headers X-Forwarded-* enviados pela plataforma.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddAuthentication(SupabaseAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SupabaseAuthenticationHandler>(
        SupabaseAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
var frontendUrl = builder.Configuration["Frontend:Url"]?.TrimEnd('/');

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = new List<string>
        {
            "http://localhost:4200",
            "https://localhost:4200",
            "https://front-englishyard.vercel.app",
            "https://www.englishyard.com.br",
            "https://englishyard.com.br",
        };

        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            origins.Add(frontendUrl);
        }

        policy
            .WithOrigins(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "EnglishYard.Api"
})).AllowAnonymous();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "EnglishYard.Api"
})).AllowAnonymous();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703" or "42883")
    {
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            title = "Estrutura do banco de dados desatualizada",
            status = StatusCodes.Status503ServiceUnavailable,
            detail = "A estrutura financeira necessária não foi encontrada. Execute, nesta ordem, as migrações 21_INTEGRIDADE_FINANCEIRA_E_HISTORICA.sql e 22_CONTROLE_SESSOES.sql."
        });
    }
});
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
