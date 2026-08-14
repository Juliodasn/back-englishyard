using EnglishYard.Application.Autenticacao;
using EnglishYard.Application.Imagens;

namespace EnglishYard.Application.Professoras;

public sealed class ProfessoraService(
    IProfessoraRepository repository,
    ISupabaseAuthAdminGateway authAdminGateway,
    IImagemStorageGateway imagemStorage)
{
    private static readonly HashSet<string> StatusPermitidos = ["Ativa", "Em onboarding", "Em férias", "Pausada"];
    private static readonly HashSet<string> TiposChavePixPermitidos = ["CPF", "E-mail", "Telefone", "Chave aleatória"];

    public async Task<IReadOnlyList<ProfessoraResponse>> ListarAsync(CancellationToken cancellationToken)
    {
        var professoras = await repository.ListarAsync(cancellationToken);
        return professoras.Select(ProfessoraResponse.FromEntity).ToArray();
    }


    public async Task<ProfessoraListagemPaginadaResponse> ListarPaginadoAsync(
        string? busca,
        string? status,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken)
    {
        pagina = Math.Max(1, pagina);
        tamanhoPagina = tamanhoPagina is 10 or 25 or 50 ? tamanhoPagina : 10;
        busca = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim();
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

        if (status is not null && !StatusPermitidos.Contains(status))
            throw new ProfessoraValidationException("Status de filtro inválido.");

        var (itens, total) = await repository.ListarPaginadoAsync(
            busca,
            status,
            pagina,
            tamanhoPagina,
            cancellationToken);

        var totalPaginas = total == 0 ? 0 : (int)Math.Ceiling(total / (double)tamanhoPagina);
        return new ProfessoraListagemPaginadaResponse(
            itens.Select(ProfessoraResponse.FromEntity).ToArray(),
            pagina,
            tamanhoPagina,
            total,
            totalPaginas);
    }

    public async Task<IReadOnlyList<ProfessoraExportacaoResponse>> ListarExportacaoAsync(
        string? busca,
        string? status,
        CancellationToken cancellationToken)
    {
        busca = string.IsNullOrWhiteSpace(busca) ? null : busca.Trim();
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

        if (status is not null && !StatusPermitidos.Contains(status))
            throw new ProfessoraValidationException("Status de filtro inválido.");

        var professoras = await repository.ListarExportacaoAsync(busca, status, cancellationToken);
        return professoras.Select(p => new ProfessoraExportacaoResponse(
            p.Nome,
            p.Email,
            p.Telefone,
            p.QuantidadeAlunos,
            p.QuantidadeAulas,
            p.ValorAulaIndividual,
            p.ValorAulaGrupo,
            p.Status)).ToArray();
    }

    public async Task<ProfessoraResponse> BuscarPorIdAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        var professora = await repository.BuscarPorIdAsync(professoraId, cancellationToken)
            ?? throw new ProfessoraNaoEncontradaException("Professora não encontrada ou inativa.");
        return ProfessoraResponse.FromEntity(professora);
    }

    public Task<IReadOnlyList<ValorAulaProfessoraHistoricoResponse>> ListarHistoricoValoresAsync(
        Guid professoraId,
        CancellationToken cancellationToken) =>
        repository.ListarHistoricoValoresAsync(professoraId, cancellationToken);

    public async Task<ProfessoraResponse> AtualizarFotoAsync(
        Guid professoraId,
        Stream conteudo,
        string contentType,
        long tamanho,
        CancellationToken cancellationToken)
    {
        var erro = ImagemPerfilValidator.ObterErro(contentType, tamanho);
        if (erro is not null)
            throw new ProfessoraValidationException(erro);

        var fotoUrl = await imagemStorage.SalvarFotoPerfilAsync(
            "professoras",
            professoraId,
            conteudo,
            contentType,
            cancellationToken);

        if (!await repository.AtualizarFotoUrlAsync(professoraId, fotoUrl, cancellationToken))
            throw new ProfessoraNaoEncontradaException("Professora não encontrada ou inativa.");

        var atualizada = await repository.BuscarPorIdAsync(professoraId, cancellationToken)
            ?? throw new ProfessoraNaoEncontradaException("A foto foi enviada, mas a professora não pôde ser recarregada do banco.");

        if (string.IsNullOrWhiteSpace(atualizada.FotoUrl))
            throw new ImagemStorageException("A foto foi enviada, mas a URL não foi persistida no cadastro da professora.");

        return ProfessoraResponse.FromEntity(atualizada);
    }

    public async Task<ProfessoraResponse> AtualizarAsync(
        Guid professoraId,
        AtualizarProfessoraRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        Validate(normalized);

        var atualizada = await repository.AtualizarAsync(professoraId, normalized, cancellationToken)
            ?? throw new ProfessoraNaoEncontradaException("Professora não encontrada ou inativa.");

        return ProfessoraResponse.FromEntity(atualizada);
    }

    public async Task<ProfessoraResponse> AtualizarPerfilProprioAsync(
        Guid professoraId,
        AtualizarMeuPerfilProfessoraRequest request,
        CancellationToken cancellationToken)
    {
        var nomeProfissional = NullIfWhiteSpace(request.NomeProfissional);
        var telefone = NullIfWhiteSpace(request.Telefone);

        if (string.IsNullOrWhiteSpace(telefone))
            throw new ProfessoraValidationException("Informe o telefone da professora.");

        if (nomeProfissional is { Length: > 120 })
            throw new ProfessoraValidationException("O nome profissional deve ter no máximo 120 caracteres.");

        if (telefone.Length > 40)
            throw new ProfessoraValidationException("O telefone deve ter no máximo 40 caracteres.");

        var atualizada = await repository.AtualizarPerfilProprioAsync(
            professoraId,
            nomeProfissional,
            telefone,
            cancellationToken)
            ?? throw new ProfessoraNaoEncontradaException("Professora não encontrada ou inativa.");

        return ProfessoraResponse.FromEntity(atualizada);
    }

    public async Task<bool> ExcluirAsync(Guid professoraId, CancellationToken cancellationToken)
    {
        var usuarioAuthId = await repository.ObterUsuarioAuthIdAsync(professoraId, cancellationToken);
        var excluida = await repository.ExcluirAsync(professoraId, cancellationToken);
        if (!excluida)
            return false;

        if (usuarioAuthId.HasValue)
        {
            try
            {
                await authAdminGateway.ExcluirUsuarioAsync(usuarioAuthId.Value, cancellationToken);
            }
            catch (Exception exception) when (exception is SupabaseAuthException or SupabaseAuthConfigurationException)
            {
                // O perfil local já está inativo, portanto a conta não consegue acessar a API.
                // A limpeza do Auth pode ser repetida posteriormente sem reativar o cadastro.
            }
        }

        return true;
    }

    public async Task CriarAcessoAsync(Guid professoraId, CriarAcessoProfessoraRequest request, CancellationToken cancellationToken)
    {
        var dados = await repository.ObterDadosParaAcessoAsync(professoraId, cancellationToken)
            ?? throw new ProfessoraValidationException("Professora não encontrada ou inativa.");

        if (await repository.PossuiPerfilAcessoAtivoAsync(professoraId, cancellationToken))
            throw new ProfessoraConflitoException("Esta professora já possui uma conta de acesso ativa.");

        var senha = request.SenhaInicial ?? string.Empty;
        var senhaError = SenhaPortalValidator.ObterErro(senha);
        if (senhaError is not null)
            throw new ProfessoraValidationException(senhaError);

        Guid? usuarioAuthId = null;
        try
        {
            usuarioAuthId = await authAdminGateway.CriarUsuarioAsync(dados.Email, senha, dados.Nome, cancellationToken);
            await repository.CriarPerfilAcessoAsync(professoraId, usuarioAuthId.Value, dados.Nome, dados.Email, cancellationToken);
        }
        catch (SupabaseAuthConflitoException exception)
        {
            throw new ProfessoraConflitoException(exception.Message);
        }
        catch
        {
            if (usuarioAuthId.HasValue)
            {
                try { await authAdminGateway.ExcluirUsuarioAsync(usuarioAuthId.Value, cancellationToken); }
                catch { /* compensação best-effort */ }
            }
            throw;
        }
    }

    public async Task<ProfessoraResponse> CadastrarAsync(CadastrarProfessoraRequest request, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        Validate(normalized);

        if (await repository.EmailExisteAsync(normalized.Email!, cancellationToken))
            throw new ProfessoraConflitoException("Já existe uma professora cadastrada com este e-mail.");

        // If the previous deletion completed locally but its Auth cleanup failed,
        // remove only the account still linked to that inactive teacher profile.
        // Looking up an Auth user by email alone would risk deleting an admin.
        var usuarioAuthInativoId = await repository.ObterUsuarioAuthIdInativoPorEmailAsync(
            normalized.Email!,
            cancellationToken);
        if (usuarioAuthInativoId.HasValue)
            await authAdminGateway.ExcluirUsuarioAsync(usuarioAuthInativoId.Value, cancellationToken);

        Guid? usuarioAuthId = null;
        try
        {
            usuarioAuthId = await authAdminGateway.CriarUsuarioAsync(
                normalized.Email!,
                normalized.SenhaInicial,
                normalized.Nome,
                cancellationToken);

            var professora = await repository.CadastrarAsync(normalized, usuarioAuthId.Value, cancellationToken);
            return ProfessoraResponse.FromEntity(professora);
        }
        catch (SupabaseAuthConflitoException exception)
        {
            throw new ProfessoraConflitoException(exception.Message);
        }
        catch
        {
            if (usuarioAuthId.HasValue)
            {
                try
                {
                    await authAdminGateway.ExcluirUsuarioAsync(usuarioAuthId.Value, cancellationToken);
                }
                catch
                {
                    // Compensação best-effort. Preserva o erro original do cadastro.
                }
            }

            throw;
        }
    }

    private static CadastrarProfessoraRequest Normalize(CadastrarProfessoraRequest request) => request with
    {
        Nome = request.Nome.Trim(),
        NomeProfissional = NullIfWhiteSpace(request.NomeProfissional),
        DocumentoIdentidade = NullIfWhiteSpace(request.DocumentoIdentidade),
        Email = NullIfWhiteSpace(request.Email)?.ToLowerInvariant(),
        SenhaInicial = request.SenhaInicial ?? string.Empty,
        Telefone = NullIfWhiteSpace(request.Telefone),
        Status = string.IsNullOrWhiteSpace(request.Status) ? "Ativa" : request.Status.Trim(),
        ModeloPagamento = string.IsNullOrWhiteSpace(request.ModeloPagamento) ? "Por aula" : request.ModeloPagamento.Trim(),
        TipoChavePix = NullIfWhiteSpace(request.TipoChavePix),
        ChavePix = NullIfWhiteSpace(request.ChavePix),
        Banco = NullIfWhiteSpace(request.Banco),
        Observacoes = NullIfWhiteSpace(request.Observacoes)
    };

    private static AtualizarProfessoraRequest Normalize(AtualizarProfessoraRequest request) => request with
    {
        Nome = request.Nome.Trim(),
        NomeProfissional = NullIfWhiteSpace(request.NomeProfissional),
        DocumentoIdentidade = NullIfWhiteSpace(request.DocumentoIdentidade),
        Telefone = NullIfWhiteSpace(request.Telefone),
        Status = string.IsNullOrWhiteSpace(request.Status) ? "Ativa" : request.Status.Trim(),
        ModeloPagamento = string.IsNullOrWhiteSpace(request.ModeloPagamento) ? "Por aula" : request.ModeloPagamento.Trim(),
        TipoChavePix = NullIfWhiteSpace(request.TipoChavePix),
        ChavePix = NullIfWhiteSpace(request.ChavePix),
        Banco = NullIfWhiteSpace(request.Banco),
        Observacoes = NullIfWhiteSpace(request.Observacoes)
    };

    private static void Validate(CadastrarProfessoraRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
            throw new ProfessoraValidationException("Informe o nome da professora.");

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            throw new ProfessoraValidationException("Informe um e-mail de acesso válido para a professora.");

        var senhaError = SenhaPortalValidator.ObterErro(request.SenhaInicial);
        if (senhaError is not null)
            throw new ProfessoraValidationException(senhaError);

        if (string.IsNullOrWhiteSpace(request.Telefone))
            throw new ProfessoraValidationException("Informe o telefone da professora.");

        if (!StatusPermitidos.Contains(request.Status))
            throw new ProfessoraValidationException("Status da professora inválido.");

        if (request.TipoChavePix is not null && !TiposChavePixPermitidos.Contains(request.TipoChavePix))
            throw new ProfessoraValidationException("Tipo de chave PIX inválido.");

        if (request.ValorAulaIndividual < 0 || request.ValorAulaGrupo < 0)
            throw new ProfessoraValidationException("Os valores de aula não podem ser negativos.");

        if (request.DiaPagamento is < 1 or > 31)
            throw new ProfessoraValidationException("O dia de pagamento deve estar entre 1 e 31.");

        if (string.IsNullOrWhiteSpace(request.ChavePix))
            throw new ProfessoraValidationException("Informe a chave PIX da professora.");
    }

    private static void Validate(AtualizarProfessoraRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nome))
            throw new ProfessoraValidationException("Informe o nome da professora.");

        if (string.IsNullOrWhiteSpace(request.Telefone))
            throw new ProfessoraValidationException("Informe o telefone da professora.");

        if (!StatusPermitidos.Contains(request.Status))
            throw new ProfessoraValidationException("Status da professora inválido.");

        if (request.TipoChavePix is not null && !TiposChavePixPermitidos.Contains(request.TipoChavePix))
            throw new ProfessoraValidationException("Tipo de chave PIX inválido.");

        if (request.ValorAulaIndividual < 0 || request.ValorAulaGrupo < 0)
            throw new ProfessoraValidationException("Os valores de aula não podem ser negativos.");

        if (request.DiaPagamento is < 1 or > 31)
            throw new ProfessoraValidationException("O dia de pagamento deve estar entre 1 e 31.");

        if (string.IsNullOrWhiteSpace(request.ChavePix))
            throw new ProfessoraValidationException("Informe a chave PIX da professora.");
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ProfessoraValidationException(string message) : Exception(message);
public sealed class ProfessoraConflitoException(string message) : Exception(message);
public sealed class ProfessoraNaoEncontradaException(string message) : Exception(message);
