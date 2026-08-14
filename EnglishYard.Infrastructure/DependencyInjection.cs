using EnglishYard.Application.Alunos;
using EnglishYard.Application.Aulas;
using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Calendario;
using EnglishYard.Application.Financeiro;
using EnglishYard.Application.Imagens;
using EnglishYard.Application.Professoras;
using EnglishYard.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EnglishYard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("EnglishYardDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A connection string 'EnglishYardDatabase' não foi configurada. " +
                "Use dotnet user-secrets ou uma variável de ambiente antes de iniciar a API.");
        }

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddScoped<IPerfilUsuarioRepository, PerfilUsuarioRepository>();
        services.AddScoped<ISupabaseAuthAdminGateway, SupabaseAuthAdminGateway>();
        services.AddScoped<IImagemStorageGateway, SupabaseStorageGateway>();
        services.AddScoped<AutenticacaoService>();
        services.AddScoped<IAlunoRepository, AlunoRepository>();
        services.AddScoped<IRegistroAulaRepository, RegistroAulaRepository>();
        services.AddScoped<RegistroAulaService>();
        services.AddScoped<AlunoService>();
        services.AddScoped<ICalendarioRepository, CalendarioRepository>();
        services.AddScoped<CalendarioService>();
        services.AddScoped<IFinanceiroRepository, FinanceiroRepository>();
        services.AddScoped<FinanceiroService>();
        services.AddScoped<IProfessoraRepository, ProfessoraRepository>();
        services.AddScoped<ProfessoraService>();

        return services;
    }
}
