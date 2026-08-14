namespace EnglishYard.Application.Financeiro;

public interface IFinanceiroRepository
{
    Task<FinanceiroResumoResponse> ObterResumoAsync(DateOnly competencia, CancellationToken cancellationToken);
    Task<DemonstrativoProfessoraResponse?> ObterDemonstrativoProfessoraAsync(Guid professoraId, DateOnly competencia, CancellationToken cancellationToken);
    Task RegistrarRecebimentoAsync(Guid alunoId, DateOnly competencia, RegistrarRecebimentoRequest request, CancellationToken cancellationToken);
    Task<DespesaFinanceiroResponse> CadastrarDespesaAsync(DateOnly competencia, CadastrarDespesaRequest request, CancellationToken cancellationToken);
    Task<bool> MarcarDespesaPagaAsync(Guid despesaId, MarcarDespesaPagaRequest request, CancellationToken cancellationToken);
    Task<bool> EstornarRecebimentoAsync(Guid recebimentoId, string motivo, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> AjustarMensalidadeAsync(Guid mensalidadeId, decimal desconto, string motivo, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> CancelarMensalidadeAsync(Guid mensalidadeId, string motivo, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> AtualizarDespesaAsync(Guid despesaId, AtualizarDespesaRequest request, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> CancelarDespesaAsync(Guid despesaId, string motivo, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> ReabrirDespesaAsync(Guid despesaId, string motivo, Guid usuarioId, CancellationToken cancellationToken);
    Task<Guid> CriarAjusteProfessoraAsync(Guid professoraId, DateOnly competencia, CriarAjusteProfessoraRequest request, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> ExcluirAjusteProfessoraAsync(Guid ajusteId, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> AprovarFechamentoAsync(Guid professoraId, DateOnly competencia, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> MarcarFechamentoPagoAsync(Guid professoraId, DateOnly competencia, MarcarFechamentoPagoRequest request, Guid usuarioId, CancellationToken cancellationToken);
    Task<bool> ReabrirFechamentoAsync(Guid professoraId, DateOnly competencia, string motivo, Guid usuarioId, CancellationToken cancellationToken);
    Task<PoliticaPagamentoResponse?> ObterPoliticaPagamentoAsync(DateOnly data, CancellationToken cancellationToken);
    Task<PoliticaPagamentoResponse> SalvarPoliticaPagamentoAsync(SalvarPoliticaPagamentoRequest request, Guid usuarioId, CancellationToken cancellationToken);
}
