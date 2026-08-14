namespace EnglishYard.Application.Financeiro;

public interface IFinanceiroRepository
{
    Task<FinanceiroResumoResponse> ObterResumoAsync(DateOnly competencia, CancellationToken cancellationToken);
    Task<DemonstrativoProfessoraResponse?> ObterDemonstrativoProfessoraAsync(Guid professoraId, DateOnly competencia, CancellationToken cancellationToken);
    Task RegistrarRecebimentoAsync(Guid alunoId, DateOnly competencia, RegistrarRecebimentoRequest request, CancellationToken cancellationToken);
    Task<DespesaFinanceiroResponse> CadastrarDespesaAsync(DateOnly competencia, CadastrarDespesaRequest request, CancellationToken cancellationToken);
    Task<bool> MarcarDespesaPagaAsync(Guid despesaId, MarcarDespesaPagaRequest request, CancellationToken cancellationToken);
}
