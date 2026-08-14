namespace EnglishYard.Application.Financeiro;

public sealed record MensalidadeFinanceiroResponse(
    Guid? MensalidadeId,
    Guid AlunoId,
    string AlunoNome,
    string Descricao,
    decimal ValorOriginal,
    decimal Desconto,
    decimal ValorFinal,
    decimal ValorRecebido,
    decimal Saldo,
    DateOnly DataVencimento,
    string Status,
    string? FormaPagamentoPrevista);

public sealed record DespesaFinanceiroResponse(
    Guid Id,
    string Descricao,
    string Categoria,
    string? Fornecedor,
    decimal Valor,
    DateOnly Competencia,
    DateOnly DataVencimento,
    DateOnly? DataPagamento,
    string Status,
    string Recorrencia,
    string? FormaPagamento,
    string? Observacoes);

public sealed record MovimentoFinanceiroResponse(
    Guid Id,
    DateOnly Data,
    string Tipo,
    string Descricao,
    string Categoria,
    decimal Valor);

public sealed record PagamentoProfessoraResumoResponse(
    Guid ProfessoraId,
    string Nome,
    string Email,
    short DiaPagamento,
    decimal ValorAulaIndividual,
    decimal ValorAulaGrupo,
    int AulasPrevistas,
    int AulasRealizadas,
    int AulasAplicadas,
    int FaltasAluno,
    int ReposicoesRealizadas,
    int AulasRemarcadas,
    int AulasIndividuais,
    int AulasGrupo,
    decimal ValorPrevisto,
    decimal ValorRealizado,
    decimal Ajustes,
    decimal ValorTotal,
    string StatusFechamento,
    DateOnly? DataPagamento);

public sealed record FinanceiroResumoResponse(
    string Competencia,
    decimal ReceitaPrevista,
    decimal ReceitaRecebida,
    decimal ReceitaPendente,
    decimal ReceitaVencida,
    decimal DespesasPrevistas,
    decimal DespesasPagas,
    decimal PagamentoProfessorasPrevisto,
    decimal PagamentoProfessorasPago,
    decimal ResultadoPrevisto,
    decimal ResultadoRealizado,
    int AlunosAtivos,
    int MensalidadesPagas,
    int MensalidadesPendentes,
    IReadOnlyList<MensalidadeFinanceiroResponse> Mensalidades,
    IReadOnlyList<DespesaFinanceiroResponse> Despesas,
    IReadOnlyList<MovimentoFinanceiroResponse> Movimentos,
    IReadOnlyList<PagamentoProfessoraResumoResponse> Professoras);

public sealed record AulaPagamentoProfessoraResponse(
    string Id,
    DateOnly Data,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    string Tipo,
    IReadOnlyList<string> Alunos,
    string Status,
    bool PossuiRegistroReal,
    decimal ValorPrevisto,
    decimal ValorRealizado);

public sealed record DemonstrativoProfessoraResponse(
    Guid ProfessoraId,
    string Nome,
    string Email,
    short DiaPagamento,
    string ModeloPagamento,
    string? TipoChavePix,
    string? ChavePix,
    string? Banco,
    decimal ValorAulaIndividual,
    decimal ValorAulaGrupo,
    string Competencia,
    int AulasPrevistas,
    int AulasRealizadas,
    int AulasAplicadas,
    int FaltasAluno,
    int ReposicoesRealizadas,
    int AulasRemarcadas,
    int AulasIndividuais,
    int AulasGrupo,
    decimal ValorPrevisto,
    decimal ValorRealizado,
    decimal Ajustes,
    decimal ValorTotal,
    string StatusFechamento,
    DateOnly? DataPagamento,
    IReadOnlyList<AulaPagamentoProfessoraResponse> Aulas);

public sealed record RegistrarRecebimentoRequest(
    decimal Valor,
    DateOnly? DataRecebimento,
    string FormaPagamento,
    string? Observacao);

public sealed record CadastrarDespesaRequest(
    string Descricao,
    string Categoria,
    string? Fornecedor,
    decimal Valor,
    DateOnly DataVencimento,
    string Recorrencia,
    string? FormaPagamento,
    string? Observacoes,
    int? QuantidadeMeses = null);

public sealed record MarcarDespesaPagaRequest(
    DateOnly? DataPagamento,
    string? FormaPagamento);
